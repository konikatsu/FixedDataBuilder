using System.Text;

namespace FixedDataBuilder;

public static class FixedLengthDataReader
{
    public static IReadOnlyList<List<string>> Read(string path, IReadOnlyList<FieldDefinition> fields, RecordSeparatorMode separatorMode)
    {
        return Read(path, fields, separatorMode, DataEncodingProfile.ShiftJis);
    }

    public static IReadOnlyList<List<string>> Read(
        string path,
        IReadOnlyList<FieldDefinition> fields,
        RecordSeparatorMode separatorMode,
        DataEncodingProfile encodingProfile)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var encoding = encodingProfile == DataEncodingProfile.Utf8WithNationalText
            ? Encoding.UTF8
            : Encoding.GetEncoding(932);
        var bytes = File.ReadAllBytes(path);
        var recordLength = fields.Sum(field => field.StorageByteLength);
        if (recordLength <= 0)
        {
            throw new InvalidDataException("定義書に項目がありません。");
        }

        var records = SplitRecords(bytes, recordLength, separatorMode);
        return records.Select(record => DecodeRecord(record, fields, encoding, encodingProfile)).ToList();
    }

    private static List<byte[]> SplitRecords(byte[] bytes, int recordLength, RecordSeparatorMode separatorMode)
    {
        return separatorMode switch
        {
            RecordSeparatorMode.None => SplitRecordsWithoutLineBreaks(bytes, recordLength),
            RecordSeparatorMode.CrLfOrLf => SplitRecordsWithLineBreaks(bytes, recordLength),
            _ => throw new InvalidOperationException($"未対応のレコード区切りです: {separatorMode}")
        };
    }

    private static List<byte[]> SplitRecordsWithLineBreaks(byte[] bytes, int recordLength)
    {
        var records = new List<byte[]>();
        var offset = 0;

        while (offset < bytes.Length)
        {
            if (IsLineBreak(bytes[offset]))
            {
                offset = SkipLineBreak(bytes, offset);
                continue;
            }

            if (offset + recordLength > bytes.Length)
            {
                throw new InvalidDataException("固定長データの末尾がレコード長に足りません。");
            }

            var record = new byte[recordLength];
            Array.Copy(bytes, offset, record, 0, recordLength);
            records.Add(record);
            offset += recordLength;

            if (offset < bytes.Length && IsLineBreak(bytes[offset]))
            {
                offset = SkipLineBreak(bytes, offset);
            }
        }

        if (records.Count == 0)
        {
            throw new InvalidDataException("固定長データにレコードがありません。");
        }

        return records;
    }

    private static List<byte[]> SplitRecordsWithoutLineBreaks(byte[] bytes, int recordLength)
    {
        if (bytes.Length % recordLength != 0)
        {
            throw new InvalidDataException("固定長データのサイズがレコード長の倍数ではありません。");
        }

        var records = new List<byte[]>();
        for (var offset = 0; offset < bytes.Length; offset += recordLength)
        {
            var record = new byte[recordLength];
            Array.Copy(bytes, offset, record, 0, recordLength);
            records.Add(record);
        }

        if (records.Count == 0)
        {
            throw new InvalidDataException("固定長データにレコードがありません。");
        }

        return records;
    }

    private static List<string> DecodeRecord(byte[] recordBytes, IReadOnlyList<FieldDefinition> fields, Encoding encoding, DataEncodingProfile encodingProfile)
    {
        var values = new List<string>(fields.Count);
        var offset = 0;
        var offsetsByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var field in fields)
        {
            var fieldOffset = field.IsRedefines
                && field.RedefinesName is not null
                && offsetsByName.TryGetValue(field.RedefinesName, out var redefinedOffset)
                    ? redefinedOffset
                    : offset;
            var fieldBytes = recordBytes.AsSpan(fieldOffset, field.PhysicalByteLength);
            values.Add(field.Type switch
            {
                FieldDataType.PlainNumber => DecodeDisplayNumber(fieldBytes, field, signed: false, encoding),
                FieldDataType.SignedNumber => DecodeDisplayNumber(fieldBytes, field, signed: true, encoding),
                FieldDataType.PackedUnsigned => DecodePackedNumber(fieldBytes, field, signed: false),
                FieldDataType.PackedSigned => DecodePackedNumber(fieldBytes, field, signed: true),
                FieldDataType.Text or FieldDataType.HalfWidthText => encoding.GetString(fieldBytes).TrimEnd(),
                FieldDataType.FullWidthText => DecodeNationalText(fieldBytes, field, encodingProfile),
                _ => throw new InvalidOperationException($"未対応の型です: {field.Type}")
            });
            if (!field.IsRedefines)
            {
                offsetsByName[field.Name] = offset;
                offset += field.StorageByteLength;
            }
        }

        return values;
    }

    private static string DecodeNationalText(ReadOnlySpan<byte> bytes, FieldDefinition field, DataEncodingProfile encodingProfile)
    {
        var encoding = encodingProfile == DataEncodingProfile.Utf8WithNationalText
            ? field.NationalByteWidth == 4 ? Encoding.UTF32 : Encoding.Unicode
            : Encoding.GetEncoding(932);
        return encoding.GetString(bytes).TrimEnd('\0', ' ', '\u3000');
    }

    private static string DecodeDisplayNumber(ReadOnlySpan<byte> bytes, FieldDefinition field, bool signed, Encoding encoding)
    {
        var text = encoding.GetString(bytes).Trim();
        var negative = signed && text.StartsWith('-');
        if (negative || text.StartsWith('+'))
        {
            text = text[1..];
        }

        if (signed && text.Length > 0 && TryDecodeAsciiZonedSign(text[^1], out var lastDigit, out var zonedNegative))
        {
            text = text[..^1] + lastDigit;
            negative = zonedNegative;
        }

        if (!text.All(char.IsDigit))
        {
            throw new InvalidDataException($"{field.Name}: 数値以外の文字があります。");
        }

        return FormatDigitsForDisplay(text.PadLeft(field.Length, '0'), field, signed, negative);
    }

    private static string DecodePackedNumber(ReadOnlySpan<byte> bytes, FieldDefinition field, bool signed)
    {
        var digits = PackedDecimal.DecodeDigits(bytes, field.Length, signed, out var negative);
        return FormatDigitsForDisplay(digits, field, signed, negative);
    }

    private static bool TryDecodeAsciiZonedSign(char value, out char digit, out bool negative)
    {
        const string positiveSigns = "{ABCDEFGHI";
        const string negativeSigns = "}JKLMNOPQR";

        var positiveIndex = positiveSigns.IndexOf(value);
        if (positiveIndex >= 0)
        {
            digit = (char)('0' + positiveIndex);
            negative = false;
            return true;
        }

        var negativeIndex = negativeSigns.IndexOf(value);
        if (negativeIndex >= 0)
        {
            digit = (char)('0' + negativeIndex);
            negative = true;
            return true;
        }

        digit = '\0';
        negative = false;
        return false;
    }

    private static string FormatDigitsForDisplay(string digits, FieldDefinition field, bool signed, bool negative)
    {
        var sign = signed ? negative ? "-" : "+" : string.Empty;
        if (field.DecimalScale == 0)
        {
            return sign + digits;
        }

        var integerPart = digits[..field.IntegerDigitLength];
        var decimalPart = digits[field.IntegerDigitLength..];
        return $"{sign}{integerPart}.{decimalPart}";
    }

    private static bool IsLineBreak(byte value) => value is 0x0D or 0x0A;

    private static int SkipLineBreak(byte[] bytes, int offset)
    {
        if (bytes[offset] == 0x0D && offset + 1 < bytes.Length && bytes[offset + 1] == 0x0A)
        {
            return offset + 2;
        }

        return offset + 1;
    }
}

public enum RecordSeparatorMode
{
    CrLfOrLf,
    None
}
