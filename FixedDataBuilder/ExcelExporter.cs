using System.IO.Compression;
using System.Xml;
using System.Text;
using System.Globalization;

namespace FixedDataBuilder;

public enum ExcelCellKind
{
    Normal,
    Header,
    Definition,
    Comment
}

public sealed record ExcelCell(string Text, ExcelCellKind Kind = ExcelCellKind.Normal, string? BackgroundColor = null);

public static class ExcelExporter
{
    private const string RelationshipsNamespace = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string OfficeRelationshipsNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string SpreadsheetNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    public static void Write(string path, string sheetName, IReadOnlyList<IReadOnlyList<ExcelCell>> rows)
    {
        var styles = new ExcelStyles(rows);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteContentTypes(archive);
        WriteRootRelationships(archive);
        WriteWorkbook(archive, sheetName);
        WriteWorkbookRelationships(archive);
        WriteStyles(archive, styles);
        WriteWorksheet(archive, rows, styles);
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

    private static void WriteStyles(ZipArchive archive, ExcelStyles styles)
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
        writer.WriteAttributeString("count", (styles.Colors.Count + 2).ToString(CultureInfo.InvariantCulture));
        WriteFill(writer, null);
        // Excelの標準fill 0/1は予約したまま、実際の塗りつぶしにはfill 2を使う。
        WriteFill(writer, null, patternType: "gray125");
        foreach (var color in styles.Colors) WriteFill(writer, color);
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
        writer.WriteAttributeString("count", styles.Entries.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var style in styles.Entries)
        {
            var color = ExcelStyles.FillColor(style);
            var fillId = color is null ? 0 : styles.Colors.IndexOf(color) + 2;
            WriteCellFormat(writer, style.Kind == ExcelCellKind.Header ? 1 : 0, fillId,
                applyFill: color is not null, applyFont: style.Kind == ExcelCellKind.Header);
        }
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

    private static void WriteFill(XmlWriter writer, string? color, string? patternType = null)
    {
        writer.WriteStartElement("fill");
        writer.WriteStartElement("patternFill");
        writer.WriteAttributeString("patternType", patternType ?? (color is null ? "none" : "solid"));
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
        writer.WriteAttributeString("horizontal", "left");
        writer.WriteAttributeString("vertical", "top");
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void WriteWorksheet(ZipArchive archive, IReadOnlyList<IReadOnlyList<ExcelCell>> rows, ExcelStyles styles)
    {
        using var writer = CreateXmlWriter(archive, "xl/worksheets/sheet1.xml");
        writer.WriteStartDocument();
        writer.WriteStartElement("worksheet", SpreadsheetNamespace);
        var widths = ColumnWidths(rows);
        WriteColumns(writer, widths);
        writer.WriteStartElement("sheetData");

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            writer.WriteStartElement("row");
            writer.WriteAttributeString("r", (rowIndex + 1).ToString());

            var row = rows[rowIndex];
            if (row.Any(cell => cell.Kind == ExcelCellKind.Comment))
            {
                var lines = row.Select((cell, column) => cell.Text.Replace("\r\n", "\n").Replace('\r', '\n')
                    .Split('\n').Sum(line => Math.Max(1, (int)Math.Ceiling((double)TextWidth(line) / Math.Max(1, widths[column] - 2)))))
                    .DefaultIfEmpty(1).Max();
                writer.WriteAttributeString("ht", Math.Clamp(lines * 15d + 6, 21, 409.5).ToString(CultureInfo.InvariantCulture));
                writer.WriteAttributeString("customHeight", "1");
            }
            for (var columnIndex = 0; columnIndex < row.Count; columnIndex++)
            {
                WriteCell(writer, rowIndex, columnIndex, row[columnIndex], styles);
            }

            writer.WriteEndElement();
        }

        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndDocument();
    }

    private static List<int> ColumnWidths(IReadOnlyList<IReadOnlyList<ExcelCell>> rows)
    {
        var columnCount = rows.Count == 0 ? 0 : rows.Max(row => row.Count);
        return Enumerable.Range(0, columnCount).Select(column => Math.Clamp(rows
            .Where(row => column < row.Count && row[column].Kind != ExcelCellKind.Comment)
            .Select(row => row[column].Text.Replace('\r', '\n').Split('\n').Max(TextWidth))
            .DefaultIfEmpty(6).Max() + 2, 8, 32)).ToList();
    }

    private static int TextWidth(string value) => value.EnumerateRunes().Sum(rune =>
        rune.Value <= 0xff || rune.Value is >= 0xff61 and <= 0xff9f ? 1 : 2);

    private static void WriteColumns(XmlWriter writer, IReadOnlyList<int> widths)
    {
        if (widths.Count == 0) return;

        writer.WriteStartElement("cols");
        for (var columnIndex = 0; columnIndex < widths.Count; columnIndex++)
        {
            writer.WriteStartElement("col");
            writer.WriteAttributeString("min", (columnIndex + 1).ToString());
            writer.WriteAttributeString("max", (columnIndex + 1).ToString());
            writer.WriteAttributeString("width", widths[columnIndex].ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("customWidth", "1");
            writer.WriteEndElement();
        }
        writer.WriteEndElement();
    }

    private static void WriteCell(XmlWriter writer, int rowIndex, int columnIndex, ExcelCell cell, ExcelStyles styles)
    {
        writer.WriteStartElement("c");
        writer.WriteAttributeString("r", ToCellReference(rowIndex, columnIndex));
        writer.WriteAttributeString("t", "inlineStr");
        writer.WriteAttributeString("s", styles.Index(cell).ToString(CultureInfo.InvariantCulture));
        writer.WriteStartElement("is");
        writer.WriteStartElement("t");
        writer.WriteAttributeString("xml", "space", null, "preserve");
        writer.WriteString(cell.Text);
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private sealed record ExcelStyle(ExcelCellKind Kind, string? Background);

    private sealed class ExcelStyles
    {
        public List<ExcelStyle> Entries { get; } = [new(ExcelCellKind.Normal, null), new(ExcelCellKind.Header, null), new(ExcelCellKind.Definition, null)];
        public List<string> Colors { get; } = ["FFDDEBF7"];
        private readonly Dictionary<ExcelStyle, int> indices = new();
        public ExcelStyles(IReadOnlyList<IReadOnlyList<ExcelCell>> rows)
        {
            for (var index = 0; index < Entries.Count; index++) indices.Add(Entries[index], index);
            foreach (var cell in rows.SelectMany(row => row))
            {
                var style = Key(cell);
                if (indices.ContainsKey(style)) continue;
                indices.Add(style, Entries.Count);
                Entries.Add(style);
                var color = FillColor(style);
                if (color is not null && !Colors.Contains(color)) Colors.Add(color);
            }
        }
        public int Index(ExcelCell cell) => indices[Key(cell)];
        private static ExcelStyle Key(ExcelCell cell)
        {
            if (cell.BackgroundColor is { } color && (color.Length != 8 || color.Any(ch => !Uri.IsHexDigit(ch))))
                throw new ArgumentException("Excelの背景色はARGBの8桁16進数で指定してください。");
            return new(cell.Kind, cell.BackgroundColor?.ToUpperInvariant());
        }
        public static string? FillColor(ExcelStyle style) => style.Background ?? (style.Kind switch
        {
            ExcelCellKind.Header or ExcelCellKind.Definition => "FFDDEBF7",
            ExcelCellKind.Comment => "FFE1F2FC",
            _ => null
        });
    }

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
            NewLineHandling = NewLineHandling.Entitize,
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
