using System.IO.Compression;
using System.Text;
using ClosedXML.Excel;
using GlpiNg.Modules.Inventory.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace GlpiNg.Modules.Inventory.Components.Pages.Computers;

/// <summary>
/// Génère les exports (CSV/XLSX/ODS/PDF) du tableau "Ordinateurs" pour le menu Exporter de
/// <see cref="Index"/>. Les libellés dupliquent volontairement ceux d'Index.razor.cs plutôt que
/// de les partager, pour ne pas coupler le writer à l'état du composant (même choix que
/// Detail.razor.cs, qui duplique déjà ces mêmes petits formatteurs).
/// </summary>
internal static class ComputerExportWriter
{
    private static readonly string[] Headers =
    [
        "Nom", "Statut", "Fabricant / Modèle", "Système d'exploitation", "Dernière remontée"
    ];

    static ComputerExportWriter()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public static byte[] BuildCsv(IReadOnlyList<Computer> computers)
    {
        StringBuilder sb = new();
        sb.AppendLine(string.Join(';', Headers.Select(CsvEscape)));

        foreach (Computer computer in computers)
        {
            sb.AppendLine(string.Join(';', Row(computer).Select(CsvEscape)));
        }

        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(sb.ToString());
    }

    private static string CsvEscape(string value)
    {
        return value.Contains(';') || value.Contains('"') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }

    public static byte[] BuildXlsx(IReadOnlyList<Computer> computers)
    {
        using XLWorkbook workbook = new();
        IXLWorksheet sheet = workbook.Worksheets.Add("Ordinateurs");

        for (int col = 0; col < Headers.Length; col++)
        {
            IXLCell cell = sheet.Cell(1, col + 1);
            cell.Value = Headers[col];
            cell.Style.Font.Bold = true;
        }

        for (int rowIndex = 0; rowIndex < computers.Count; rowIndex++)
        {
            string[] row = Row(computers[rowIndex]);
            for (int col = 0; col < row.Length; col++)
            {
                sheet.Cell(rowIndex + 2, col + 1).Value = row[col];
            }
        }

        sheet.Columns().AdjustToContents();

        using MemoryStream stream = new();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public static byte[] BuildOds(IReadOnlyList<Computer> computers)
    {
        using MemoryStream stream = new();

        using (ZipArchive archive = new(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            // Le "mimetype" doit être la toute première entrée de l'archive et rester non compressé :
            // c'est ce qui permet à un lecteur ODS de reconnaître le format sans lire tout le zip.
            ZipArchiveEntry mimetypeEntry = archive.CreateEntry("mimetype", CompressionLevel.NoCompression);
            using (Stream entryStream = mimetypeEntry.Open())
            using (StreamWriter writer = new(entryStream, new UTF8Encoding(false)))
            {
                writer.Write("application/vnd.oasis.opendocument.spreadsheet");
            }

            ZipArchiveEntry manifestEntry = archive.CreateEntry("META-INF/manifest.xml");
            using (Stream entryStream = manifestEntry.Open())
            using (StreamWriter writer = new(entryStream, new UTF8Encoding(false)))
            {
                writer.Write("""
                    <?xml version="1.0" encoding="UTF-8"?>
                    <manifest:manifest xmlns:manifest="urn:oasis:names:tc:opendocument:xmlns:manifest:1.0" manifest:version="1.2">
                      <manifest:file-entry manifest:full-path="/" manifest:version="1.2" manifest:media-type="application/vnd.oasis.opendocument.spreadsheet"/>
                      <manifest:file-entry manifest:full-path="content.xml" manifest:media-type="text/xml"/>
                    </manifest:manifest>
                    """);
            }

            ZipArchiveEntry contentEntry = archive.CreateEntry("content.xml");
            using (Stream entryStream = contentEntry.Open())
            using (StreamWriter writer = new(entryStream, new UTF8Encoding(false)))
            {
                writer.Write(BuildOdsContentXml(computers));
            }
        }

        return stream.ToArray();
    }

    private static string BuildOdsContentXml(IReadOnlyList<Computer> computers)
    {
        StringBuilder sb = new();
        sb.Append("""
            <?xml version="1.0" encoding="UTF-8"?>
            <office:document-content xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0" xmlns:table="urn:oasis:names:tc:opendocument:xmlns:table:1.0" xmlns:text="urn:oasis:names:tc:opendocument:xmlns:text:1.0" office:version="1.2"><office:body><office:spreadsheet><table:table table:name="Ordinateurs">
            """);

        AppendOdsRow(sb, Headers);
        foreach (Computer computer in computers)
        {
            AppendOdsRow(sb, Row(computer));
        }

        sb.Append("</table:table></office:spreadsheet></office:body></office:document-content>");
        return sb.ToString();
    }

    private static void AppendOdsRow(StringBuilder sb, string[] values)
    {
        sb.Append("<table:table-row>");
        foreach (string value in values)
        {
            sb.Append("""<table:table-cell office:value-type="string"><text:p>""");
            sb.Append(XmlEscape(value));
            sb.Append("</text:p></table:table-cell>");
        }
        sb.Append("</table:table-row>");
    }

    private static string XmlEscape(string value) => System.Security.SecurityElement.Escape(value) ?? value;

    public static byte[] BuildPdf(IReadOnlyList<Computer> computers, bool landscape)
    {
        Document document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(landscape ? PageSizes.A4.Landscape() : PageSizes.A4);
                page.Margin(24);
                page.DefaultTextStyle(style => style.FontSize(9));

                page.Header().Text("Ordinateurs").FontSize(16).Bold();

                page.Content().PaddingTop(10).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        foreach (string _ in Headers)
                        {
                            columns.RelativeColumn();
                        }
                    });

                    table.Header(header =>
                    {
                        foreach (string title in Headers)
                        {
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text(title).Bold();
                        }
                    });

                    foreach (Computer computer in computers)
                    {
                        foreach (string value in Row(computer))
                        {
                            table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4).Text(value);
                        }
                    }
                });

                page.Footer().AlignCenter().Text(text =>
                {
                    text.CurrentPageNumber();
                    text.Span(" / ");
                    text.TotalPages();
                });
            });
        });

        return document.GeneratePdf();
    }

    private static string[] Row(Computer computer) =>
    [
        computer.Name,
        StatusLabel(computer.Status),
        ManufacturerAndModel(computer),
        computer.OperatingSystem ?? "—",
        LastInventoryLabel(computer)
    ];

    private static string ManufacturerAndModel(Computer computer)
    {
        bool hasManufacturer = !string.IsNullOrWhiteSpace(computer.Manufacturer);
        bool hasModel = !string.IsNullOrWhiteSpace(computer.Model);

        if (!hasManufacturer && !hasModel) return "—";
        if (hasManufacturer && hasModel) return $"{computer.Manufacturer} {computer.Model}";
        return hasManufacturer ? computer.Manufacturer! : computer.Model!;
    }

    private static string LastInventoryLabel(Computer computer)
    {
        return computer.LastInventoryAt is { } lastInventory
            ? lastInventory.ToLocalTime().ToString("dd/MM/yyyy HH:mm")
            : "Jamais";
    }

    private static string StatusLabel(ComputerStatus status) => status switch
    {
        ComputerStatus.InStock => "En stock",
        ComputerStatus.InProduction => "En production",
        ComputerStatus.Broken => "En panne",
        ComputerStatus.Retired => "Réformé",
        _ => status.ToString()
    };
}
