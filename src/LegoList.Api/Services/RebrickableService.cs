using System.Text.Json.Serialization;

namespace LegoList.Services;

public class RebrickableService(HttpClient http, ILogger<RebrickableService> logger)
{
    public record RebrickableSetData(
        string? Name,
        string? Theme,
        string? ImageUrl,
        int? PieceCount);

    public async Task<RebrickableSetData> FetchAsync(string setNumber)
    {
        // Rebrickable uses "75192-1" format; append -1 if no variant suffix present
        var rebrickableId = System.Text.RegularExpressions.Regex.IsMatch(setNumber, @"-\d+$")
            ? setNumber
            : $"{setNumber}-1";

        string? name = null, theme = null, imageUrl = null;
        int? pieceCount = null;

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
                    if (data.ThemeId.HasValue)
                        theme = await FetchThemeNameAsync(data.ThemeId.Value);
                }
            }
            else
            {
                logger.LogInformation("Rebrickable returned {Status} for set {SetNumber}", response.StatusCode, setNumber);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch Rebrickable data for set {SetNumber}", setNumber);
        }

        return new RebrickableSetData(name, theme, imageUrl, pieceCount);
    }

    private async Task<string?> FetchThemeNameAsync(int themeId)
    {
        try
        {
            var response = await http.GetAsync($"api/v3/lego/themes/{themeId}/");
            if (!response.IsSuccessStatusCode) return null;
            var data = await response.Content.ReadFromJsonAsync<RebrickableThemeResponse>();
            return data?.Name;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch Rebrickable theme {ThemeId}", themeId);
            return null;
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
