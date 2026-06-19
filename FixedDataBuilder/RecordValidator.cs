using System.Text;

namespace FixedDataBuilder;

public static class RecordValidator
{
    public static IReadOnlyList<string> Validate(IReadOnlyList<FieldDefinition> fields, IReadOnlyList<List<string>> records)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var shiftJis = Encoding.GetEncoding(932);
        var errors = new List<string>();

        for (var recordIndex = 0; recordIndex < records.Count; recordIndex++)
        {
            var record = records[recordIndex];
            for (var fieldIndex = 0; fieldIndex < fields.Count; fieldIndex++)
            {
                var field = fields[fieldIndex];
                var value = record[fieldIndex].Trim();
                var location = $"Rec {recordIndex + 1} / {field.Name}";

                switch (field.Type)
                {
                    case FieldDataType.PlainNumber:
                    case FieldDataType.PackedUnsigned:
                        ValidateNumber(errors, location, value, field, signed: false);
                        break;

                    case FieldDataType.SignedNumber:
                    case FieldDataType.PackedSigned:
                        ValidateNumber(errors, location, value, field, signed: true);
                        break;

                    case FieldDataType.Text:
                        if (shiftJis.GetByteCount(value) > field.Length)
                        {
                            errors.Add($"{location}: Shift_JIS バイト長 {field.Length} を超えています。");
                        }
                        break;

                    case FieldDataType.HalfWidthText:
                        if (Encoding.ASCII.GetByteCount(value) != value.Length)
                        {
                            errors.Add($"{location}: 半角文字のみ入力してください。");
                        }
                        if (value.Length > field.Length)
                        {
                            errors.Add($"{location}: 桁数 {field.Length} を超えています。");
                        }
                        break;

                    case FieldDataType.FullWidthText:
                        if (value.Length > field.Length)
                        {
                            errors.Add($"{location}: 桁数 {field.Length} を超えています。");
                        }
                        break;
                }
            }
        }

        return errors;
    }

    private static void ValidateNumber(List<string> errors, string location, string value, FieldDefinition field, bool signed)
    {
        if (!NumericValueFormatter.TryFormatDigits(value, field, signed, out _, out _, out var error))
        {
            errors.Add($"{location}: {error}");
        }
    }
}
