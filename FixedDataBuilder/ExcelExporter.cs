using System.IO.Compression;
using System.Xml;

namespace FixedDataBuilder;

public enum ExcelCellKind
{
    Normal,
    Header,
    Definition
}

public sealed record ExcelCell(string Text, ExcelCellKind Kind = ExcelCellKind.Normal);

public static class ExcelExporter
{
    private const string RelationshipsNamespace = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string OfficeRelationshipsNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string SpreadsheetNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    public static void Write(string path, string sheetName, IReadOnlyList<IReadOnlyList<ExcelCell>> rows)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteContentTypes(archive);
        WriteRootRelationships(archive);
        WriteWorkbook(archive, sheetName);
        WriteWorkbookRelationships(archive);
        WriteStyles(archive);
        WriteWorksheet(archive, rows);
    }

    private static void WriteContentTypes(ZipArchive archive)
    {
        using var writer = CreateXmlWriter(archive, "[Content_Types].xml");
        writer.WriteStartDocument();
        writer.WriteStartElement("Types", "http://schemas.openxmlformats.org/package/2006/content-types");
        writer.WriteStartElement("Default");
        writer.WriteAttributeString("Extension", "rels");
        writer.WriteAttributeString("ContentType", "application/vnd.openxmlformats-package.relationships+xml");
        writer.WriteEndElement();
        writer.WriteStartElement("Default");
        writer.WriteAttributeString("Extension", "xml");
        writer.WriteAttributeString("ContentType", "application/xml");
        writer.WriteEndElement();
        WriteOverride(writer, "/xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml");
        WriteOverride(writer, "/xl/worksheets/sheet1.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml");
        WriteOverride(writer, "/xl/styles.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml");
        writer.WriteEndElement();
        writer.WriteEndDocument();
    }

    private static void WriteOverride(XmlWriter writer, string partName, string contentType)
    {
        writer.WriteStartElement("Override");
        writer.WriteAttributeString("PartName", partName);
        writer.WriteAttributeString("ContentType", contentType);
        writer.WriteEndElement();
    }

    private static void WriteRootRelationships(ZipArchive archive)
    {
        using var writer = CreateXmlWriter(archive, "_rels/.rels");
        writer.WriteStartDocument();
        writer.WriteStartElement("Relationships", RelationshipsNamespace);
        writer.WriteStartElement("Relationship");
        writer.WriteAttributeString("Id", "rId1");
        writer.WriteAttributeString("Type", $"{OfficeRelationshipsNamespace}/officeDocument");
        writer.WriteAttributeString("Target", "xl/workbook.xml");
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndDocument();
    }

    private static void WriteWorkbook(ZipArchive archive, string sheetName)
    {
        using var writer = CreateXmlWriter(archive, "xl/workbook.xml");
        writer.WriteStartDocument();
        writer.WriteStartElement("workbook", SpreadsheetNamespace);
        writer.WriteAttributeString("xmlns", "r", null, OfficeRelationshipsNamespace);
        writer.WriteStartElement("sheets");
        writer.WriteStartElement("sheet");
        writer.WriteAttributeString("name", SanitizeSheetName(sheetName));
        writer.WriteAttributeString("sheetId", "1");
        writer.WriteAttributeString("r", "id", OfficeRelationshipsNamespace, "rId1");
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndDocument();
    }

    private static void WriteWorkbookRelationships(ZipArchive archive)
    {
        using var writer = CreateXmlWriter(archive, "xl/_rels/workbook.xml.rels");
        writer.WriteStartDocument();
        writer.WriteStartElement("Relationships", RelationshipsNamespace);
        WriteRelationship(writer, "rId1", $"{OfficeRelationshipsNamespace}/worksheet", "worksheets/sheet1.xml");
        WriteRelationship(writer, "rId2", $"{OfficeRelationshipsNamespace}/styles", "styles.xml");
        writer.WriteEndElement();
        writer.WriteEndDocument();
    }

    private static void WriteRelationship(XmlWriter writer, string id, string type, string target)
    {
        writer.WriteStartElement("Relationship");
        writer.WriteAttributeString("Id", id);
        writer.WriteAttributeString("Type", type);
        writer.WriteAttributeString("Target", target);
        writer.WriteEndElement();
    }

    private static void WriteStyles(ZipArchive archive)
    {
        using var writer = CreateXmlWriter(archive, "xl/styles.xml");
        writer.WriteStartDocument();
        writer.WriteStartElement("styleSheet", SpreadsheetNamespace);

        writer.WriteStartElement("fonts");
        writer.WriteAttributeString("count", "2");
        WriteFont(writer, bold: false);
        WriteFont(writer, bold: true);
        writer.WriteEndElement();

        writer.WriteStartElement("fills");
        writer.WriteAttributeString("count", "3");
        WriteFill(writer, null);
        WriteFill(writer, "FFDCECDC");
        WriteFill(writer, "FFCCE8CC");
        writer.WriteEndElement();

        writer.WriteStartElement("borders");
        writer.WriteAttributeString("count", "1");
        writer.WriteStartElement("border");
        writer.WriteElementString("left", string.Empty);
        writer.WriteElementString("right", string.Empty);
        writer.WriteElementString("top", string.Empty);
        writer.WriteElementString("bottom", string.Empty);
        writer.WriteElementString("diagonal", string.Empty);
        writer.WriteEndElement();
        writer.WriteEndElement();

        writer.WriteStartElement("cellStyleXfs");
        writer.WriteAttributeString("count", "1");
        WriteCellFormat(writer, fontId: 0, fillId: 0, applyFill: false, applyFont: false);
        writer.WriteEndElement();

        writer.WriteStartElement("cellXfs");
        writer.WriteAttributeString("count", "3");
        WriteCellFormat(writer, fontId: 0, fillId: 0, applyFill: false, applyFont: false);
        WriteCellFormat(writer, fontId: 1, fillId: 2, applyFill: true, applyFont: true);
        WriteCellFormat(writer, fontId: 0, fillId: 1, applyFill: true, applyFont: false);
        writer.WriteEndElement();

        writer.WriteEndElement();
        writer.WriteEndDocument();
    }

    private static void WriteFont(XmlWriter writer, bool bold)
    {
        writer.WriteStartElement("font");
        if (bold)
        {
            writer.WriteElementString("b", string.Empty);
        }
        writer.WriteStartElement("name");
        writer.WriteAttributeString("val", "Meiryo UI");
        writer.WriteEndElement();
        writer.WriteStartElement("sz");
        writer.WriteAttributeString("val", "10");
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void WriteFill(XmlWriter writer, string? color)
    {
        writer.WriteStartElement("fill");
        writer.WriteStartElement("patternFill");
        writer.WriteAttributeString("patternType", color is null ? "none" : "solid");
        if (color is not null)
        {
            writer.WriteStartElement("fgColor");
            writer.WriteAttributeString("rgb", color);
            writer.WriteEndElement();
            writer.WriteStartElement("bgColor");
            writer.WriteAttributeString("indexed", "64");
            writer.WriteEndElement();
        }
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void WriteCellFormat(XmlWriter writer, int fontId, int fillId, bool applyFill, bool applyFont)
    {
        writer.WriteStartElement("xf");
        writer.WriteAttributeString("numFmtId", "0");
        writer.WriteAttributeString("fontId", fontId.ToString());
        writer.WriteAttributeString("fillId", fillId.ToString());
        writer.WriteAttributeString("borderId", "0");
        writer.WriteAttributeString("xfId", "0");
        if (applyFill)
        {
            writer.WriteAttributeString("applyFill", "1");
        }
        if (applyFont)
        {
            writer.WriteAttributeString("applyFont", "1");
        }
        writer.WriteStartElement("alignment");
        writer.WriteAttributeString("wrapText", "1");
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void WriteWorksheet(ZipArchive archive, IReadOnlyList<IReadOnlyList<ExcelCell>> rows)
    {
        using var writer = CreateXmlWriter(archive, "xl/worksheets/sheet1.xml");
        writer.WriteStartDocument();
        writer.WriteStartElement("worksheet", SpreadsheetNamespace);
        WriteColumns(writer, rows);
        writer.WriteStartElement("sheetData");

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            writer.WriteStartElement("row");
            writer.WriteAttributeString("r", (rowIndex + 1).ToString());

            var row = rows[rowIndex];
            for (var columnIndex = 0; columnIndex < row.Count; columnIndex++)
            {
                WriteCell(writer, rowIndex, columnIndex, row[columnIndex]);
            }

            writer.WriteEndElement();
        }

        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndDocument();
    }

    private static void WriteColumns(XmlWriter writer, IReadOnlyList<IReadOnlyList<ExcelCell>> rows)
    {
        var columnCount = rows.Count == 0 ? 0 : rows.Max(row => row.Count);
        if (columnCount == 0)
        {
            return;
        }

        writer.WriteStartElement("cols");
        for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
        {
            var width = rows
                .Where(row => columnIndex < row.Count)
                .Select(row => row[columnIndex].Text.Split('\n').Max(part => part.Length))
                .DefaultIfEmpty(8)
                .Max();
            width = Math.Clamp(width + 2, 8, 32);

            writer.WriteStartElement("col");
            writer.WriteAttributeString("min", (columnIndex + 1).ToString());
            writer.WriteAttributeString("max", (columnIndex + 1).ToString());
            writer.WriteAttributeString("width", width.ToString());
            writer.WriteAttributeString("customWidth", "1");
            writer.WriteEndElement();
        }
        writer.WriteEndElement();
    }

    private static void WriteCell(XmlWriter writer, int rowIndex, int columnIndex, ExcelCell cell)
    {
        writer.WriteStartElement("c");
        writer.WriteAttributeString("r", ToCellReference(rowIndex, columnIndex));
        writer.WriteAttributeString("t", "inlineStr");
        writer.WriteAttributeString("s", CellStyleIndex(cell.Kind).ToString());
        writer.WriteStartElement("is");
        writer.WriteStartElement("t");
        writer.WriteAttributeString("xml", "space", null, "preserve");
        writer.WriteString(cell.Text);
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static int CellStyleIndex(ExcelCellKind kind) => kind switch
    {
        ExcelCellKind.Header => 1,
        ExcelCellKind.Definition => 2,
        _ => 0
    };

    private static string ToCellReference(int rowIndex, int columnIndex)
    {
        var dividend = columnIndex + 1;
        var columnName = string.Empty;
        while (dividend > 0)
        {
            var modulo = (dividend - 1) % 26;
            columnName = Convert.ToChar('A' + modulo) + columnName;
            dividend = (dividend - modulo) / 26;
        }

        return $"{columnName}{rowIndex + 1}";
    }

    private static XmlWriter CreateXmlWriter(ZipArchive archive, string entryName)
    {
        var entry = archive.CreateEntry(entryName);
        return XmlWriter.Create(entry.Open(), new XmlWriterSettings
        {
            Encoding = System.Text.Encoding.UTF8,
            Indent = false,
            CloseOutput = true
        });
    }

    private static string SanitizeSheetName(string value)
    {
        var invalid = new[] { ':', '\\', '/', '?', '*', '[', ']' };
        var cleaned = new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "Sheet1" : cleaned[..Math.Min(cleaned.Length, 31)];
    }
}
