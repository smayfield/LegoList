using System.Text.RegularExpressions;

namespace LegoList.Blazor.Services;

public record BulkSetEntry(string SetNumber, int Quantity);

public record BulkSetParseResult(IReadOnlyList<BulkSetEntry> Entries, IReadOnlyList<string> Invalid);

/// <summary>
/// Parses text pasted into the "add several sets" box: one set per line (or separated by ';'),
/// each a set number with an optional quantity, e.g. "75192", "75192 x2", "75192, 2", "10294-1 3".
/// Repeated set numbers are combined.
/// </summary>
public static class BulkSetParser
{
    private static readonly Regex Line = new(
        @"^(?<n>\d{3,7}(?:-\d{1,2})?)(?:(?:\s*[,x×*]\s*|\s+)(?<q>\d{1,3}))?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static BulkSetParseResult Parse(string? text)
    {
        var entries = new List<BulkSetEntry>();
        var invalid = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
            return new BulkSetParseResult(entries, invalid);

        foreach (var raw in text.Split(new[] { '\n', '\r', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var match = Line.Match(raw);
            var quantity = 1;
            if (match.Success && match.Groups["q"].Success)
                quantity = int.Parse(match.Groups["q"].Value);

            if (!match.Success || quantity < 1)
            {
                invalid.Add(raw);
                continue;
            }

            var number = match.Groups["n"].Value;
            var existing = entries.FindIndex(e => e.SetNumber == number);
            if (existing >= 0)
                entries[existing] = entries[existing] with { Quantity = entries[existing].Quantity + quantity };
            else
                entries.Add(new BulkSetEntry(number, quantity));
        }

        return new BulkSetParseResult(entries, invalid);
    }
}
