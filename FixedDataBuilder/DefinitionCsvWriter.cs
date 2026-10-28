using System.Text;

namespace FixedDataBuilder;

public sealed record DefinitionCsvRow(string Name, string Definition, string Comment = "");

public static class DefinitionCsvWriter
{
    public static void Write(string path, IReadOnlyList<DefinitionCsvRow> rows)
    {
        File.WriteAllText(path, BuildCsv(rows), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public static string BuildCsv(IReadOnlyList<DefinitionCsvRow> rows)
    {
        var builder = new StringBuilder();
        var hasComments = rows.Any(row => !string.IsNullOrEmpty(row.Comment));
        builder.AppendLine(hasComments ? "項目名,定義,コメント" : "項目名,定義");
        foreach (var row in rows)
        {
            builder.Append(Escape(row.Name));
            builder.Append(',');
            builder.Append(Escape(row.Definition));
            if (hasComments)
            {
                builder.Append(',');
                builder.Append(Escape(row.Comment));
            }
            builder.AppendLine();
        }

        return builder.ToString();
    }

    public static IReadOnlyList<DefinitionCsvRow> NormalizeRows(IEnumerable<DefinitionCsvRow> rows)
    {
        var normalizedRows = new List<DefinitionCsvRow>();
        var rowNumber = 1;
        foreach (var row in rows)
        {
            rowNumber++;
            var name = row.Name.Trim();
            var definition = row.Definition.Trim();
            if (string.IsNullOrEmpty(name) && string.IsNullOrEmpty(definition) && string.IsNullOrEmpty(row.Comment))
            {
                continue;
            }

            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(definition))
            {
                throw new InvalidDataException($"{rowNumber} 行目: 項目名と定義を両方入力してください。");
            }

            normalizedRows.Add(new DefinitionCsvRow(name, definition, row.Comment));
        }

        if (normalizedRows.Count == 0)
        {
            throw new InvalidDataException("定義項目を1件以上入力してください。");
        }

        return normalizedRows;
    }

    private static string Escape(string value)
    {
        return value.Any(ch => ch is ',' or '"' or '\r' or '\n')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }
}
