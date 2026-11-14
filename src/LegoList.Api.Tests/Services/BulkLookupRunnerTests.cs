using LegoList.Blazor.Services;
using Xunit;

namespace LegoList.Api.Tests.Services;

public class BulkLookupRunnerTests
{
    private static readonly TimeSpan Pause = TimeSpan.FromSeconds(7);

    /// <summary>A runner whose retry pause is recorded instead of waited out.</summary>
    private static (BulkLookupRunner runner, List<TimeSpan> pauses) CreateRunner()
    {
        var pauses = new List<TimeSpan>();
        var runner = new BulkLookupRunner(Pause, (span, _) =>
        {
            pauses.Add(span);
            return Task.CompletedTask;
        });
        return (runner, pauses);
    }

    [Fact]
    public async Task ProcessesEverySetInOrder_AndCountsOutcomes()
    {
        var (runner, pauses) = CreateRunner();
        var seen = new List<int>();

        var result = await runner.RunAsync(
            [1, 2, 3],
            (id, _) =>
            {
                seen.Add(id);
                return Task.FromResult(id == 2 ? LookupOutcome.NotFound : LookupOutcome.Updated);
            },
            null,
            CancellationToken.None);

        Assert.Equal([1, 2, 3], seen);
        Assert.Equal(new BulkLookupResult(Updated: 2, NotFound: 1, Remaining: 0, Cancelled: false, StoppedByFailures: false), result);
        Assert.Empty(pauses); // no extra waiting between successful lookups; the API paces the calls
    }

    [Fact]
    public async Task DoesNothing_WhenThereAreNoSets()
    {
        var (runner, _) = CreateRunner();
        var calls = 0;

        var result = await runner.RunAsync([], (_, _) => { calls++; return Task.FromResult(LookupOutcome.Updated); }, null, CancellationToken.None);

        Assert.Equal(0, calls);
        Assert.Equal(new BulkLookupResult(0, 0, 0, false, false), result);
    }

    [Fact]
    public async Task ReportsProgress_BeforeEachSet()
    {
        var (runner, _) = CreateRunner();
        var progress = new List<(int Position, int Total, int Id)>();

        await runner.RunAsync(
            [10, 20],
            (_, _) => Task.FromResult(LookupOutcome.Updated),
            (position, total, id) => progress.Add((position, total, id)),
            CancellationToken.None);

        Assert.Equal([(1, 2, 10), (2, 2, 20)], progress);
    }

    [Fact]
    public async Task RetriesAFailedSetOnce_AfterAPause_AndCarriesOn()
    {
        var (runner, pauses) = CreateRunner();
        var attempts = new Dictionary<int, int>();

        var result = await runner.RunAsync(
            [1, 2],
            (id, _) =>
            {
                attempts[id] = attempts.GetValueOrDefault(id) + 1;
                // Set 1 fails the first time only.
                return Task.FromResult(id == 1 && attempts[id] == 1 ? LookupOutcome.Failed : LookupOutcome.Updated);
            },
            null,
            CancellationToken.None);

        Assert.Equal(2, attempts[1]);
        Assert.Equal(1, attempts[2]);
        Assert.Equal([Pause], pauses);
        Assert.Equal(2, result.Updated);
        Assert.False(result.StoppedByFailures);
    }

    [Fact]
    public async Task StopsWhenARetryFailsToo_LeavingTheRestUntouched()
    {
        var (runner, pauses) = CreateRunner();
        var seen = new List<int>();

        var result = await runner.RunAsync(
            [1, 2, 3, 4],
            (id, _) =>
            {
                seen.Add(id);
                return Task.FromResult(id == 3 ? LookupOutcome.Failed : LookupOutcome.Updated);
            },
            null,
            CancellationToken.None);

        Assert.Equal([1, 2, 3, 3], seen); // set 3 tried twice, set 4 never reached
        Assert.Single(pauses);
        Assert.True(result.StoppedByFailures);
        Assert.False(result.Cancelled);
        Assert.Equal(2, result.Updated);
        Assert.Equal(2, result.Remaining); // the failed set and the one after it
    }

    [Fact]
    public async Task DoesNotRetry_ASetRebrickableDoesNotKnow()
    {
        var (runner, pauses) = CreateRunner();
        var calls = 0;

        var result = await runner.RunAsync(
            [1],
            (_, _) => { calls++; return Task.FromResult(LookupOutcome.NotFound); },
            null,
            CancellationToken.None);

        Assert.Equal(1, calls);
        Assert.Empty(pauses);
        Assert.Equal(1, result.NotFound);
    }

    [Fact]
    public async Task Cancelling_BetweenSets_StopsAndReportsWhatIsLeft()
    {
        var (runner, _) = CreateRunner();
        using var cts = new CancellationTokenSource();
        var seen = new List<int>();

        var result = await runner.RunAsync(
            [1, 2, 3],
            (id, _) =>
            {
                seen.Add(id);
                if (id == 1) cts.Cancel();
                return Task.FromResult(LookupOutcome.Updated);
            },
            null,
            cts.Token);

        Assert.Equal([1], seen);
        Assert.True(result.Cancelled);
        Assert.Equal(1, result.Updated);
        Assert.Equal(2, result.Remaining);
    }

    [Fact]
    public async Task Cancelling_DuringALookup_CountsThatSetAsNotDone()
    {
        var (runner, _) = CreateRunner();
        using var cts = new CancellationTokenSource();

        var result = await runner.RunAsync(
            [1, 2],
            (id, token) =>
            {
                cts.Cancel();
                token.ThrowIfCancellationRequested();
                return Task.FromResult(LookupOutcome.Updated);
            },
            null,
            cts.Token);

        Assert.True(result.Cancelled);
        Assert.Equal(0, result.Updated);
        Assert.Equal(2, result.Remaining);
    }

    [Fact]
    public async Task Cancelling_DuringTheRetryPause_StopsWithoutRetrying()
    {
        using var cts = new CancellationTokenSource();
        var runner = new BulkLookupRunner(Pause, (_, token) =>
        {
            cts.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        });
        var calls = 0;

        var result = await runner.RunAsync(
            [1],
            (_, _) => { calls++; return Task.FromResult(LookupOutcome.Failed); },
            null,
            cts.Token);

        Assert.Equal(1, calls);
        Assert.True(result.Cancelled);
        Assert.False(result.StoppedByFailures);
        Assert.Equal(1, result.Remaining);
    }
}
