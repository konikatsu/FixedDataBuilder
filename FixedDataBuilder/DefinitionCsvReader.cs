using System.Globalization;
using System.Text;

namespace FixedDataBuilder;

public static class DefinitionCsvReader
{
    public static IReadOnlyList<FieldDefinition> Read(string path)
    {
        using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var fields = new List<FieldDefinition>();
        var lineNumber = 0;

        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var cells = ParseCsvLine(line);
            if (cells.Count < 3)
            {
                throw new InvalidDataException($"{lineNumber} 行目: 項目名,型,桁数 の3列が必要です。");
            }

            if (lineNumber == 1 && cells[0].Trim() == "項目名")
            {
                continue;
            }

            if (!int.TryParse(cells[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var length) || length <= 0)
            {
                throw new InvalidDataException($"{lineNumber} 行目: 桁数は正の整数で指定してください。");
            }

            fields.Add(new FieldDefinition(cells[0].Trim(), ParseType(cells[1].Trim()), length));
        }

        if (fields.Count == 0)
        {
            throw new InvalidDataException("定義書に項目がありません。");
        }

        return fields;
    }

    private static FieldDataType ParseType(string value) => value switch
    {
        "平数字" => FieldDataType.PlainNumber,
        "PAC_符号なし" or "PAC/packed decimal 符号なし" => FieldDataType.PackedUnsigned,
        "PAC_符号あり" or "PAC/packed decimal 符号あり" => FieldDataType.PackedSigned,
        "文字_半角" => FieldDataType.HalfWidthText,
        "文字_全角" => FieldDataType.FullWidthText,
        _ => throw new InvalidDataException($"未対応の型です: {value}")
    };

    private static List<string> ParseCsvLine(string line)
    {
        var cells = new List<string>();
        var cell = new StringBuilder();
        var inQuote = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                if (inQuote && i + 1 < line.Length && line[i + 1] == '"')
                {
                    cell.Append('"');
                    i++;
                    continue;
                }

                inQuote = !inQuote;
                continue;
            }

            if (ch == ',' && !inQuote)
            {
                cells.Add(cell.ToString());
                cell.Clear();
                continue;
            }

            cell.Append(ch);
        }

        cells.Add(cell.ToString());
        return cells;
    }
}
