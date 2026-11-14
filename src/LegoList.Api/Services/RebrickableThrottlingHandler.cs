using System.Net;

namespace LegoList.Services;

/// <summary>
/// Sends every Rebrickable request through <see cref="RebrickableRateLimiter"/> and retries
/// HTTP 429 responses (honoring Retry-After) a few times before giving up.
/// </summary>
public class RebrickableThrottlingHandler(
    RebrickableRateLimiter limiter,
    ILogger<RebrickableThrottlingHandler> logger) : DelegatingHandler
{
    public int MaxRetries { get; init; } = 3;

    /// <summary>A Retry-After longer than this isn't waited out; the 429 is returned instead.</summary>
    public TimeSpan MaxRetryDelay { get; init; } = TimeSpan.FromSeconds(10);

    public Func<TimeSpan, CancellationToken, Task> Delay { get; init; } = Task.Delay;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            await limiter.WaitAsync(ct);

            // A request message can only be sent once, so retries go out as copies.
            var response = await base.SendAsync(attempt == 0 ? request : Clone(request), ct);

            if (response.StatusCode != HttpStatusCode.TooManyRequests || attempt >= MaxRetries)
                return response;

            var wait = GetRetryDelay(response, attempt);
            if (wait > MaxRetryDelay)
            {
                logger.LogWarning("Rebrickable rate limit: Retry-After {Wait} is too long; not retrying", wait);
                return response;
            }

            logger.LogInformation("Rebrickable returned 429; retrying in {Wait} (attempt {Attempt})", wait, attempt + 1);
            response.Dispose();
            await Delay(wait, ct);
        }
    }

    private static TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta)
            return delta;
        if (retryAfter?.Date is { } date)
        {
            var until = date - DateTimeOffset.UtcNow;
            return until > TimeSpan.Zero ? until : TimeSpan.Zero;
        }
        return TimeSpan.FromSeconds(Math.Pow(2, attempt)); // 1s, 2s, 4s
    }

    private static HttpRequestMessage Clone(HttpRequestMessage original)
    {
        var copy = new HttpRequestMessage(original.Method, original.RequestUri) { Version = original.Version };
        foreach (var header in original.Headers)
            copy.Headers.TryAddWithoutValidation(header.Key, header.Value);
        return copy;
    }
}
