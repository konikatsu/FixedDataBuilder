using System.Text;

namespace FixedDataBuilder;

public sealed record DataEncodingDetection(CsvTextEncoding? Encoding, string Description)
{
    public DataEncodingProfile FixedEncoding(DataEncodingProfile fallback) => Encoding switch
    {
        CsvTextEncoding.Utf8 => DataEncodingProfile.Utf8WithNationalText,
        CsvTextEncoding.ShiftJis => DataEncodingProfile.ShiftJis,
        _ => fallback
    };
}

/// <summary>Validity-based candidates, not a guarantee of the producer's encoding.</summary>
public static class DataEncodingDetector
{
    public static DataEncodingDetection Csv(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }))
        {
            // An invalid UTF-8 BOM payload must not fall back to MS932.
            JapaneseEncodings.ForCsv(CsvTextEncoding.Utf8).GetCharCount(bytes.AsSpan(3));
            return new(CsvTextEncoding.Utf8, "UTF-8（BOMあり）");
        }
        if (bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xfe })
            || bytes.AsSpan().StartsWith(new byte[] { 0xfe, 0xff })
            || bytes.AsSpan().StartsWith(new byte[] { 0, 0, 0xfe, 0xff }))
            throw new InvalidDataException("入力CSVのUTF-16/UTF-32には対応していません。UTF-8・SJIS・MS932を使用してください。");
        var candidates = new Candidates();
        candidates.Observe(bytes);
        var result = candidates.Result();
        return result.Encoding is null
            ? new(CsvTextEncoding.Utf8, "ASCIIのみ（UTF-8・SJIS・MS932共通。UTF-8を使用）") : result;
    }

    public static DataEncodingDetection Fixed(string path, IReadOnlyList<FieldDefinition> fields, RecordSeparatorMode separator)
    {
        var length = fields.Sum(field => field.StorageByteLength);
        if (length <= 0) throw new InvalidDataException("定義書に項目がありません。");
        var records = FixedLengthDataReader.SplitRecords(File.ReadAllBytes(path), length, separator);
        var candidates = new Candidates();
        foreach (var record in records)
        {
            var offset = 0;
            foreach (var field in fields)
            {
                // REDEFINES may reinterpret packed bytes as text. Inspect physical text fields only.
                if (field.IsRedefines) continue;
                if (field.Type is FieldDataType.Text or FieldDataType.HalfWidthText)
                    candidates.Observe(record.AsSpan(offset, field.PhysicalByteLength));
                offset += field.StorageByteLength;
            }
        }
        return candidates.Result();
    }

    private sealed class Candidates
    {
        private readonly Encoding utf8 = JapaneseEncodings.ForCsv(CsvTextEncoding.Utf8);
        private readonly Encoding ms932 = JapaneseEncodings.ForCsv(CsvTextEncoding.ShiftJis);
        private bool utf8Valid = true, ms932Valid = true, asciiOnly = true, observed;

        public void Observe(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length == 0) return;
            observed = true;
            foreach (var value in bytes) if (value >= 0x80) asciiOnly = false;
            if (utf8Valid) utf8Valid = Valid(utf8, bytes);
            if (ms932Valid) ms932Valid = Valid(ms932, bytes);
        }

        public DataEncodingDetection Result()
        {
            if (!observed) return new(null, "判別対象のX項目なし（X文字コードは現在の指定を使用）");
            if (asciiOnly) return new(null, "ASCIIのみ（UTF-8・SJIS・MS932共通。現在の指定を使用）");
            if (utf8Valid) return new(CsvTextEncoding.Utf8, ms932Valid
                ? "UTF-8候補（MS932でも読取可能。文字化け時は手動指定）" : "UTF-8候補（BOMなし）");
            if (ms932Valid) return new(CsvTextEncoding.ShiftJis, "MS932候補（SJISとの厳密な区別は手動指定）");
            throw new InvalidDataException("文字コードを判別できません。UTF-8・MS932のどちらとしても不正なバイト列があります。読込条件・定義・元ファイルを確認してください。");
        }

        private static bool Valid(Encoding encoding, ReadOnlySpan<byte> bytes)
        {
            try { encoding.GetCharCount(bytes); return true; }
            catch (DecoderFallbackException) { return false; }
        }
    }
}
