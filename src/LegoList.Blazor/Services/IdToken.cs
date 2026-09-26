using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace LegoList.Blazor.Services;

/// <summary>
/// The Google ID token is stored as a claim on the signed-in user (inside the auth
/// cookie) so it's available both during prerendering and inside the interactive
/// circuit, where there is no HttpContext.
/// </summary>
public static class IdToken
{
    public const string ClaimType = "legolist:id_token";

    public static string? From(ClaimsPrincipal? user) =>
        user?.FindFirst(ClaimType)?.Value;

    /// <summary>Reads the token's "exp" claim without validating it (the API validates).</summary>
    public static DateTimeOffset? GetExpiry(string token)
    {
        var parts = token.Split('.');
        if (parts.Length < 2) return null;
        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
            return doc.RootElement.TryGetProperty("exp", out var exp) && exp.TryGetInt64(out var seconds)
                ? DateTimeOffset.FromUnixTimeSeconds(seconds)
                : null;
        }
        catch (FormatException) { return null; }
        catch (JsonException) { return null; }
    }
}
