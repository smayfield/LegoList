namespace LegoList.Models;

public class SetMetadata
{
    public string SetNumber { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public int? PieceCount { get; set; }
    public DateTime FetchedAt { get; set; }
}
