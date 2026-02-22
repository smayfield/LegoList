using System.Net.Http.Headers;

namespace LegoList.Blazor.Services;

public class ApiAuthHandler(TokenProvider tokenProvider) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(tokenProvider.IdToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenProvider.IdToken);
        return base.SendAsync(request, ct);
    }
}
