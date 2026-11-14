namespace LegoList.Models;

public class LegoSet
{
    public int Id { get; set; }
    public string SetNumber { get; set; } = string.Empty;
    public string Theme { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public bool Built { get; set; }
    public string? DisplayLocation { get; set; }
    public decimal? PurchasePrice { get; set; }
    public string? Notes { get; set; }
    public int SetListId { get; set; }
    public SetList SetList { get; set; } = null!;
    public SetMetadata? SetMetadata { get; set; }

    /// <summary>
    /// Folds a duplicate of this set (same set number, another list) into this one: quantities add up,
    /// this set's own details win, and anything the duplicate recorded that this one lacks is kept.
    /// </summary>
    public void Absorb(LegoSet duplicate)
    {
        Quantity += duplicate.Quantity;
        DisplayLocation ??= duplicate.DisplayLocation;
        PurchasePrice ??= duplicate.PurchasePrice;
        if (!string.IsNullOrWhiteSpace(duplicate.Notes) && duplicate.Notes != Notes)
        {
            Notes = string.IsNullOrWhiteSpace(Notes) ? duplicate.Notes : $"{Notes}\n\n{duplicate.Notes}";
        }
    }
}
