using System.Globalization;
using System.Text;

namespace LegoList.Services;

/// <summary>
/// A tiny PDF writer: landscape Letter pages of text in the four standard Helvetica/Courier fonts.
/// No dependencies and no font files, so it behaves the same in every container.
/// Text is limited to Latin-1; any other character is written as '?'.
/// </summary>
public class SimplePdfWriter
{
    public const double PageWidth = 792;   // 11in landscape
    public const double PageHeight = 612;  // 8.5in

    public enum Font { Helvetica = 1, HelveticaBold = 2, Courier = 3, CourierBold = 4 }

    private readonly List<StringBuilder> pages = [];

    public int PageCount => pages.Count;

    public void AddPage() => pages.Add(new StringBuilder());

    /// <summary>Draws one line of text with its baseline-left corner at (x, y), measured from the bottom-left of the page.</summary>
    public void Text(Font font, double size, double x, double y, string text)
    {
        if (pages.Count == 0) AddPage();
        pages[^1]
            .Append("BT /F").Append((int)font).Append(' ').Append(Num(size)).Append(" Tf 1 0 0 1 ")
            .Append(Num(x)).Append(' ').Append(Num(y)).Append(" Tm (")
            .Append(Escape(text)).Append(") Tj ET\n");
    }

    public byte[] Build()
    {
        if (pages.Count == 0) AddPage();

        // Object numbers: 1 catalog, 2 page tree, 3-6 fonts, then a page and its content stream per page.
        var pageObject = (int i) => 7 + i * 2;
        var contentObject = (int i) => 8 + i * 2;
        var objectCount = 6 + pages.Count * 2;

        var offsets = new long[objectCount + 1];
        using var ms = new MemoryStream();

        void Write(string s) { var b = Encoding.Latin1.GetBytes(s); ms.Write(b, 0, b.Length); }
        void BeginObject(int number) { offsets[number] = ms.Position; Write($"{number} 0 obj\n"); }
        void EndObject() => Write("endobj\n");

        Write("%PDF-1.4\n");

        BeginObject(1);
        Write("<< /Type /Catalog /Pages 2 0 R >>\n");
        EndObject();

        BeginObject(2);
        Write("<< /Type /Pages /Count " + pages.Count + " /Kids [" +
              string.Join(" ", Enumerable.Range(0, pages.Count).Select(i => pageObject(i) + " 0 R")) + "] >>\n");
        EndObject();

        var fonts = new[] { "Helvetica", "Helvetica-Bold", "Courier", "Courier-Bold" };
        for (var i = 0; i < fonts.Length; i++)
        {
            BeginObject(3 + i);
            Write($"<< /Type /Font /Subtype /Type1 /BaseFont /{fonts[i]} /Encoding /WinAnsiEncoding >>\n");
            EndObject();
        }

        for (var i = 0; i < pages.Count; i++)
        {
            BeginObject(pageObject(i));
            Write($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {Num(PageWidth)} {Num(PageHeight)}] " +
                  "/Resources << /Font << /F1 3 0 R /F2 4 0 R /F3 5 0 R /F4 6 0 R >> >> " +
                  $"/Contents {contentObject(i)} 0 R >>\n");
            EndObject();

            var content = Encoding.Latin1.GetBytes(pages[i].ToString());
            BeginObject(contentObject(i));
            Write($"<< /Length {content.Length} >>\nstream\n");
            ms.Write(content, 0, content.Length);
            Write("\nendstream\n");
            EndObject();
        }

        var xrefStart = ms.Position;
        Write($"xref\n0 {objectCount + 1}\n");
        Write("0000000000 65535 f \n");
        for (var n = 1; n <= objectCount; n++)
            Write(offsets[n].ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
        Write($"trailer\n<< /Size {objectCount + 1} /Root 1 0 R >>\nstartxref\n{xrefStart}\n%%EOF\n");

        return ms.ToArray();
    }

    private static string Num(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Escape(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (ch == '\\' || ch == '(' || ch == ')')
                sb.Append('\\').Append(ch);
            else if (ch < 0x20 || ch == 0x7F)
                sb.Append(' ');
            else if (ch > 0xFF || (ch >= 0x80 && ch <= 0x9F))
                sb.Append('?');
            else
                sb.Append(ch);
        }
        return sb.ToString();
    }
}
