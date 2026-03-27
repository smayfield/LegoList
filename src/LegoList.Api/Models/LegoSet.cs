namespace LegoList.Models;

public class LegoSet
{
    public int Id { get; set; }
    public string SetNumber { get; set; } = string.Empty;
    public string Theme { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public int SetListId { get; set; }
    public SetList SetList { get; set; } = null!;
    public SetMetadata? SetMetadata { get; set; }
}
