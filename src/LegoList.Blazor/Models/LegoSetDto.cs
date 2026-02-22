namespace LegoList.Blazor.Models;

public class LegoSetDto
{
    public int Id { get; set; }
    public string SetNumber { get; set; } = string.Empty;
    public string Theme { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public int SetListId { get; set; }
    public string? ImageUrl { get; set; }
    public int? PieceCount { get; set; }
}
