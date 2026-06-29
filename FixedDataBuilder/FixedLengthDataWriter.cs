using System.Text;

namespace FixedDataBuilder;

public static class FixedLengthDataWriter
{
    public static void Write(string path, IReadOnlyList<FieldDefinition> fields, IReadOnlyList<List<string>> records, RecordSeparatorMode separatorMode)
    {
        Write(path, fields, records, separatorMode, DataEncodingProfile.ShiftJis, NationalTextEncoding.ShiftJis);
    }

    public static void Write(
        string path,
        IReadOnlyList<FieldDefinition> fields,
        IReadOnlyList<List<string>> records,
        RecordSeparatorMode separatorMode,
        DataEncodingProfile encodingProfile,
        NationalTextEncoding nationalTextEncoding)
    {
        using var stream = File.Create(path);
        foreach (var record in records)
        {
            stream.Write(EncodeRecord(fields, record, encodingProfile, nationalTextEncoding));

            if (separatorMode == RecordSeparatorMode.CrLfOrLf)
            {
                stream.WriteByte(0x0D);
                stream.WriteByte(0x0A);
            }
        }
    }

    public static byte[] EncodeRecord(IReadOnlyList<FieldDefinition> fields, IReadOnlyList<string> record)
    {
        return EncodeRecord(fields, record, DataEncodingProfile.ShiftJis, NationalTextEncoding.ShiftJis);
    }

    public static byte[] EncodeRecord(
        IReadOnlyList<FieldDefinition> fields,
        IReadOnlyList<string> record,
        DataEncodingProfile encodingProfile,
        NationalTextEncoding nationalTextEncoding)
    {
        using var stream = new MemoryStream();

        foreach (var (field, index) in fields.Select((field, index) => (field, index)))
        {
            if (field.IsRedefines)
            {
                continue;
            }

            stream.Write(EncodeField(field, record[index], encodingProfile, nationalTextEncoding));
        }

        return stream.ToArray();
    }

    public static byte[] EncodeField(FieldDefinition field, string value)
    {
        return EncodeField(field, value, DataEncodingProfile.ShiftJis, NationalTextEncoding.ShiftJis);
    }

    public static byte[] EncodeField(
        FieldDefinition field,
        string value,
        DataEncodingProfile encodingProfile,
        NationalTextEncoding nationalTextEncoding)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var encoding = encodingProfile == DataEncodingProfile.Utf8WithNationalText
            ? Encoding.UTF8
            : Encoding.GetEncoding(932);
        value = value.Trim();

        return field.Type switch
        {
            FieldDataType.PlainNumber => EncodeDisplayNumber(value, field, signed: false, encoding),
            FieldDataType.SignedNumber => EncodeDisplayNumber(value, field, signed: true, encoding),
            FieldDataType.PackedUnsigned => EncodePackedNumber(value, field, signed: false),
            FieldDataType.PackedSigned => EncodePackedNumber(value, field, signed: true),
            FieldDataType.Text or FieldDataType.HalfWidthText => EncodeText(value, field.PhysicalByteLength, encoding),
            FieldDataType.FullWidthText => EncodeFullWidthText(value, field, nationalTextEncoding),
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

    private static byte[] EncodeFullWidthText(string value, FieldDefinition field, NationalTextEncoding nationalTextEncoding)
    {
        var length = field.Length;
        var encoding = NationalTextEncodingHelper.GetEncoding(nationalTextEncoding);
        var normalized = string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value;

        if (normalized.Length > length)
        {
            throw new InvalidDataException($"{value} は保存文字数 {length} を超えています。");
        }

        normalized = normalized.PadRight(length, '\u3000');
        var bytes = encoding.GetBytes(normalized);
        var byteLength = field.PhysicalByteLength;
        if (bytes.Length != byteLength)
        {
            throw new InvalidDataException($"{value} は全角文字 {length} 文字の領域に保存できません。");
        }

        return bytes;
    }
}

public enum DataEncodingProfile
{
    ShiftJis,
    Utf8WithNationalText
}

public enum NationalTextEncoding
{
    ShiftJis,
    Utf8,
    Utf16,
    Utf32
}

public static class NationalTextEncodingHelper
{
    public static Encoding GetEncoding(NationalTextEncoding encoding)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return encoding switch
        {
            NationalTextEncoding.ShiftJis => Encoding.GetEncoding(932),
            NationalTextEncoding.Utf8 => Encoding.UTF8,
            NationalTextEncoding.Utf16 => Encoding.Unicode,
            NationalTextEncoding.Utf32 => Encoding.UTF32,
            _ => throw new InvalidOperationException($"未対応の型N文字コードです: {encoding}")
        };
    }

    public static int FixedByteWidth(NationalTextEncoding encoding)
    {
        return encoding switch
        {
            NationalTextEncoding.ShiftJis => 2,
            NationalTextEncoding.Utf8 => 3,
            NationalTextEncoding.Utf16 => 2,
            NationalTextEncoding.Utf32 => 4,
            _ => throw new InvalidOperationException($"未対応の型N文字コードです: {encoding}")
        };
    }

    public static string DisplayName(NationalTextEncoding encoding)
    {
        return encoding switch
        {
            NationalTextEncoding.ShiftJis => "Shift_JIS",
            NationalTextEncoding.Utf8 => "UTF-8",
            NationalTextEncoding.Utf16 => "UTF-16LE",
            NationalTextEncoding.Utf32 => "UTF-32LE",
            _ => encoding.ToString()
        };
    }
}
