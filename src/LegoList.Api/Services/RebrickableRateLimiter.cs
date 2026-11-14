namespace LegoList.Services;

/// <summary>
/// Spaces outgoing Rebrickable requests at least <c>minInterval</c> apart, process-wide.
/// Callers queue up (in order) instead of failing when sets are added in quick succession.
/// </summary>
public class RebrickableRateLimiter
{
    private readonly TimeSpan minInterval;
    private readonly Func<DateTimeOffset> clock;
    private readonly Func<TimeSpan, CancellationToken, Task> delay;
    private readonly SemaphoreSlim gate = new(1, 1);
    private DateTimeOffset next = DateTimeOffset.MinValue;

    public RebrickableRateLimiter(
        TimeSpan minInterval,
        Func<DateTimeOffset>? clock = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        this.minInterval = minInterval;
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
        this.delay = delay ?? Task.Delay;
    }

    /// <summary>Completes when the caller may send its request.</summary>
    public async Task WaitAsync(CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            var now = clock();
            var slot = next > now ? next : now;
            var wait = slot - now;
            if (wait > TimeSpan.Zero)
                await delay(wait, ct);
            next = slot + minInterval;
        }
        finally
        {
            gate.Release();
        }
    }
}
