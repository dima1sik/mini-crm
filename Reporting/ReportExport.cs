using System.Text;
using System.Xml.Linq;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

namespace MiniCRM.Reporting;

public static class ReportExport
{
    static ReportExport() => GlobalFontSettings.FontResolver = new EmbeddedFontResolver();

    public static byte[] Pdf(IReadOnlyList<string> lines)
    {
        using var document = new PdfDocument();
        document.Info.Title = "MiniCRM sales report";
        var font = new XFont("DejaVu Sans", 10);
        using var measureDocument = new PdfDocument();
        var measurePage = measureDocument.AddPage();
        using var measure = XGraphics.FromPdfPage(measurePage);
        var rows = lines.SelectMany(line => Wrap(line, measure, font, 510)).ToArray();
        foreach (var chunk in rows.Chunk(45))
        {
            var page = document.AddPage();
            using var graphics = XGraphics.FromPdfPage(page);
            var y = 45d;
            foreach (var line in chunk)
            {
                graphics.DrawString(line, font, XBrushes.Black, new XPoint(40, y));
                y += 16;
            }
        }
        if (document.PageCount == 0) document.AddPage();
        using var output = new MemoryStream();
        document.Save(output, closeStream: false);
        return output.ToArray();
    }

    public static byte[] Spreadsheet(IReadOnlyList<string> lines)
    {
        XNamespace ns = "urn:schemas-microsoft-com:office:spreadsheet";
        var workbook = new XDocument(new XDeclaration("1.0", "utf-8", null),
            new XProcessingInstruction("mso-application", "progid=\"Excel.Sheet\""),
            new XElement(ns + "Workbook", new XAttribute(XNamespace.Xmlns + "ss", ns),
                new XElement(ns + "Worksheet", new XAttribute(ns + "Name", "Report"),
                    new XElement(ns + "Table", lines.Select(line => new XElement(ns + "Row",
                        new XElement(ns + "Cell", new XElement(ns + "Data", new XAttribute(ns + "Type", "String"), line))))))));
        using var output = new MemoryStream();
        workbook.Save(output);
        return output.ToArray();
    }

    private static IEnumerable<string> Wrap(string line, XGraphics graphics, XFont font, double width)
    {
        // Measure actual glyph widths and preserve whole Unicode characters.
        var current = new StringBuilder();
        foreach (var rune in line.EnumerateRunes())
        {
            if (rune.Value is '\r' or '\n')
            {
                if (current.Length > 0) { yield return current.ToString(); current.Clear(); }
                continue;
            }
            var next = rune.ToString();
            if (current.Length > 0 && graphics.MeasureString(current + next, font).Width > width)
            {
                yield return current.ToString();
                current.Clear();
            }
            current.Append(next);
        }
        yield return current.ToString();
    }

    private sealed class EmbeddedFontResolver : IFontResolver
    {
        public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic) => new("DejaVuSans");
        public byte[] GetFont(string faceName)
        {
            using var source = typeof(ReportExport).Assembly.GetManifestResourceStream("MiniCRM.Reporting.Fonts.DejaVuSans.ttf")
                ?? throw new InvalidOperationException("The bundled report font is missing.");
            using var output = new MemoryStream();
            source.CopyTo(output);
            return output.ToArray();
        }
    }
}
