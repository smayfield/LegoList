using LegoList.Services;
using System.Globalization;
using System.Text;
using Xunit;

namespace LegoList.Api.Tests.Services;

public class ListExportServiceTests
{
    private static readonly DateTime Stamp = new(2026, 11, 14, 0, 0, 0, DateTimeKind.Utc);

    private static ExportList Sample(string name = "Star Wars", params ExportSet[] sets) =>
        new(name, sets.Length > 0 ? sets : [new ExportSet("75192-1", "Millennium Falcon", "Star Wars", 2, 7541)]);

    private static string Csv(params ExportList[] lists)
    {
        var bytes = ListExportService.ToCsv(lists);
        return new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3); // skip the BOM
    }

    // ── CSV ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Csv_StartsWithByteOrderMark_ThenHeader()
    {
        var bytes = ListExportService.ToCsv([Sample()]);

        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        Assert.StartsWith("List,Set number,Name,Theme,Quantity,Pieces per set,Total pieces\r\n", Csv(Sample()));
    }

    [Fact]
    public void Csv_WritesOneRowPerSet_WithTotals()
    {
        var csv = Csv(Sample());

        Assert.Contains("Star Wars,75192-1,Millennium Falcon,Star Wars,2,7541,15082\r\n", csv);
    }

    [Fact]
    public void Csv_LeavesPieceColumnsEmpty_WhenUnknown()
    {
        var csv = Csv(Sample("Mine", new ExportSet("1234", "Custom", "MOC", 3, null)));

        Assert.Contains("Mine,1234,Custom,MOC,3,,\r\n", csv);
    }

    [Fact]
    public void Csv_QuotesCommasQuotesAndNewlines()
    {
        var csv = Csv(Sample("A, B", new ExportSet("1", "The \"Big\" One", "Line\nBreak", 1, 10)));

        Assert.Contains("\"A, B\",1,\"The \"\"Big\"\" One\",\"Line\nBreak\",1,10,10", csv);
    }

    [Theory]
    [InlineData("=SUM(A1)")]
    [InlineData("+1")]
    [InlineData("-1")]
    [InlineData("@cmd")]
    public void Csv_NeutralisesFormulaCells(string name)
    {
        var csv = Csv(Sample("L", new ExportSet("1", name, "T", 1, 1)));

        Assert.Contains($",'{name},", csv);
    }

    [Fact]
    public void Csv_CombinesSeveralLists()
    {
        var csv = Csv(Sample("One"), Sample("Two"));

        Assert.Equal(3, csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length); // header + a row per list
    }

    // ── PDF ──────────────────────────────────────────────────────────────────

    private static string PdfText(ExportList[] lists) => Encoding.Latin1.GetString(ListExportService.ToPdf(lists, Stamp));

    [Fact]
    public void Pdf_IsAWellFormedDocument()
    {
        var pdf = PdfText([Sample()]);

        Assert.StartsWith("%PDF-1.4", pdf);
        Assert.EndsWith("%%EOF\n", pdf);

        // startxref must point at the xref table, and every xref entry at its object.
        var startxref = long.Parse(pdf[(pdf.LastIndexOf("startxref\n", StringComparison.Ordinal) + 10)..].Split('\n')[0], CultureInfo.InvariantCulture);
        Assert.Equal("xref", pdf.Substring((int)startxref, 4));
        var table = pdf[(int)startxref..].Split('\n');
        var count = int.Parse(table[1].Split(' ')[1], CultureInfo.InvariantCulture);
        for (var n = 1; n < count; n++)
        {
            var offset = int.Parse(table[2 + n].Split(' ')[0], CultureInfo.InvariantCulture);
            Assert.StartsWith($"{n} 0 obj", pdf[offset..]);
        }
    }

    [Fact]
    public void Pdf_ContainsListAndSetText()
    {
        var pdf = PdfText([Sample()]);

        Assert.Contains("(Star Wars)", pdf);
        Assert.Contains("Millennium Falcon", pdf);
        Assert.Contains("75192-1", pdf);
        Assert.Contains("2 sets, 15,082 pieces", pdf);
    }

    [Fact]
    public void Pdf_EscapesParenthesesAndBackslashes_AndReplacesNonLatin1()
    {
        var pdf = PdfText([Sample("A (b) \\ c 日", new ExportSet("1", "Name", "T", 1, 1))]);

        Assert.Contains("(A \\(b\\) \\\\ c ?)", pdf);
    }

    [Fact]
    public void Pdf_StartsANewPage_WhenARunsOutOfRoom()
    {
        var sets = Enumerable.Range(1, 120).Select(i => new ExportSet($"{10000 + i}", $"Set {i}", "Theme", 1, 100)).ToArray();

        var pdf = PdfText([Sample("Big", sets)]);

        var pages = pdf.Split("/Type /Page /").Length - 1;
        Assert.True(pages >= 3, $"expected several pages, got {pages}");
        Assert.Contains("(Big \\(continued\\))", pdf);
        Assert.Contains("Set 120", pdf);
    }

    [Fact]
    public void Pdf_HandlesNoLists()
    {
        var pdf = PdfText([]);

        Assert.Contains("No lists to export.", pdf);
        Assert.StartsWith("%PDF-", pdf);
    }

    [Fact]
    public void Pdf_ShortensTextThatIsTooLongForItsColumn()
    {
        var pdf = PdfText([Sample("L", new ExportSet("1", new string('x', 200), "T", 1, 1))]);

        Assert.DoesNotContain(new string('x', 100), pdf);
        Assert.Contains("xxx...", pdf);
    }
}
