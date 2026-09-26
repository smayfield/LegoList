using System.Net.Http.Headers;
using Microsoft.AspNetCore.Components.Authorization;

namespace LegoList.Blazor.Services;

/// <summary>
/// Adds the signed-in user's Google ID token as a bearer token on API calls.
/// Inside an interactive circuit the user comes from the circuit's
/// AuthenticationStateProvider; during prerendering it comes from HttpContext.
/// </summary>
public class ApiAuthHandler(CircuitServicesAccessor circuitServices, IHttpContextAccessor httpContextAccessor)
    : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var token = await GetIdTokenAsync();
        if (!string.IsNullOrEmpty(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await base.SendAsync(request, ct);
    }

    private async Task<string?> GetIdTokenAsync()
    {
        var authStateProvider = circuitServices.Services?.GetService<AuthenticationStateProvider>();
        if (authStateProvider is not null)
        {
            var state = await authStateProvider.GetAuthenticationStateAsync();
            return IdToken.From(state.User);
        }
        return IdToken.From(httpContextAccessor.HttpContext?.User);
    }
}
