namespace LegoList.Models;

public class SetList
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public ICollection<LegoSet> Sets { get; set; } = [];
}
