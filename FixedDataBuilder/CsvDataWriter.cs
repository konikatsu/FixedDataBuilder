using System.Text;

namespace FixedDataBuilder;

public enum DataFileFormat { FixedLength, Csv }

public static class CsvDataWriter
{
    public static void Write(string path, IReadOnlyList<FieldDefinition> fields,
        IReadOnlyList<List<string>> records, CsvTextEncoding textEncoding = CsvTextEncoding.Utf8)
        => File.WriteAllBytes(path, Encode(fields, records, textEncoding));

    public static byte[] Encode(IReadOnlyList<FieldDefinition> fields,
        IReadOnlyList<List<string>> records, CsvTextEncoding textEncoding = CsvTextEncoding.Utf8)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var encoding = JapaneseEncodings.ForCsv(textEncoding);
        var indexes = fields.Select((field, index) => (field, index))
            .Where(item => !item.field.IsRedefines).Select(item => item.index).ToArray();
        if (indexes.Length == 0) throw new InvalidDataException("定義書に項目がありません。");
        if (records.Count == 0) throw new InvalidDataException("出力するデータがありません。");
        var csv = new StringBuilder();
        foreach (var record in records)
        {
            if (record.Count != fields.Count) throw new InvalidDataException("データの項目数が定義と一致しません。");
            for (var column = 0; column < indexes.Length; column++)
            {
                if (column > 0) csv.Append(',');
                csv.Append('"').Append(record[indexes[column]].Replace("\"", "\"\"")).Append('"');
            }
            csv.Append("\r\n");
        }
        // 全データの文字コード変換に成功してから書く。表現できない文字を「?」に変えない。
        return encoding.GetBytes(csv.ToString());
    }
}
