namespace LegoList.Blazor.Models;

public class AdminStatsDto
{
    public int Users { get; set; }
    public int UsersLast7Days { get; set; }
    public int UsersLast30Days { get; set; }
    public int UsersWithSets { get; set; }
    public int Lists { get; set; }
    public int SetEntries { get; set; }
    public int TotalSets { get; set; }
    public int BuiltSets { get; set; }
    public int DistinctSets { get; set; }
    public int CachedMetadata { get; set; }
    public double AverageSetsPerUser { get; set; }
    public List<NamedCountDto> TopThemes { get; set; } = [];
    public List<TopSetDto> TopSets { get; set; } = [];
}

public class NamedCountDto
{
    public string Name { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class TopSetDto
{
    public string SetNumber { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Owners { get; set; }
    public int Copies { get; set; }
}

public class AdminUserRowDto
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public int ListCount { get; set; }
    public int SetEntries { get; set; }
    public int TotalSets { get; set; }
}

public class AdminUserDto
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public List<AdminListDto> Lists { get; set; } = [];
}

public class AdminListDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<AdminSetDto> Sets { get; set; } = [];
}

public class AdminSetDto
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
    public int? PieceCount { get; set; }
}
