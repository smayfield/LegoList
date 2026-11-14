using System.Text.Json.Serialization;

namespace LegoList.Services;

public class RebrickableService(
    HttpClient http,
    ILogger<RebrickableService> logger,
    ThemeNameCache? themeCache = null)
{
    private readonly ThemeNameCache themes = themeCache ?? new ThemeNameCache();

    public record RebrickableSetData(
        string? Name,
        string? Theme,
        string? ImageUrl,
        int? PieceCount,
        // True when this is a definitive answer worth storing (found, or Rebrickable says the set
        // doesn't exist). False after a rate limit, outage or other failure, so it gets retried.
        bool Cacheable = false);

    public async Task<RebrickableSetData> FetchAsync(string setNumber)
    {
        // Rebrickable uses "75192-1" format; append -1 if no variant suffix present
        var rebrickableId = System.Text.RegularExpressions.Regex.IsMatch(setNumber, @"-\d+$")
            ? setNumber
            : $"{setNumber}-1";

        string? name = null, theme = null, imageUrl = null;
        int? pieceCount = null;
        var cacheable = false;

        try
        {
            var response = await http.GetAsync($"api/v3/lego/sets/{rebrickableId}/");
            if (response.IsSuccessStatusCode)
            {
                var data = await response.Content.ReadFromJsonAsync<RebrickableSetResponse>();
                if (data is not null)
                {
                    name = data.Name;
                    imageUrl = data.SetImgUrl;
                    pieceCount = data.NumParts;
                    cacheable = true;
                    if (data.ThemeId.HasValue)
                    {
                        var (themeName, definitive) = await FetchThemeNameAsync(data.ThemeId.Value);
                        theme = themeName;
                        cacheable = definitive;
                    }
                }
            }
            else
            {
                // 404 means Rebrickable doesn't know the set: a definitive answer. Anything else
                // (429, 5xx, ...) is a failure and must not be remembered.
                cacheable = response.StatusCode == System.Net.HttpStatusCode.NotFound;
                logger.LogInformation("Rebrickable returned {Status} for set {SetNumber}", response.StatusCode, setNumber);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch Rebrickable data for set {SetNumber}", setNumber);
        }

        return new RebrickableSetData(name, theme, imageUrl, pieceCount, cacheable);
    }

    // Definitive is false when the lookup failed (not "theme not found"), so the caller can retry later.
    private async Task<(string? Name, bool Definitive)> FetchThemeNameAsync(int themeId)
    {
        if (themes.TryGet(themeId, out var cached))
            return (cached, true);

        try
        {
            var response = await http.GetAsync($"api/v3/lego/themes/{themeId}/");
            if (!response.IsSuccessStatusCode)
                return (null, response.StatusCode == System.Net.HttpStatusCode.NotFound);
            var data = await response.Content.ReadFromJsonAsync<RebrickableThemeResponse>();
            if (data?.Name is { } name)
                themes.Set(themeId, name);
            return (data?.Name, true);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch Rebrickable theme {ThemeId}", themeId);
            return (null, false);
        }
    }

    private sealed record RebrickableSetResponse(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("theme_id")] int? ThemeId,
        [property: JsonPropertyName("set_img_url")] string? SetImgUrl,
        [property: JsonPropertyName("num_parts")] int? NumParts);

    private sealed record RebrickableThemeResponse(
        [property: JsonPropertyName("name")] string? Name);
}
