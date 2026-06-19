using System.Text;

namespace FixedDataBuilder;

public static class FixedLengthDataWriter
{
    public static void Write(string path, IReadOnlyList<FieldDefinition> fields, IReadOnlyList<List<string>> records)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var encoding = Encoding.GetEncoding(932);

        using var stream = File.Create(path);
        foreach (var record in records)
        {
            foreach (var (field, index) in fields.Select((field, index) => (field, index)))
            {
                var value = record[index].Trim();
                var bytes = field.Type switch
                {
                    FieldDataType.PlainNumber => EncodeDisplayNumber(value, field, signed: false, encoding),
                    FieldDataType.SignedNumber => EncodeDisplayNumber(value, field, signed: true, encoding),
                    FieldDataType.PackedUnsigned => EncodePackedNumber(value, field, signed: false),
                    FieldDataType.PackedSigned => EncodePackedNumber(value, field, signed: true),
                    FieldDataType.Text or FieldDataType.HalfWidthText or FieldDataType.FullWidthText => EncodeText(value, field.StorageByteLength, encoding),
                    _ => throw new InvalidOperationException($"未対応の型です: {field.Type}")
                };

                stream.Write(bytes);
            }

            stream.WriteByte(0x0D);
            stream.WriteByte(0x0A);
        }
    }

    private static byte[] EncodeDisplayNumber(string value, FieldDefinition field, bool signed, Encoding encoding)
    {
        if (!NumericValueFormatter.TryFormatDigits(value, field, signed, out var digits, out var negative, out var error))
        {
            throw new InvalidDataException($"{field.Name}: {error}");
        }

        if (negative)
        {
            throw new InvalidDataException($"{field.Name}: 符号付き表示数値の負値保存ルールは未設定です。");
        }

        return encoding.GetBytes(digits);
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
}
