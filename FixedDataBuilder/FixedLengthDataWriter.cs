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
                    FieldDataType.PlainNumber => EncodePlainNumber(value, field.Length, encoding),
                    FieldDataType.PackedUnsigned => PackedDecimal.Encode(value, field.Length, signed: false),
                    FieldDataType.PackedSigned => PackedDecimal.Encode(value, field.Length, signed: true),
                    FieldDataType.HalfWidthText or FieldDataType.FullWidthText => EncodeText(value, field.StorageByteLength, encoding),
                    _ => throw new InvalidOperationException($"未対応の型です: {field.Type}")
                };

                stream.Write(bytes);
            }

            stream.WriteByte(0x0D);
            stream.WriteByte(0x0A);
        }
    }

    private static byte[] EncodePlainNumber(string value, int length, Encoding encoding)
    {
        var padded = value.PadLeft(length, '0');
        return encoding.GetBytes(padded);
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
