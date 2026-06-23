using System.IO.Compression;
using System.Xml.Linq;

namespace FixedDataBuilder;

public sealed record ExcelImportResult(
    IReadOnlyList<FieldDefinition> Fields,
    IReadOnlyList<List<string>> Records,
    string LayoutName);

public static class ExcelImporter
{
    private static readonly XNamespace SpreadsheetNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    public static ExcelImportResult Read(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var rows = ReadRows(archive);
        if (rows.Count == 0)
        {
            throw new InvalidDataException("Excel に取り込める行がありません。");
        }

        if (Cell(rows, 0, 0) == "項目名" && Cell(rows, 0, 1) == "定義")
        {
            return ReadFieldRows(rows);
        }

        if (Cell(rows, 0, 0) == "レコード")
        {
            return ReadRecordRows(rows);
        }

        throw new InvalidDataException("FixedDataBuilder が出力した Excel 形式ではありません。");
    }

    private static ExcelImportResult ReadFieldRows(IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var fields = new List<FieldDefinition>();
        var fieldRows = new List<IReadOnlyList<string>>();

        for (var rowIndex = 1; rowIndex < rows.Count; rowIndex++)
        {
            var name = Cell(rows, rowIndex, 0).Trim();
            var definition = Cell(rows, rowIndex, 1).Trim();
            if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(definition))
            {
                continue;
            }

            fields.Add(DefinitionCsvReader.ParseFieldDefinition(name, definition, rowIndex + 1));
            fieldRows.Add(rows[rowIndex]);
        }

        if (fields.Count == 0)
        {
            throw new InvalidDataException("Excel に定義項目がありません。");
        }

        var records = new List<List<string>>();
        var columnCount = Math.Max(
            rows.Max(row => row.Count),
            2);

        for (var columnIndex = 2; columnIndex < columnCount; columnIndex++)
        {
            if (string.IsNullOrWhiteSpace(Cell(rows, 0, columnIndex))
                && fieldRows.All(row => string.IsNullOrWhiteSpace(Cell(row, columnIndex))))
            {
                continue;
            }

            var record = new List<string>();
            foreach (var row in fieldRows)
            {
                record.Add(Cell(row, columnIndex));
            }

            records.Add(record);
        }

        if (records.Count == 0)
        {
            throw new InvalidDataException("Excel にレコードがありません。");
        }

        return new ExcelImportResult(fields, records, "項目縦");
    }

    private static ExcelImportResult ReadRecordRows(IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var twoLineHeader = IsTwoLineRecordHeader(rows);
        var dataStartRow = twoLineHeader ? 2 : 1;
        var fields = new List<FieldDefinition>();
        var columnCount = rows.Max(row => row.Count);

        for (var columnIndex = 1; columnIndex < columnCount; columnIndex++)
        {
            var (name, definition) = twoLineHeader
                ? (Cell(rows, 0, columnIndex).Trim(), Cell(rows, 1, columnIndex).Trim())
                : SplitRecordHeader(Cell(rows, 0, columnIndex), columnIndex + 1);

            if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(definition))
            {
                continue;
            }

            fields.Add(DefinitionCsvReader.ParseFieldDefinition(name, definition, 1));
        }

        if (fields.Count == 0)
        {
            throw new InvalidDataException("Excel に定義項目がありません。");
        }

        var records = new List<List<string>>();
        for (var rowIndex = dataStartRow; rowIndex < rows.Count; rowIndex++)
        {
            if (rows[rowIndex].All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            var record = new List<string>();
            for (var fieldIndex = 0; fieldIndex < fields.Count; fieldIndex++)
            {
                record.Add(Cell(rows, rowIndex, fieldIndex + 1));
            }

            records.Add(record);
        }

        if (records.Count == 0)
        {
            throw new InvalidDataException("Excel にレコードがありません。");
        }

        return new ExcelImportResult(fields, records, "レコード縦");
    }

    private static bool IsTwoLineRecordHeader(IReadOnlyList<IReadOnlyList<string>> rows)
    {
        return rows.Count >= 2
            && string.IsNullOrWhiteSpace(Cell(rows, 1, 0))
            && !string.IsNullOrWhiteSpace(Cell(rows, 1, 1));
    }

    private static (string Name, string Definition) SplitRecordHeader(string text, int columnNumber)
    {
        var lines = text
            .Replace("\r\n", "\n")
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (lines.Length < 2)
        {
            throw new InvalidDataException($"1 行目 {columnNumber} 列目: 項目名と定義が2段で必要です。");
        }

        return (lines[0], lines[1]);
    }

    private static IReadOnlyList<IReadOnlyList<string>> ReadRows(ZipArchive archive)
    {
        var sharedStrings = ReadSharedStrings(archive);
        var worksheetEntry = archive.GetEntry("xl/worksheets/sheet1.xml")
            ?? throw new InvalidDataException("Excel の先頭シートを読み込めません。");
        using var stream = worksheetEntry.Open();
        var document = XDocument.Load(stream);
        var rows = new List<List<string>>();

        foreach (var rowElement in document.Descendants(SpreadsheetNamespace + "row"))
        {
            var rowNumber = ParseRowNumber(rowElement);
            while (rows.Count < rowNumber)
            {
                rows.Add([]);
            }

            var row = rows[rowNumber - 1];
            foreach (var cellElement in rowElement.Elements(SpreadsheetNamespace + "c"))
            {
                var columnIndex = ParseColumnIndex(cellElement);
                while (row.Count <= columnIndex)
                {
                    row.Add(string.Empty);
                }

                row[columnIndex] = ReadCellText(cellElement, sharedStrings);
            }
        }

        return rows;
    }

    private static IReadOnlyList<string> ReadSharedStrings(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null)
        {
            return [];
        }

        using var stream = entry.Open();
        var document = XDocument.Load(stream);
        return document
            .Descendants(SpreadsheetNamespace + "si")
            .Select(item => string.Concat(item.Descendants(SpreadsheetNamespace + "t").Select(text => text.Value)))
            .ToList();
    }

    private static string ReadCellText(XElement cellElement, IReadOnlyList<string> sharedStrings)
    {
        var type = cellElement.Attribute("t")?.Value;
        if (type == "inlineStr")
        {
            return string.Concat(cellElement.Descendants(SpreadsheetNamespace + "t").Select(text => text.Value));
        }

        var value = cellElement.Element(SpreadsheetNamespace + "v")?.Value ?? string.Empty;
        if (type == "s" && int.TryParse(value, out var sharedStringIndex) && sharedStringIndex >= 0 && sharedStringIndex < sharedStrings.Count)
        {
            return sharedStrings[sharedStringIndex];
        }

        return value;
    }

    private static int ParseRowNumber(XElement rowElement)
    {
        return int.TryParse(rowElement.Attribute("r")?.Value, out var rowNumber) && rowNumber > 0
            ? rowNumber
            : 1;
    }

    private static int ParseColumnIndex(XElement cellElement)
    {
        var reference = cellElement.Attribute("r")?.Value ?? "A1";
        var columnIndex = 0;
        foreach (var ch in reference)
        {
            if (!char.IsLetter(ch))
            {
                break;
            }

            columnIndex = (columnIndex * 26) + char.ToUpperInvariant(ch) - 'A' + 1;
        }

        return Math.Max(0, columnIndex - 1);
    }

    private static string Cell(IReadOnlyList<IReadOnlyList<string>> rows, int rowIndex, int columnIndex)
    {
        if (rowIndex < 0 || rowIndex >= rows.Count)
        {
            return string.Empty;
        }

        return Cell(rows[rowIndex], columnIndex);
    }

    private static string Cell(IReadOnlyList<string> row, int columnIndex)
    {
        return columnIndex < 0 || columnIndex >= row.Count
            ? string.Empty
            : row[columnIndex];
    }
}
