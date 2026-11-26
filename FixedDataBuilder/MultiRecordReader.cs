using System.Globalization;
using System.Text;
using Microsoft.VisualBasic.FileIO;

namespace FixedDataBuilder;

public sealed record MultiRecordDefinition(IReadOnlyList<FieldDefinition> Header, IReadOnlyList<FieldDefinition> Detail)
{
    public MultiRecordDefinition WithNationalEncoding(NationalTextEncoding encoding)
    {
        var width = NationalTextEncodingHelper.FixedByteWidth(encoding);
        FieldDefinition[] Convert(IReadOnlyList<FieldDefinition> fields) => fields.Select(field =>
            field.Type == FieldDataType.FullWidthText ? field with { NationalByteWidth = width } : field).ToArray();
        return new(Convert(Header), Convert(Detail));
    }
    public IReadOnlyList<FieldDefinition> ForKind(char kind) => kind switch
    {
        '1' => Header,
        '2' => Detail,
        _ => throw new InvalidDataException($"未対応のレコード区分: {kind}")
    };

    public static MultiRecordDefinition Read(string path, DefinitionTextEncoding encoding = DefinitionTextEncoding.Auto)
    {
        using var parser = new TextFieldParser(new StringReader(DefinitionFileText.Read(path, encoding)))
        { TextFieldType = FieldType.Delimited, HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false };
        parser.SetDelimiters(",");
        string[] first;
        try { first = parser.ReadFields() ?? []; }
        catch (MalformedLineException ex) { throw new InvalidDataException("定義CSVの先頭行: 引用符が不正です。", ex); }
        if (first.Length < 3 || first[0].Trim() != "レコード区分" || first[1].Trim() != "項目名" || first[2].Trim() != "定義"
            || first.Length > 4 || first.Length == 4 && first[3].Trim() is not ("備考" or "コメント"))
            throw new InvalidDataException("定義CSVの先頭行は「レコード区分,項目名,定義,備考」としてください。備考は省略できます。");
        var header = new List<FieldDefinition>(); var detail = new List<FieldDefinition>();
        while (!parser.EndOfData)
        {
            var line = parser.LineNumber;
            string[] cells;
            try { cells = parser.ReadFields() ?? []; }
            catch (MalformedLineException ex) { throw new InvalidDataException($"定義CSV {parser.ErrorLineNumber}行目: 引用符が不正です。", ex); }
            if (cells.Length is < 3 or > 4) throw new InvalidDataException($"定義CSV {line}行目: 3列または4列にしてください。");
            var fields = cells[0].Trim() switch { "1" => header, "2" => detail,
                _ => throw new InvalidDataException($"定義CSV {line}行目: レコード区分は1か2を指定してください。") };
            var field = DefinitionCsvReader.ParseFieldDefinition(cells[1], cells[2], checked((int)line));
            fields.Add(field with { Comment = cells.Length == 4 ? cells[3] : "" });
        }
        if (header.Count == 0 || detail.Count == 0) throw new InvalidDataException("区分1のヘッダーと区分2の明細の両方を定義してください。");
        return new(header, detail);
    }
}

public sealed record MultiRecord(int FileRow, char Kind, IReadOnlyList<string> Values, string? Warning = null,
    IReadOnlyDictionary<int, string>? FieldWarnings = null);
public sealed record MultiRecordReadResult(IReadOnlyList<MultiRecord> Records, string? IncompleteReason);

public static class MultiRecordReader
{
    public static MultiRecordReadResult Read(string path, MultiRecordDefinition definition, int kindPosition,
        RecordSeparatorMode separator, DataEncodingProfile textEncoding, NationalTextEncoding nationalEncoding)
    {
        if (kindPosition < 1) throw new InvalidDataException("レコード区分の開始位置は1以上にしてください。");
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var encoding = JapaneseEncodings.ForData(textEncoding);
        var headerLength = definition.Header.Sum(field => field.StorageByteLength);
        var detailLength = definition.Detail.Sum(field => field.StorageByteLength);
        if (headerLength < kindPosition || detailLength < kindPosition)
            throw new InvalidDataException("レコード区分の位置が定義のレコード長を超えています。");
        if (new FileInfo(path).Length > 64L * 1024 * 1024)
            throw new InvalidDataException("表示対象は64MB以下のファイルを指定してください。");
        var input = File.ReadAllBytes(path);
        if (input.Length == 0) throw new InvalidDataException("データファイルが空です。");
        var records = new List<MultiRecord>(); var hasHeader = false;
        var offset = 0; var number = 0;
        while (offset < input.Length)
        {
            number++;
            if (input.Length - offset < kindPosition) throw new InvalidDataException($"{number}レコード目: 区分位置に届く前にデータが終わりました。");
            var marker = input[offset + kindPosition - 1];
            var kind = marker switch { (byte)'1' => '1', (byte)'2' => '2', _ => '?' };
            if (kind == '?')
            {
                if (headerLength != detailLength)
                {
                    var unknownWarning = $"{number}レコード目（先頭から{offset + 1L}バイト目）: 不明な区分（16進 {marker:X2}）。レコード長を特定できないため、ここで読込を中止しました。下欄に残りの先頭最大64バイトを16進表示します。";
                    records.Add(new(number, '?', [Convert.ToHexString(input.AsSpan(offset, Math.Min(input.Length - offset, 64)))], unknownWarning));
                    return new(records, unknownWarning);
                }
                if (input.Length - offset < headerLength) throw new InvalidDataException($"{number}レコード目: 不明区分のレコードが{headerLength}バイトに満たず、境界を確認できません。");
                var rawUnknown = input.AsSpan(offset, headerLength).ToArray();
                records.Add(new(number, '?', [Convert.ToHexString(rawUnknown)], $"{number}レコード目（先頭から{offset + 1L}バイト目）: 不明なレコード区分（16進 {marker:X2}）。全内容は16進表示です。"));
                offset += headerLength;
                if (separator == RecordSeparatorMode.CrLfOrLf && offset < input.Length)
                {
                    if (input[offset] == 0x0d && offset + 1 < input.Length && input[offset + 1] == 0x0a) offset += 2;
                    else if (input[offset] == 0x0a) offset++;
                    else return new(records, $"{number}レコード目: 改行がないため、以降の読込を中止しました。");
                }
                continue;
            }
            var fields = definition.ForKind(kind);
            var length = kind == '1' ? headerLength : detailLength;
            if (input.Length - offset < length) throw new InvalidDataException($"{number}レコード目: {length}バイトに満たないデータです。");
            var raw = input.AsSpan(offset, length).ToArray();
            var values = new List<string>(fields.Count);
            var fieldWarnings = new Dictionary<int, string>();
            // Decode fields independently, so invalid test values do not hide other fields or records.
            var position = 0;
            for (var field = 0; field < fields.Count; field++)
            {
                var item = fields[field];
                if (item.IsRedefines) throw new InvalidDataException("マルチレイアウトのREDEFINESには対応していません。");
                var fieldBytes = raw.AsSpan(position, item.PhysicalByteLength).ToArray();
                try
                {
                    // Preserve physical spaces in text fields, including fixed-width padding.
                    var value = item.Type switch
                    {
                        FieldDataType.Text or FieldDataType.HalfWidthText => encoding.GetString(fieldBytes),
                        FieldDataType.FullWidthText => NationalTextEncodingHelper.GetEncoding(nationalEncoding).GetString(fieldBytes),
                        _ => FixedLengthDataReader.DecodeRecord(fieldBytes, [item], encoding, nationalEncoding)[0]
                    };
                    // For ordinary integer fields show the original bytes; blank values must not turn into zeroes.
                    if (item.Type == FieldDataType.PlainNumber && item.DecimalScale == 0) value = encoding.GetString(fieldBytes);
                    values.Add(value);
                }
                catch (Exception ex) when (ex is InvalidDataException or ArgumentException or DecoderFallbackException)
                {
                    var hex = Convert.ToHexString(fieldBytes);
                    fieldWarnings[field] = $"{number}レコード目（区分{kind}）/ {item.Name}: {ex.Message}　実バイト（16進）: {hex}";
                    if (item.Type is FieldDataType.PackedSigned or FieldDataType.PackedUnsigned) values.Add($"[HEX:{hex}]");
                    else
                    {
                        try { values.Add(encoding.GetString(fieldBytes)); }
                        catch (DecoderFallbackException) { values.Add($"[HEX:{hex}]"); }
                    }
                }
                position += item.StorageByteLength;
            }
            if (kind == '1') hasHeader = true;
            var warnings = new List<string>();
            if (kind == '2' && !hasHeader) warnings.Add("ヘッダーより前の明細です。");
            if (fieldWarnings.Count > 0) warnings.Add($"{fieldWarnings.Count}項目に不正な値があります。該当セルを選ぶと内容を確認できます。");
            records.Add(new(number, kind, values, warnings.Count == 0 ? null : string.Join(" ", warnings), fieldWarnings));
            offset += length;
            if (separator == RecordSeparatorMode.CrLfOrLf && offset < input.Length)
            {
                if (input[offset] == 0x0d && offset + 1 < input.Length && input[offset + 1] == 0x0a) offset += 2;
                else if (input[offset] == 0x0a) offset++;
                else throw new InvalidDataException($"{number}レコード目: 定義長 {length} バイトの後に改行がありません。");
            }
        }
        return new(records, null);
    }
}
