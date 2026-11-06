namespace FixedDataBuilder;

public sealed record CsvColumnInference(int Number, string Name, int MaximumByteLength,
    bool IsDigitsOnly, string Example);

public sealed record CsvDefinitionInference(IReadOnlyList<FieldDefinition> Fields,
    IReadOnlyList<List<string>> Records, CsvTextEncoding Encoding, bool HasHeader,
    IReadOnlyList<CsvColumnInference> Columns);

/// <summary>ヘッダー候補は確定ではない。利用者が先頭行を確認してから定義を作る。</summary>
public sealed record CsvDefinitionAnalysis(CsvTextEncoding Encoding, IReadOnlyList<List<string>> Rows,
    bool SuggestedHasHeader, string HeaderReason)
{
    public CsvDefinitionInference CreateDefinition(bool hasHeader)
    {
        if (Rows.Count == 0) throw new InvalidDataException("CSVデータに行がありません。");
        if (hasHeader && Rows.Count == 1)
            throw new InvalidDataException("CSVヘッダーの後にデータ行がありません。");

        var columnCount = Rows[0].Count;
        if (columnCount == 0 || Rows.Any(row => row.Count != columnCount))
            throw new InvalidDataException("CSVの列数がそろっていません。");
        var encoding = JapaneseEncodings.ForCsv(Encoding);
        var records = Rows.Skip(hasHeader ? 1 : 0).Select(row => row.ToList()).ToList();
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fields = new List<FieldDefinition>();
        var columns = new List<CsvColumnInference>();
        for (var column = 0; column < columnCount; column++)
        {
            var baseName = hasHeader ? Rows[0][column].Trim() : string.Empty;
            if (string.IsNullOrEmpty(baseName)) baseName = $"項目{column + 1}";
            var name = baseName;
            var suffix = 2;
            while (!usedNames.Add(name)) name = $"{baseName}_{suffix++}";

            var maximum = 0;
            var digitsOnly = true;
            var anyNonempty = false;
            var example = string.Empty;
            foreach (var record in records)
            {
                var value = record[column];
                maximum = Math.Max(maximum, encoding.GetByteCount(value));
                if (value.Length == 0) continue;
                if (!anyNonempty) example = value;
                anyNonempty = true;
                if (!CsvDefinitionInferer.IsAsciiDigits(value)) digitsOnly = false;
            }

            // 観測したCSVだけから業務上の上限や数字属性は確定できない。値を変えないXで保持する。
            var length = Math.Max(1, maximum);
            var encodingName = JapaneseEncodings.Name(Encoding);
            var comment = $"CSV実測: {maximum}byte/{encodingName}（仕様上限未確定）";
            fields.Add(new FieldDefinition(name, FieldDataType.Text, length, $"X({length})", Comment: comment));
            columns.Add(new CsvColumnInference(column + 1, name, maximum,
                anyNonempty && digitsOnly, example));
        }
        return new CsvDefinitionInference(fields, records, Encoding, hasHeader, columns);
    }
}

public static class CsvDefinitionInferer
{
    public static CsvDefinitionAnalysis Analyze(string path, CsvTextEncoding? encoding = null)
    {
        var actualEncoding = encoding ?? DataEncodingDetector.Csv(path).Encoding ?? CsvTextEncoding.Utf8;
        var rows = CsvDataReader.ReadRows(path, actualEncoding);
        var (suggested, reason) = SuggestHeader(rows);
        return new CsvDefinitionAnalysis(actualEncoding, rows, suggested, reason);
    }

    internal static bool IsAsciiDigits(string value)
        => value.Length > 0 && value.All(ch => ch is >= '0' and <= '9');

    private static (bool Suggested, string Reason) SuggestHeader(IReadOnlyList<List<string>> rows)
    {
        if (rows.Count < 2)
            return (false, "1行だけのため、ヘッダーなしを候補にしました。先頭行を確認してください。");

        var first = rows[0];
        var labels = first.Select(value => value.Trim()).ToList();
        var plausibleLabels = labels.All(value => value.Length is > 0 and <= 80
            && !value.Any(ch => ch is '\r' or '\n') && value.Any(char.IsLetter))
            && labels.Distinct(StringComparer.OrdinalIgnoreCase).Count() == labels.Count;
        if (plausibleLabels)
        {
            // 初行が文字列ラベルで、以降は空を除き数字のみになる列がある場合に限り候補とする。
            // 全行が文字列のCSVを「先頭行がヘッダー」と決め付けてデータを落とさない。
            for (var column = 0; column < first.Count; column++)
            {
                var values = rows.Skip(1).Select(row => row[column]).Where(value => value.Length > 0).ToList();
                if (values.Count > 0 && values.All(IsAsciiDigits))
                    return (true, "先頭行は項目名らしい文字列、後続行には数字のみの列があるため、ヘッダーありの候補です。先頭行を確認してください。");
            }
        }
        return (false, "ヘッダーの有無を確定できないため、ヘッダーなしを候補にしました。先頭行が項目名なら、ヘッダーありに変更してください。");
    }
}
