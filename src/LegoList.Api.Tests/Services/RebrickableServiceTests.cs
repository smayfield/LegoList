using LegoList.Services;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Text;
using Xunit;

namespace LegoList.Api.Tests.Services;

public class RebrickableServiceTests
{
    private static RebrickableService Create(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var http = new HttpClient(new StubHttpMessageHandler(respond))
        {
            BaseAddress = new Uri("https://rebrickable.com/")
        };
        return new RebrickableService(http, NullLogger<RebrickableService>.Instance);
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task FetchAsync_AppendsVariantSuffix_WhenMissing()
    {
        string? url = null;
        var service = Create(req => { url = req.RequestUri?.ToString(); return Json("{}"); });

        await service.FetchAsync("75192");

        Assert.Contains("75192-1", url);
    }

    [Fact]
    public async Task FetchAsync_PreservesVariantSuffix_WhenPresent()
    {
        string? url = null;
        var service = Create(req => { url = req.RequestUri?.ToString(); return Json("{}"); });

        await service.FetchAsync("75192-1");

        Assert.Contains("75192-1", url);
        Assert.DoesNotContain("75192-1-1", url);
    }

    [Fact]
    public async Task FetchAsync_ReturnsSetData_WhenApiSucceeds()
    {
        var service = Create(_ => Json(
            "{\"name\":\"Millennium Falcon\",\"theme_id\":null,\"set_img_url\":\"https://img.com/f.jpg\",\"num_parts\":7541}"));

        var result = await service.FetchAsync("75192");

        Assert.Equal("Millennium Falcon", result.Name);
        Assert.Equal("https://img.com/f.jpg", result.ImageUrl);
        Assert.Equal(7541, result.PieceCount);
        Assert.Null(result.Theme);
    }

    [Fact]
    public async Task FetchAsync_FetchesThemeName_WhenThemeIdPresent()
    {
        var service = Create(req =>
        {
            if (req.RequestUri!.ToString().Contains("/themes/"))
                return Json("{\"name\":\"Star Wars\"}");
            return Json("{\"name\":\"Falcon\",\"theme_id\":158,\"set_img_url\":null,\"num_parts\":null}");
        });

        var result = await service.FetchAsync("75192");

        Assert.Equal("Star Wars", result.Theme);
    }

    [Fact]
    public async Task FetchAsync_ReturnsNullTheme_WhenThemeFetchFails()
    {
        var service = Create(req =>
        {
            if (req.RequestUri!.ToString().Contains("/themes/"))
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            return Json("{\"name\":\"Technic Set\",\"theme_id\":1,\"set_img_url\":null,\"num_parts\":null}");
        });

        var result = await service.FetchAsync("42083");

        Assert.Equal("Technic Set", result.Name);
        Assert.Null(result.Theme);
    }

    [Fact]
    public async Task FetchAsync_ReturnsAllNulls_WhenApiReturnsError()
    {
        var service = Create(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var result = await service.FetchAsync("99999");

        Assert.Null(result.Name);
        Assert.Null(result.Theme);
        Assert.Null(result.ImageUrl);
        Assert.Null(result.PieceCount);
    }

    [Fact]
    public async Task FetchAsync_ReturnsAllNulls_WhenApiThrows()
    {
        var http = new HttpClient(new ThrowingHandler())
        {
            BaseAddress = new Uri("https://rebrickable.com/")
        };
        var service = new RebrickableService(http, NullLogger<RebrickableService>.Instance);

        var result = await service.FetchAsync("12345");

        Assert.Null(result.Name);
        Assert.Null(result.Theme);
        Assert.Null(result.ImageUrl);
        Assert.Null(result.PieceCount);
    }

    [Fact]
    public async Task FetchAsync_IsCacheable_WhenSetFound()
    {
        var service = Create(_ => Json("{\"name\":\"Falcon\",\"theme_id\":null}"));

        Assert.True((await service.FetchAsync("75192")).Cacheable);
    }

    [Fact]
    public async Task FetchAsync_IsCacheable_WhenSetDoesNotExist()
    {
        var service = Create(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        Assert.True((await service.FetchAsync("99999")).Cacheable);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task FetchAsync_IsNotCacheable_WhenApiFails(HttpStatusCode status)
    {
        var service = Create(_ => new HttpResponseMessage(status));

        Assert.False((await service.FetchAsync("75192")).Cacheable);
    }

    [Fact]
    public async Task FetchAsync_IsNotCacheable_WhenApiThrows()
    {
        var http = new HttpClient(new ThrowingHandler()) { BaseAddress = new Uri("https://rebrickable.com/") };
        var service = new RebrickableService(http, NullLogger<RebrickableService>.Instance);

        Assert.False((await service.FetchAsync("12345")).Cacheable);
    }

    [Fact]
    public async Task FetchAsync_IsNotCacheable_WhenThemeLookupIsRateLimited()
    {
        var service = Create(req => req.RequestUri!.ToString().Contains("/themes/")
            ? new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            : Json("{\"name\":\"Falcon\",\"theme_id\":158}"));

        var result = await service.FetchAsync("75192");

        Assert.Equal("Falcon", result.Name);
        Assert.Null(result.Theme);
        Assert.False(result.Cacheable);
    }

    [Fact]
    public async Task FetchAsync_FetchesEachThemeOnlyOnce()
    {
        var themeCalls = 0;
        var service = Create(req =>
        {
            if (req.RequestUri!.ToString().Contains("/themes/"))
            {
                themeCalls++;
                return Json("{\"name\":\"Star Wars\"}");
            }
            return Json("{\"name\":\"Some set\",\"theme_id\":158}");
        });

        var first = await service.FetchAsync("75192");
        var second = await service.FetchAsync("75257");

        Assert.Equal("Star Wars", first.Theme);
        Assert.Equal("Star Wars", second.Theme);
        Assert.Equal(1, themeCalls);
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => throw new HttpRequestException("Simulated network failure");
    }
}
