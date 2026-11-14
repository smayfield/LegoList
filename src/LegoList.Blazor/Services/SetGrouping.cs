using System.Text.RegularExpressions;

namespace LegoList.Blazor.Services;

/// <summary>One theme's sets, already in set-number order.</summary>
public record ThemeGroup<T>(string Theme, IReadOnlyList<T> Sets);

/// <summary>
/// Groups sets by theme (themes A–Z, sets with no theme last) and orders each group by set number,
/// comparing the numeric part as a number so "9999" comes before "10001" and "10294-1" before "10294-2".
/// </summary>
public static partial class SetGrouping
{
    public const string NoTheme = "No theme";

    public static string ThemeOf(string? theme) => string.IsNullOrWhiteSpace(theme) ? NoTheme : theme.Trim();

    /// <summary>Distinct themes (with set counts), in the same order <see cref="Group{T}"/> uses.</summary>
    public static IReadOnlyList<(string Theme, int Count)> Themes<T>(IEnumerable<T> sets, Func<T, string?> theme) =>
        sets.GroupBy(s => ThemeOf(theme(s)), StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key == NoTheme ? 1 : 0)
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => (g.Key, g.Count()))
            .ToList();

    /// <summary>Groups the sets; when <paramref name="onlyThemes"/> is non-empty, keeps just those themes.</summary>
    public static IReadOnlyList<ThemeGroup<T>> Group<T>(
        IEnumerable<T> sets, Func<T, string?> theme, Func<T, string?> setNumber, ISet<string>? onlyThemes = null)
    {
        var filter = onlyThemes is { Count: > 0 }
            ? new HashSet<string>(onlyThemes, StringComparer.OrdinalIgnoreCase)
            : null;
        return sets
            .GroupBy(s => ThemeOf(theme(s)), StringComparer.OrdinalIgnoreCase)
            .Where(g => filter is null || filter.Contains(g.Key))
            .OrderBy(g => g.Key == NoTheme ? 1 : 0)
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new ThemeGroup<T>(g.Key, g.OrderBy(s => setNumber(s), SetNumberComparer.Instance).ToList()))
            .ToList();
    }

    private sealed partial class SetNumberComparer : IComparer<string?>
    {
        public static readonly SetNumberComparer Instance = new();

        public int Compare(string? x, string? y)
        {
            var (xa, xb) = Parse(x);
            var (ya, yb) = Parse(y);
            var c = xa.CompareTo(ya);
            if (c != 0) return c;
            c = xb.CompareTo(yb);
            return c != 0 ? c : string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
        }

        // "10294-2" -> (10294, 2). Anything that isn't numeric sorts after the numeric sets.
        private static (long Number, long Variant) Parse(string? value)
        {
            var m = NumberPattern().Match(value?.Trim() ?? "");
            if (!m.Success) return (long.MaxValue, 0);
            var number = long.TryParse(m.Groups["n"].Value, out var n) ? n : long.MaxValue;
            var variant = m.Groups["v"].Success && long.TryParse(m.Groups["v"].Value, out var v) ? v : 0;
            return (number, variant);
        }

        [GeneratedRegex(@"^(?<n>\d+)(?:-(?<v>\d+))?")]
        private static partial Regex NumberPattern();
    }
}
