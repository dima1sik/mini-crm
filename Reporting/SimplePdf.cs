using System.Text;

namespace MiniCRM.Reporting;

public static class SimplePdf
{
    public static string Build(List<string> lines)
    {
        var safe = lines.Take(34).Select(Escape).ToList();
        var body = string.Join("\n", safe.Select(line => $"({line}) Tj 0 -18 Td"));
        var stream = $"BT /F1 11 Tf 50 790 Td {body} ET";
        var objects = new List<string>
        {
            "1 0 obj << /Type /Catalog /Pages 2 0 R >> endobj\n",
            "2 0 obj << /Type /Pages /Kids [3 0 R] /Count 1 >> endobj\n",
            "3 0 obj << /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >> endobj\n",
            "4 0 obj << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> endobj\n",
            $"5 0 obj << /Length {stream.Length} >> stream\n{stream}\nendstream endobj\n"
        };
        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int> { 0 };
        foreach (var obj in objects)
        {
            offsets.Add(pdf.Length);
            pdf.Append(obj);
        }
        var xrefStart = pdf.Length;
        pdf.Append($"xref\n0 {objects.Count + 1}\n");
        pdf.Append("0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1)) pdf.Append($"{offset:0000000000} 00000 n \n");
        pdf.Append($"trailer << /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xrefStart}\n%%EOF");
        return pdf.ToString();
    }

    private static string Escape(string text)
    {
        return new string(text.Where(c => c >= 32 && c <= 126).ToArray()).Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
    }
}
