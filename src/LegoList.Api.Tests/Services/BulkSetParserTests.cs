using LegoList.Blazor.Services;
using Xunit;

namespace LegoList.Api.Tests.Services;

public class BulkSetParserTests
{
    [Fact]
    public void Parse_ReturnsNothing_ForBlankInput()
    {
        var result = BulkSetParser.Parse("  \n \r\n ");

        Assert.Empty(result.Entries);
        Assert.Empty(result.Invalid);
    }

    [Theory]
    [InlineData("75192", "75192", 1)]
    [InlineData("75192 2", "75192", 2)]
    [InlineData("75192 x3", "75192", 3)]
    [InlineData("75192x3", "75192", 3)]
    [InlineData("75192 X3", "75192", 3)]
    [InlineData("75192, 4", "75192", 4)]
    [InlineData("75192 × 5", "75192", 5)]
    [InlineData("10294-1", "10294-1", 1)]
    [InlineData("10294-1 x2", "10294-1", 2)]
    [InlineData("  42083  ", "42083", 1)]
    public void Parse_UnderstandsOneLine(string line, string number, int quantity)
    {
        var result = BulkSetParser.Parse(line);

        var entry = Assert.Single(result.Entries);
        Assert.Equal(number, entry.SetNumber);
        Assert.Equal(quantity, entry.Quantity);
        Assert.Empty(result.Invalid);
    }

    [Fact]
    public void Parse_HandlesManyLines_InOrder()
    {
        var result = BulkSetParser.Parse("75192\r\n10294 x2\n\n42083");

        Assert.Equal(new[] { "75192", "10294", "42083" }, result.Entries.Select(e => e.SetNumber).ToArray());
        Assert.Equal(new[] { 1, 2, 1 }, result.Entries.Select(e => e.Quantity).ToArray());
    }

    [Fact]
    public void Parse_SplitsOnSemicolons()
    {
        var result = BulkSetParser.Parse("75192; 10294 x2");

        Assert.Equal(2, result.Entries.Count);
    }

    [Fact]
    public void Parse_CombinesRepeatedSetNumbers()
    {
        var result = BulkSetParser.Parse("75192\n75192 x2");

        var entry = Assert.Single(result.Entries);
        Assert.Equal(3, entry.Quantity);
    }

    [Theory]
    [InlineData("Millennium Falcon")]
    [InlineData("75192 x0")]
    [InlineData("75192 x")]
    [InlineData("12")]
    [InlineData("75192, 10294, 42083")]
    public void Parse_ReportsLinesItCannotUnderstand(string line)
    {
        var result = BulkSetParser.Parse($"42083\n{line}");

        Assert.Equal("42083", Assert.Single(result.Entries).SetNumber);
        Assert.Equal(line, Assert.Single(result.Invalid));
    }
}
