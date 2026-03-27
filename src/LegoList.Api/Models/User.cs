namespace LegoList.Models;

public class User
{
    public int Id { get; set; }
    public string GoogleSub { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public ICollection<SetList> SetLists { get; set; } = [];
}
