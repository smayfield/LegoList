namespace LegoList.Blazor.Models;

/// <summary>The <c>{ "Error": "..." }</c> body the API sends with a 400.</summary>
public class ApiError
{
    public string? Error { get; set; }
}
