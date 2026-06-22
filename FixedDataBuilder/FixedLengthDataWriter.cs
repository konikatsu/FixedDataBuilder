using System.Text;

namespace FixedDataBuilder;

public static class FixedLengthDataWriter
{
    public static void Write(string path, IReadOnlyList<FieldDefinition> fields, IReadOnlyList<List<string>> records, RecordSeparatorMode separatorMode)
    {
        using var stream = File.Create(path);
        foreach (var record in records)
        {
            stream.Write(EncodeRecord(fields, record));

            if (separatorMode == RecordSeparatorMode.CrLfOrLf)
            {
                stream.WriteByte(0x0D);
                stream.WriteByte(0x0A);
            }
        }
    }

    public static byte[] EncodeRecord(IReadOnlyList<FieldDefinition> fields, IReadOnlyList<string> record)
    {
        using var stream = new MemoryStream();

        foreach (var (field, index) in fields.Select((field, index) => (field, index)))
        {
            stream.Write(EncodeField(field, record[index]));
        }

        return stream.ToArray();
    }

    public static byte[] EncodeField(FieldDefinition field, string value)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var encoding = Encoding.GetEncoding(932);
        value = value.Trim();

        return field.Type switch
        {
            FieldDataType.PlainNumber => EncodeDisplayNumber(value, field, signed: false, encoding),
            FieldDataType.SignedNumber => EncodeDisplayNumber(value, field, signed: true, encoding),
            FieldDataType.PackedUnsigned => EncodePackedNumber(value, field, signed: false),
            FieldDataType.PackedSigned => EncodePackedNumber(value, field, signed: true),
            FieldDataType.Text or FieldDataType.HalfWidthText => EncodeText(value, field.StorageByteLength, encoding),
            FieldDataType.FullWidthText => EncodeFullWidthText(value, field.Length, encoding),
            _ => throw new InvalidOperationException($"未対応の型です: {field.Type}")
        };
    }

    private static byte[] EncodeDisplayNumber(string value, FieldDefinition field, bool signed, Encoding encoding)
    {
        if (!NumericValueFormatter.TryFormatDigits(value, field, signed, out var digits, out var negative, out var error))
        {
            throw new InvalidDataException($"{field.Name}: {error}");
        }

        if (signed)
        {
            digits = ApplyAsciiZonedSign(digits, negative);
        }

        return encoding.GetBytes(digits);
    }

    private static string ApplyAsciiZonedSign(string digits, bool negative)
    {
        var lastDigit = digits[^1];
        if (lastDigit is < '0' or > '9')
        {
            throw new InvalidDataException("符号付き表示数値の末尾が数字ではありません。");
        }

        const string positiveSigns = "{ABCDEFGHI";
        const string negativeSigns = "}JKLMNOPQR";
        var signedLastDigit = negative
            ? negativeSigns[lastDigit - '0']
            : positiveSigns[lastDigit - '0'];
        return digits[..^1] + signedLastDigit;
    }

    private static byte[] EncodePackedNumber(string value, FieldDefinition field, bool signed)
    {
        if (!NumericValueFormatter.TryFormatDigits(value, field, signed, out var digits, out var negative, out var error))
        {
            throw new InvalidDataException($"{field.Name}: {error}");
        }

        return PackedDecimal.EncodeDigits(digits, signed, negative);
    }

    private static byte[] EncodeText(string value, int byteLength, Encoding encoding)
    {
        var bytes = encoding.GetBytes(value);
        if (bytes.Length > byteLength)
        {
            throw new InvalidDataException($"{value} は保存バイト長 {byteLength} を超えています。");
        }

        var buffer = Enumerable.Repeat((byte)0x20, byteLength).ToArray();
        Array.Copy(bytes, buffer, bytes.Length);
        return buffer;
    }

    private static byte[] EncodeFullWidthText(string value, int length, Encoding encoding)
    {
        var normalized = string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value;

        if (normalized.Length > length)
        {
            throw new InvalidDataException($"{value} は保存文字数 {length} を超えています。");
        }

        normalized = normalized.PadRight(length, '\u3000');
        var bytes = encoding.GetBytes(normalized);
        var byteLength = length * 2;
        if (bytes.Length != byteLength)
        {
            throw new InvalidDataException($"{value} は全角文字 {length} 文字の領域に保存できません。");
        }

        return bytes;
    }
}
