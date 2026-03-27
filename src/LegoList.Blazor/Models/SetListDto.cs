namespace LegoList.Blazor.Models;

public class SetListDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SetCount { get; set; }
    public List<LegoSetDto> Sets { get; set; } = [];
}
