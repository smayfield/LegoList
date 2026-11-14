using LegoList.Services;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Net.Http.Headers;
using Xunit;

namespace LegoList.Api.Tests.Services;

public class RebrickableThrottlingTests
{
    // ── Rate limiter ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Limiter_DoesNotWait_ForTheFirstRequest()
    {
        var delays = new List<TimeSpan>();
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var limiter = new RebrickableRateLimiter(TimeSpan.FromSeconds(1), () => now,
            (d, _) => { delays.Add(d); now += d; return Task.CompletedTask; });

        await limiter.WaitAsync();

        Assert.Empty(delays);
    }

    [Fact]
    public async Task Limiter_SpacesBackToBackRequestsByTheInterval()
    {
        var delays = new List<TimeSpan>();
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var limiter = new RebrickableRateLimiter(TimeSpan.FromSeconds(1), () => now,
            (d, _) => { delays.Add(d); now += d; return Task.CompletedTask; });

        await limiter.WaitAsync();
        await limiter.WaitAsync();
        await limiter.WaitAsync();

        Assert.Equal(new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1) }, delays);
    }

    [Fact]
    public async Task Limiter_DoesNotWait_WhenEnoughTimeHasPassed()
    {
        var delays = new List<TimeSpan>();
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var limiter = new RebrickableRateLimiter(TimeSpan.FromSeconds(1), () => now,
            (d, _) => { delays.Add(d); now += d; return Task.CompletedTask; });

        await limiter.WaitAsync();
        now += TimeSpan.FromSeconds(5);
        await limiter.WaitAsync();

        Assert.Empty(delays);
    }

    // ── Retry handler ────────────────────────────────────────────────────────

    private static (HttpClient client, List<TimeSpan> delays, Func<int> calls) CreateClient(
        Func<int, HttpResponseMessage> respond, int maxRetries = 3)
    {
        var delays = new List<TimeSpan>();
        var calls = 0;
        // A limiter with no spacing, so these tests only exercise the retry behavior.
        var limiter = new RebrickableRateLimiter(TimeSpan.Zero);
        var handler = new RebrickableThrottlingHandler(limiter, NullLogger<RebrickableThrottlingHandler>.Instance)
        {
            MaxRetries = maxRetries,
            Delay = (d, _) => { delays.Add(d); return Task.CompletedTask; },
            InnerHandler = new StubHttpMessageHandler(_ => respond(++calls))
        };
        return (new HttpClient(handler) { BaseAddress = new Uri("https://rebrickable.com/") }, delays, () => calls);
    }

    private static HttpResponseMessage TooManyRequests(TimeSpan? retryAfter = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        if (retryAfter is not null)
            response.Headers.RetryAfter = new RetryConditionHeaderValue(retryAfter.Value);
        return response;
    }

    [Fact]
    public async Task Handler_RetriesRateLimitedRequest_ThenSucceeds()
    {
        var (client, delays, calls) = CreateClient(n => n < 3 ? TooManyRequests() : new HttpResponseMessage(HttpStatusCode.OK));

        var response = await client.GetAsync("api/v3/lego/sets/75192-1/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, calls());
        Assert.Equal(new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2) }, delays); // exponential backoff
    }

    [Fact]
    public async Task Handler_HonorsRetryAfterHeader()
    {
        var (client, delays, _) = CreateClient(n => n == 1 ? TooManyRequests(TimeSpan.FromSeconds(4)) : new HttpResponseMessage(HttpStatusCode.OK));

        await client.GetAsync("api/v3/lego/sets/75192-1/");

        Assert.Equal(new[] { TimeSpan.FromSeconds(4) }, delays);
    }

    [Fact]
    public async Task Handler_GivesUp_AfterMaxRetries()
    {
        var (client, _, calls) = CreateClient(_ => TooManyRequests(), maxRetries: 2);

        var response = await client.GetAsync("api/v3/lego/sets/75192-1/");

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(3, calls()); // the first try plus two retries
    }

    [Fact]
    public async Task Handler_DoesNotWaitOut_ALongRetryAfter()
    {
        var (client, delays, calls) = CreateClient(_ => TooManyRequests(TimeSpan.FromMinutes(5)));

        var response = await client.GetAsync("api/v3/lego/sets/75192-1/");

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(1, calls());
        Assert.Empty(delays);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Handler_DoesNotRetry_OtherStatuses(HttpStatusCode status)
    {
        var (client, delays, calls) = CreateClient(_ => new HttpResponseMessage(status));

        var response = await client.GetAsync("api/v3/lego/sets/75192-1/");

        Assert.Equal(status, response.StatusCode);
        Assert.Equal(1, calls());
        Assert.Empty(delays);
    }

    [Fact]
    public async Task Handler_KeepsHeadersOnRetries()
    {
        var auth = new List<string?>();
        var delays = new List<TimeSpan>();
        var handler = new RebrickableThrottlingHandler(new RebrickableRateLimiter(TimeSpan.Zero),
            NullLogger<RebrickableThrottlingHandler>.Instance)
        {
            Delay = (d, _) => { delays.Add(d); return Task.CompletedTask; },
            InnerHandler = new StubHttpMessageHandler(req =>
            {
                auth.Add(req.Headers.Authorization?.ToString());
                return auth.Count < 2 ? TooManyRequests() : new HttpResponseMessage(HttpStatusCode.OK);
            })
        };
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://rebrickable.com/") };
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "key abc");

        await client.GetAsync("api/v3/lego/sets/75192-1/");

        Assert.Equal(new string?[] { "key abc", "key abc" }, auth);
    }
}
