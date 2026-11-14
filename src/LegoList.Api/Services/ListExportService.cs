using System.Globalization;
using System.Text;

namespace LegoList.Services;

public record ExportSet(string SetNumber, string Name, string Theme, int Quantity, int? PieceCount)
{
    public long TotalPieces => (PieceCount ?? 0) * (long)Quantity;
}

public record ExportList(string Name, IReadOnlyList<ExportSet> Sets)
{
    public int SetCount => Sets.Sum(s => s.Quantity);
    public long PieceCount => Sets.Sum(s => s.TotalPieces);
}

/// <summary>Turns lists into a CSV or PDF file.</summary>
public static class ListExportService
{
    // ── CSV ──────────────────────────────────────────────────────────────────

    public static byte[] ToCsv(IReadOnlyList<ExportList> lists)
    {
        var sb = new StringBuilder();
        sb.Append("List,Set number,Name,Theme,Quantity,Pieces per set,Total pieces\r\n");
        foreach (var list in lists)
        {
            foreach (var set in list.Sets)
            {
                sb.Append(string.Join(',',
                    CsvText(list.Name),
                    CsvText(set.SetNumber),
                    CsvText(set.Name),
                    CsvText(set.Theme),
                    set.Quantity.ToString(CultureInfo.InvariantCulture),
                    set.PieceCount?.ToString(CultureInfo.InvariantCulture) ?? "",
                    set.PieceCount is null ? "" : set.TotalPieces.ToString(CultureInfo.InvariantCulture)));
                sb.Append("\r\n");
            }
        }

        // A byte-order mark makes Excel read the file as UTF-8.
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        return [.. utf8.GetPreamble(), .. utf8.GetBytes(sb.ToString())];
    }

    private static string CsvText(string value)
    {
        // A cell starting with = + - @ can run as a formula when opened in a spreadsheet.
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
            value = "'" + value;
        return value.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
    }

    // ── PDF ──────────────────────────────────────────────────────────────────

    private const double Margin = 36;
    private const double LineHeight = 11;
    private const double MonoSize = 9;
    private const double MonoWidth = 0.6 * MonoSize; // Courier glyphs are 600/1000 em wide
    private const int NumberWidth = 10, NameWidth = 58, ThemeWidth = 30, QtyWidth = 4, PiecesWidth = 8;

    public static byte[] ToPdf(IReadOnlyList<ExportList> lists, DateTime generatedAtUtc)
    {
        var pdf = new SimplePdfWriter();
        var y = 0.0;
        var pageNumber = 0;

        void NewPage()
        {
            pdf.AddPage();
            pageNumber++;
            y = SimplePdfWriter.PageHeight - Margin - 14;
            pdf.Text(SimplePdfWriter.Font.Helvetica, 8, SimplePdfWriter.PageWidth - Margin - 40, Margin - 18, $"Page {pageNumber}");
            pdf.Text(SimplePdfWriter.Font.Helvetica, 8, Margin, Margin - 18, $"LegoList export, {generatedAtUtc:yyyy-MM-dd}");
        }

        void TableHeader()
        {
            pdf.Text(SimplePdfWriter.Font.CourierBold, MonoSize, Margin, y, Row("Set", "Name", "Theme", "Qty", "Pieces"));
            y -= LineHeight + 3;
        }

        NewPage();
        if (lists.Count == 0)
            pdf.Text(SimplePdfWriter.Font.Helvetica, 12, Margin, y, "No lists to export.");

        for (var i = 0; i < lists.Count; i++)
        {
            var list = lists[i];

            // Start each list with room for its title, header and a few rows; otherwise on a fresh page.
            if (i > 0 && y < Margin + 90) NewPage();
            else if (i > 0) y -= 18;

            pdf.Text(SimplePdfWriter.Font.HelveticaBold, 16, Margin, y, list.Name);
            y -= 16;
            pdf.Text(SimplePdfWriter.Font.Helvetica, 10, Margin, y,
                string.Create(CultureInfo.InvariantCulture,
                    $"{list.SetCount:N0} {(list.SetCount == 1 ? "set" : "sets")}, {list.PieceCount:N0} pieces"));
            y -= 18;
            TableHeader();

            if (list.Sets.Count == 0)
                pdf.Text(SimplePdfWriter.Font.Courier, MonoSize, Margin, y, "(no sets)");

            foreach (var set in list.Sets)
            {
                if (y < Margin)
                {
                    NewPage();
                    pdf.Text(SimplePdfWriter.Font.HelveticaBold, 11, Margin, y, list.Name + " (continued)");
                    y -= 16;
                    TableHeader();
                }

                pdf.Text(SimplePdfWriter.Font.Courier, MonoSize, Margin, y,
                    Row(set.SetNumber, set.Name, set.Theme, set.Quantity.ToString(CultureInfo.InvariantCulture),
                        set.PieceCount is null ? "" : set.TotalPieces.ToString("N0", CultureInfo.InvariantCulture)));
                y -= LineHeight;
            }
        }

        return pdf.Build();
    }

    // One fixed-width table row. Quantity and pieces are right-aligned; long text is cut with "...".
    private static string Row(string number, string name, string theme, string qty, string pieces) =>
        string.Join(' ',
            Fit(number, NumberWidth).PadRight(NumberWidth),
            Fit(name, NameWidth).PadRight(NameWidth),
            Fit(theme, ThemeWidth).PadRight(ThemeWidth),
            Fit(qty, QtyWidth).PadLeft(QtyWidth),
            Fit(pieces, PiecesWidth).PadLeft(PiecesWidth));

    private static string Fit(string text, int width) =>
        text.Length <= width ? text : text[..(width - 3)] + "...";
}
