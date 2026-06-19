using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace FixedDataBuilder;

public static partial class DefinitionCsvReader
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
            if (IsHeader(lineNumber, cells))
            {
                continue;
            }

            fields.Add(ParseField(cells, lineNumber));
        }

        if (fields.Count == 0)
        {
            throw new InvalidDataException("定義書に項目がありません。");
        }

        return fields;
    }

    private static FieldDefinition ParseField(IReadOnlyList<string> cells, int lineNumber)
    {
        if (cells.Count < 2)
        {
            throw new InvalidDataException($"{lineNumber} 行目: 項目名と定義が必要です。");
        }

        var name = cells[0].Trim();
        if (string.IsNullOrEmpty(name))
        {
            throw new InvalidDataException($"{lineNumber} 行目: 項目名が空です。");
        }

        if (cells.Count >= 3 && int.TryParse(cells[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var legacyLength))
        {
            if (legacyLength <= 0)
            {
                throw new InvalidDataException($"{lineNumber} 行目: 桁数は正の整数で指定してください。");
            }

            var legacyType = cells[1].Trim();
            return new FieldDefinition(name, ParseLegacyType(legacyType), legacyLength, $"{legacyType} {legacyLength}");
        }

        return ParseCobolPicture(name, cells[1].Trim(), lineNumber);
    }

    private static FieldDefinition ParseCobolPicture(string name, string picture, int lineNumber)
    {
        var normalized = SpaceRegex().Replace(picture.Trim(), " ").ToUpperInvariant();

        var textMatch = TextPictureRegex().Match(normalized);
        if (textMatch.Success)
        {
            return new FieldDefinition(name, FieldDataType.Text, ParseLength(textMatch.Groups[1].Value, lineNumber), normalized);
        }

        var fullWidthTextMatch = FullWidthTextPictureRegex().Match(normalized);
        if (fullWidthTextMatch.Success)
        {
            return new FieldDefinition(name, FieldDataType.FullWidthText, ParseLength(fullWidthTextMatch.Groups[1].Value, lineNumber), normalized);
        }

        var numberMatch = NumberPictureRegex().Match(normalized);
        if (numberMatch.Success)
        {
            var signed = numberMatch.Groups["sign"].Success;
            var integerLength = ParseLength(numberMatch.Groups["integer"].Value, lineNumber);
            var decimalScale = numberMatch.Groups["decimal"].Success
                ? ParseLength(numberMatch.Groups["decimal"].Value, lineNumber)
                : 0;
            var length = integerLength + decimalScale;
            var packed = numberMatch.Groups["packed"].Success;

            if (packed)
            {
                return new FieldDefinition(name, signed ? FieldDataType.PackedSigned : FieldDataType.PackedUnsigned, length, normalized, decimalScale);
            }

            return new FieldDefinition(name, signed ? FieldDataType.SignedNumber : FieldDataType.PlainNumber, length, normalized, decimalScale);
        }

        throw new InvalidDataException($"{lineNumber} 行目: 未対応の COBOL 表記です: {picture}");
    }

    private static int ParseLength(string value, int lineNumber)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var length) || length <= 0)
        {
            throw new InvalidDataException($"{lineNumber} 行目: 桁数は正の整数で指定してください。");
        }

        return length;
    }

    private static bool IsHeader(int lineNumber, IReadOnlyList<string> cells)
    {
        if (lineNumber != 1 || cells.Count == 0)
        {
            return false;
        }

        var first = cells[0].Trim();
        return first is "項目名" or "項目";
    }

    private static FieldDataType ParseLegacyType(string value) => value switch
    {
        "平数字" => FieldDataType.PlainNumber,
        "PAC_符号なし" or "PAC/packed decimal 符号なし" => FieldDataType.PackedUnsigned,
        "PAC_符号あり" or "PAC/packed decimal 符号あり" => FieldDataType.PackedSigned,
        "文字" => FieldDataType.Text,
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

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpaceRegex();

    [GeneratedRegex(@"^X\((\d+)\)$")]
    private static partial Regex TextPictureRegex();

    [GeneratedRegex(@"^N\((\d+)\)$")]
    private static partial Regex FullWidthTextPictureRegex();

    [GeneratedRegex(@"^(?<sign>S)?9\((?<integer>\d+)(V(?<decimal>\d+))?\)(?<packed> COMP-3)?$")]
    private static partial Regex NumberPictureRegex();
}
