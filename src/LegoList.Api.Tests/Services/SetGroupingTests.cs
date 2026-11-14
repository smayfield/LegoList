using LegoList.Blazor.Services;
using Xunit;

namespace LegoList.Api.Tests.Services;

public class SetGroupingTests
{
    private record S(string Number, string? Theme);

    private static IReadOnlyList<ThemeGroup<S>> Group(IEnumerable<S> sets, params string[] only) =>
        SetGrouping.Group(sets, s => s.Theme, s => s.Number, only.ToHashSet());

    [Fact]
    public void GroupsByTheme_ThemesAlphabetical_NoThemeLast()
    {
        var groups = Group([new("1", null), new("2", "Technic"), new("3", "City"), new("4", "  ")]);

        Assert.Equal(["City", "Technic", SetGrouping.NoTheme], groups.Select(g => g.Theme));
        Assert.Equal(["1", "4"], groups[2].Sets.Select(s => s.Number).Order());
    }

    [Fact]
    public void SortsWithinTheme_ByNumericSetNumber()
    {
        var groups = Group([new("10001", "A"), new("9999", "A"), new("10294-2", "A"), new("10294-1", "A"), new("75192", "A")]);

        Assert.Equal(["9999", "10001", "10294-1", "10294-2", "75192"], groups.Single().Sets.Select(s => s.Number));
    }

    [Fact]
    public void ThemeMatching_IsCaseInsensitive()
    {
        var groups = Group([new("1", "city"), new("2", "City")]);

        Assert.Single(groups);
        Assert.Equal(2, groups[0].Sets.Count);
    }

    [Fact]
    public void Filter_KeepsOnlyChosenThemes_AndEmptyFilterKeepsAll()
    {
        var sets = new[] { new S("1", "City"), new S("2", "Technic"), new S("3", "Space") };

        Assert.Equal(["City", "Space"], Group(sets, "space", "City").Select(g => g.Theme));
        Assert.Equal(3, Group(sets).Count);
    }

    [Fact]
    public void Themes_ListsCounts_InGroupOrder()
    {
        var themes = SetGrouping.Themes([new S("1", "B"), new S("2", "A"), new S("3", "B"), new S("4", null)], s => s.Theme);

        Assert.Equal([("A", 1), ("B", 2), (SetGrouping.NoTheme, 1)], themes);
    }
}
