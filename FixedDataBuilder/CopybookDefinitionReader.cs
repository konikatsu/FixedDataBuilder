using System.Text;
using System.Text.RegularExpressions;

namespace FixedDataBuilder;

public static partial class CopybookDefinitionReader
{
    public static IReadOnlyList<FieldDefinition> Read(string path)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var statements = ReadStatements(path);
        var nodes = BuildFieldTree(statements);
        var fields = new List<FieldDefinition>();

        foreach (var node in nodes)
        {
            fields.AddRange(ExpandNode(node, [], hasOccursAncestor: false, isInsideRedefines: false));
        }

        if (fields.Count == 0)
        {
            throw new InvalidDataException("COBOL コピー句から PIC 付きの項目を読み取れませんでした。");
        }

        return fields;
    }

    private static IReadOnlyList<CopybookNode> BuildFieldTree(IReadOnlyList<(string Statement, int LineNumber)> statements)
    {
        var roots = new List<CopybookNode>();
        var stack = new List<CopybookNode>();

        foreach (var (statement, lineNumber) in statements)
        {
            if (!TryParseNode(statement, lineNumber, out var node))
            {
                continue;
            }

            while (stack.Count > 0 && stack[^1].Level >= node.Level)
            {
                stack.RemoveAt(stack.Count - 1);
            }

            if (stack.Count == 0)
            {
                roots.Add(node);
            }
            else
            {
                stack[^1].Children.Add(node);
            }

            stack.Add(node);
        }

        return roots;
    }

    private static IReadOnlyList<(string Statement, int LineNumber)> ReadStatements(string path)
    {
        var lines = File.ReadAllLines(path, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true));
        var statements = new List<(string Statement, int LineNumber)>();
        var builder = new StringBuilder();
        var statementLineNumber = 0;

        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var lineNumber = lineIndex + 1;
            var line = NormalizeSourceLine(lines[lineIndex]);
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (statementLineNumber == 0)
            {
                statementLineNumber = lineNumber;
            }

            while (true)
            {
                var periodIndex = line.IndexOf('.');
                if (periodIndex < 0)
                {
                    builder.Append(' ');
                    builder.Append(line);
                    break;
                }

                builder.Append(' ');
                builder.Append(line[..periodIndex]);
                var statement = SpaceRegex().Replace(builder.ToString(), " ").Trim();
                if (statement.Length > 0)
                {
                    statements.Add((statement, statementLineNumber));
                }

                builder.Clear();
                statementLineNumber = 0;
                line = line[(periodIndex + 1)..].Trim();
                if (line.Length == 0)
                {
                    break;
                }

                statementLineNumber = lineNumber;
            }
        }

        var trailingStatement = SpaceRegex().Replace(builder.ToString(), " ").Trim();
        if (trailingStatement.Length > 0)
        {
            statements.Add((trailingStatement, statementLineNumber == 0 ? lines.Length : statementLineNumber));
        }

        return statements;
    }

    private static string NormalizeSourceLine(string sourceLine)
    {
        if (string.IsNullOrWhiteSpace(sourceLine))
        {
            return string.Empty;
        }

        var line = sourceLine.TrimEnd();
        if (line.Length >= 7)
        {
            var indicator = line[6];
            if (indicator is '*' or '/' or 'D' or 'd')
            {
                return string.Empty;
            }

            line = line.Length > 7 ? line[7..] : string.Empty;
        }

        var inlineCommentIndex = line.IndexOf("*>", StringComparison.Ordinal);
        if (inlineCommentIndex >= 0)
        {
            line = line[..inlineCommentIndex];
        }

        var trimmed = line.Trim();
        return trimmed.StartsWith("*", StringComparison.Ordinal)
            ? string.Empty
            : trimmed;
    }

    private static bool TryParseNode(string statement, int lineNumber, out CopybookNode node)
    {
        node = null!;

        var match = FieldStatementRegex().Match(statement);
        if (!match.Success)
        {
            return false;
        }

        var level = match.Groups["level"].Value;
        if (level is "66" or "88")
        {
            return false;
        }

        if (!int.TryParse(level, out var levelNumber))
        {
            return false;
        }

        var name = match.Groups["name"].Value;
        var rest = match.Groups["rest"].Value;
        var redefinesName = ParseRedefinesName(rest);
        var pictureMatch = PictureRegex().Match(rest);
        var picture = pictureMatch.Success ? pictureMatch.Groups["picture"].Value : null;
        var occursCount = ParseOccursCount(rest, lineNumber);

        node = new CopybookNode(levelNumber, name, rest, lineNumber, picture, occursCount, redefinesName);
        return true;
    }

    private static IReadOnlyList<FieldDefinition> ExpandNode(
        CopybookNode node,
        IReadOnlyList<string> groupPrefixes,
        bool hasOccursAncestor,
        bool isInsideRedefines)
    {
        var nodeIsRedefines = !string.IsNullOrWhiteSpace(node.RedefinesName);
        if ((isInsideRedefines || nodeIsRedefines) && node.OccursCount > 1)
        {
            throw new InvalidDataException($"{node.LineNumber} 行目: REDEFINES 配下または REDEFINES 項目の OCCURS は未対応です。");
        }

        if (hasOccursAncestor && node.OccursCount > 1)
        {
            throw new InvalidDataException($"{node.LineNumber} 行目: 多重 OCCURS は未対応です。");
        }

        if (!string.IsNullOrWhiteSpace(node.Picture))
        {
            return ExpandPictureNode(node, groupPrefixes);
        }

        if (node.OccursCount > 1)
        {
            var fields = new List<FieldDefinition>();
            var groupName = ConvertCobolNameToJapanese(node.Name);
            for (var index = 0; index < node.OccursCount; index++)
            {
                var groupPrefix = $"{groupName}-{index + 1}";
                fields.AddRange(ExpandChildren(node.Children, [.. groupPrefixes, groupPrefix], hasOccursAncestor: true, isInsideRedefines));
            }

            return fields;
        }

        return ExpandChildren(node.Children, groupPrefixes, hasOccursAncestor, isInsideRedefines || nodeIsRedefines);
    }

    private static IReadOnlyList<FieldDefinition> ExpandChildren(
        IReadOnlyList<CopybookNode> children,
        IReadOnlyList<string> groupPrefixes,
        bool hasOccursAncestor,
        bool isInsideRedefines)
    {
        var fields = new List<FieldDefinition>();
        foreach (var child in children)
        {
            fields.AddRange(ExpandNode(child, groupPrefixes, hasOccursAncestor, isInsideRedefines));
        }

        return fields;
    }

    private static IReadOnlyList<FieldDefinition> ExpandPictureNode(CopybookNode node, IReadOnlyList<string> groupPrefixes)
    {
        var definition = NormalizePicture(node.Picture!, node.Rest);
        var parsedFields = new List<FieldDefinition>(node.OccursCount);
        var convertedName = ConvertCobolNameToJapanese(node.Name);
        for (var index = 0; index < node.OccursCount; index++)
        {
            var fieldNamePart = node.OccursCount == 1 ? convertedName : $"{convertedName}-{index + 1}";
            var fieldName = groupPrefixes.Count == 0
                ? fieldNamePart
                : $"{string.Join('.', groupPrefixes)}.{fieldNamePart}";
            var field = DefinitionCsvReader.ParseFieldDefinition(fieldName, definition, node.LineNumber);
            if (!string.IsNullOrWhiteSpace(node.RedefinesName))
            {
                var convertedRedefinesName = ConvertCobolNameToJapanese(node.RedefinesName);
                field = field with
                {
                    DefinitionText = $"{definition} REDEFINES {convertedRedefinesName}",
                    RedefinesName = convertedRedefinesName
                };
            }

            parsedFields.Add(field);
        }

        return parsedFields;
    }

    private static string NormalizePicture(string picture, string rest)
    {
        var normalized = SpaceRegex().Replace(picture.Trim().ToUpperInvariant(), string.Empty);
        var signed = normalized.StartsWith('S');
        if (signed)
        {
            normalized = normalized[1..];
        }

        var type = normalized[0];
        var definition = type switch
        {
            'X' => $"X({ParseRepeatedLength(normalized[1..], 'X', implicitLeadingCount: 1)})",
            'N' => $"N({ParseRepeatedLength(normalized[1..], 'N', implicitLeadingCount: 1)})",
            '9' => NormalizeNumericPicture(normalized),
            _ => throw new InvalidDataException($"未対応の PIC です: {picture}")
        };

        if (signed && definition.StartsWith("9", StringComparison.Ordinal))
        {
            definition = "S" + definition;
        }

        if (ContainsWord(rest, "COMP-3") || ContainsWord(rest, "PACKED-DECIMAL"))
        {
            definition += " COMP-3";
        }

        return definition;
    }

    private static string NormalizeNumericPicture(string normalized)
    {
        var parts = normalized.Split('V', 2);
        var integerLength = ParseRepeatedLength(parts[0][1..], '9', implicitLeadingCount: 1);
        if (parts.Length == 1)
        {
            return $"9({integerLength})";
        }

        var decimalLength = ParseRepeatedLength(parts[1], '9', implicitLeadingCount: 0);
        return $"9({integerLength}V{decimalLength})";
    }

    private static int ParseRepeatedLength(string text, char repeatChar, int implicitLeadingCount)
    {
        if (string.IsNullOrEmpty(text))
        {
            return implicitLeadingCount;
        }

        var lengthMatch = RepeatLengthRegex().Match(text);
        if (lengthMatch.Success)
        {
            return int.Parse(lengthMatch.Groups["length"].Value);
        }

        var repeatedLengthMatch = RepeatedLengthRegex().Match(text);
        if (repeatedLengthMatch.Success && char.ToUpperInvariant(repeatedLengthMatch.Groups["repeat"].Value[0]) == repeatChar)
        {
            return int.Parse(repeatedLengthMatch.Groups["length"].Value);
        }

        if (text.All(value => value == repeatChar))
        {
            return text.Length + implicitLeadingCount;
        }

        throw new InvalidDataException($"未対応の PIC 桁数です: {repeatChar}{text}");
    }

    private static int ParseOccursCount(string rest, int lineNumber)
    {
        var match = OccursRegex().Match(rest);
        if (!match.Success)
        {
            return 1;
        }

        if (!int.TryParse(match.Groups["count"].Value, out var count) || count <= 0)
        {
            throw new InvalidDataException($"{lineNumber} 行目: OCCURS の回数が不正です。");
        }

        return count;
    }

    private static string? ParseRedefinesName(string rest)
    {
        var match = RedefinesRegex().Match(rest);
        return match.Success ? match.Groups["name"].Value : null;
    }

    private static string ConvertCobolNameToJapanese(string name)
    {
        if (name.Any(value => value > 0x7F))
        {
            return name;
        }

        var parts = name
            .Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.ToUpperInvariant())
            .ToList();
        if (parts.Count == 0)
        {
            return name;
        }

        var converted = new StringBuilder();
        foreach (var part in parts)
        {
            converted.Append(NameDictionary.TryGetValue(part, out var value) ? value : part);
        }

        return converted.ToString();
    }

    private static bool ContainsWord(string text, string word)
    {
        return Regex.IsMatch(text, $@"(^|\s){Regex.Escape(word)}(\s|$)", RegexOptions.IgnoreCase);
    }

    private static readonly IReadOnlyDictionary<string, string> NameDictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["ALT"] = "別",
        ["AMOUNT"] = "金額",
        ["AGE"] = "年齢",
        ["ATTACK"] = "攻撃",
        ["BIRTH"] = "生年月日",
        ["CD"] = "コード",
        ["CODE"] = "コード",
        ["COUNT"] = "件数",
        ["CUSTOMER"] = "顧客",
        ["DATA"] = "データ",
        ["DATE"] = "日付",
        ["DETAIL"] = "明細",
        ["DT"] = "日付",
        ["EN"] = "英",
        ["FLG"] = "フラグ",
        ["FLAG"] = "フラグ",
        ["GROUP"] = "グループ",
        ["ID"] = "ID",
        ["ITEM"] = "項目",
        ["KANJI"] = "漢字",
        ["KANA"] = "カナ",
        ["KINGAKU"] = "金額",
        ["MEI"] = "名",
        ["NAME"] = "名",
        ["NM"] = "名",
        ["NO"] = "番号",
        ["NUMBER"] = "番号",
        ["PRODUCT"] = "商品",
        ["QTY"] = "数量",
        ["QUANTITY"] = "数量",
        ["RAW"] = "生データ",
        ["REC"] = "レコード",
        ["RECORD"] = "レコード",
        ["STATUS"] = "状態",
        ["TOTAL"] = "合計",
        ["TYPE"] = "種別",
        ["WEIGHT"] = "体重",
        ["YEAR"] = "年"
    };

    private sealed class CopybookNode(
        int level,
        string name,
        string rest,
        int lineNumber,
        string? picture,
        int occursCount,
        string? redefinesName)
    {
        public int Level { get; } = level;
        public string Name { get; } = name;
        public string Rest { get; } = rest;
        public int LineNumber { get; } = lineNumber;
        public string? Picture { get; } = picture;
        public int OccursCount { get; } = occursCount;
        public string? RedefinesName { get; } = redefinesName;
        public List<CopybookNode> Children { get; } = [];
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpaceRegex();

    [GeneratedRegex(@"^(?<level>\d{2}|77)\s+(?<name>[^\s.]+)(?<rest>.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex FieldStatementRegex();

    [GeneratedRegex(@"\bPIC(?:TURE)?\s+(?<picture>S?[XN9][XN9V()0-9]*)", RegexOptions.IgnoreCase)]
    private static partial Regex PictureRegex();

    [GeneratedRegex(@"^\((?<length>\d+)\)$")]
    private static partial Regex RepeatLengthRegex();

    [GeneratedRegex(@"^(?<repeat>[XN9])\((?<length>\d+)\)$", RegexOptions.IgnoreCase)]
    private static partial Regex RepeatedLengthRegex();

    [GeneratedRegex(@"\bOCCURS\s+(?<count>\d+)\s+(?:TIMES\s+)?", RegexOptions.IgnoreCase)]
    private static partial Regex OccursRegex();

    [GeneratedRegex(@"\bREDEFINES\s+(?<name>[^\s.]+)", RegexOptions.IgnoreCase)]
    private static partial Regex RedefinesRegex();
}
