using System.Text.Json.Serialization;

namespace LegoList.Services;

public class RebrickableService(HttpClient http, ILogger<RebrickableService> logger)
{
    public async Task<(string? ImageUrl, int? PieceCount)> FetchAsync(string setNumber)
    {
        // Rebrickable uses "75192-1" format; append -1 if no variant suffix present
        var rebrickableId = System.Text.RegularExpressions.Regex.IsMatch(setNumber, @"-\d+$")
            ? setNumber
            : $"{setNumber}-1";

        try
        {
            var response = await http.GetAsync($"api/v3/lego/sets/{rebrickableId}/");
            if (!response.IsSuccessStatusCode)
            {
                logger.LogInformation("Rebrickable returned {Status} for set {SetNumber}", response.StatusCode, setNumber);
                return (null, null);
            }

            var data = await response.Content.ReadFromJsonAsync<RebrickableResponse>();
            return (data?.SetImgUrl, data?.NumParts);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch Rebrickable data for set {SetNumber}", setNumber);
            return (null, null);
        }
    }

    private sealed record RebrickableResponse(
        [property: JsonPropertyName("set_img_url")] string? SetImgUrl,
        [property: JsonPropertyName("num_parts")] int? NumParts);
}
