namespace LegoList.Blazor.Services;

public enum LookupOutcome
{
    /// <summary>Rebrickable answered and the set's details were filled in.</summary>
    Updated,

    /// <summary>Rebrickable has no record of the set; asking again won't help.</summary>
    NotFound,

    /// <summary>Rebrickable couldn't be reached (rate limit or outage); worth another try later.</summary>
    Failed
}

public record BulkLookupResult(int Updated, int NotFound, int Remaining, bool Cancelled, bool StoppedByFailures);

/// <summary>
/// Works through a queue of sets that are missing details, one at a time. The API already spaces its
/// Rebrickable calls to stay under the rate limit, so this only has to avoid piling on when Rebrickable
/// pushes back: a failed lookup is retried once after a pause, and a second failure stops the run
/// (the rest would very likely fail too) so the user can try again later.
/// </summary>
public class BulkLookupRunner(
    TimeSpan? retryDelay = null,
    Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    private readonly TimeSpan retryDelay = retryDelay ?? TimeSpan.FromSeconds(5);
    private readonly Func<TimeSpan, CancellationToken, Task> delay = delay ?? Task.Delay;

    /// <param name="setIds">The sets to look up, in order.</param>
    /// <param name="lookup">Looks up one set. Throwing <see cref="OperationCanceledException"/> counts as cancellation.</param>
    /// <param name="progress">Called before each set with (position starting at 1, total, set id).</param>
    public async Task<BulkLookupResult> RunAsync(
        IReadOnlyList<int> setIds,
        Func<int, CancellationToken, Task<LookupOutcome>> lookup,
        Action<int, int, int>? progress,
        CancellationToken cancellationToken)
    {
        var updated = 0;
        var notFound = 0;

        for (var i = 0; i < setIds.Count; i++)
        {
            if (cancellationToken.IsCancellationRequested)
                return new BulkLookupResult(updated, notFound, setIds.Count - i, Cancelled: true, StoppedByFailures: false);

            var id = setIds[i];
            progress?.Invoke(i + 1, setIds.Count, id);

            try
            {
                var outcome = await lookup(id, cancellationToken);
                if (outcome == LookupOutcome.Failed)
                {
                    await delay(retryDelay, cancellationToken);
                    outcome = await lookup(id, cancellationToken);
                }

                switch (outcome)
                {
                    case LookupOutcome.Updated:
                        updated++;
                        break;
                    case LookupOutcome.NotFound:
                        notFound++;
                        break;
                    default:
                        return new BulkLookupResult(updated, notFound, setIds.Count - i, Cancelled: false, StoppedByFailures: true);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return new BulkLookupResult(updated, notFound, setIds.Count - i, Cancelled: true, StoppedByFailures: false);
            }
        }

        return new BulkLookupResult(updated, notFound, Remaining: 0, Cancelled: false, StoppedByFailures: false);
    }
}
