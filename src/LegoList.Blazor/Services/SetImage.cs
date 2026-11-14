using LegoList.Blazor.Models;

namespace LegoList.Blazor.Services;

/// <summary>Helpers for showing a set's picture safely.</summary>
public static class SetImage
{
    /// <summary>Accepts only absolute http(s) URLs, so a bad value can't become a script or local link.</summary>
    public static bool TryGetSafeHttpUrl(string? value, out string safeUrl)
    {
        safeUrl = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return false;

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return false;

        safeUrl = uri.ToString();
        return true;
    }

    public static string GetAlt(LegoSetDto set)
    {
        if (!string.IsNullOrWhiteSpace(set.Name))
            return set.Name;

        if (!string.IsNullOrWhiteSpace(set.SetNumber))
            return $"Image of LEGO set {set.SetNumber}";

        return "LEGO set image";
    }
}
