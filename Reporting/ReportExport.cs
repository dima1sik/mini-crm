using System.Text;
using System.Xml.Linq;

namespace MiniCRM.Reporting;

public static class ReportExport
{
    public static byte[] Pdf(IReadOnlyList<string> lines) => Encoding.ASCII.GetBytes(SimplePdf.Build(lines.ToList()));

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

}
