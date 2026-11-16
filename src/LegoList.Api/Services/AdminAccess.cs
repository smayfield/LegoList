using System.Security.Claims;

namespace LegoList.Services;

/// <summary>
/// Decides who is an administrator: a signed-in user whose Google-verified email is
/// listed under <c>Admin:Emails</c> in configuration.
/// </summary>
public static class AdminAccess
{
    public const string PolicyName = "Admin";

    public static bool IsAdmin(ClaimsPrincipal? user, IEnumerable<string> adminEmails)
    {
        var email = user?.FindFirst(ClaimTypes.Email)?.Value;
        if (string.IsNullOrWhiteSpace(email)) return false;

        // Only trust an address Google says the user has verified.
        var verified = user!.FindFirst("email_verified")?.Value;
        if (!string.Equals(verified, "true", StringComparison.OrdinalIgnoreCase)) return false;

        return adminEmails.Any(a => string.Equals(a?.Trim(), email.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}
