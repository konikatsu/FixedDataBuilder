using System.Text;

namespace FixedDataBuilder;

public static class RecordValidator
{
    public static IReadOnlyList<string> Validate(IReadOnlyList<FieldDefinition> fields, IReadOnlyList<List<string>> records)
    {
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
                        if (!value.All(char.IsDigit))
                        {
                            errors.Add($"{location}: 0以上の数値を入力してください。");
                        }
                        if (value.Length > field.Length)
                        {
                            errors.Add($"{location}: 桁数 {field.Length} を超えています。");
                        }
                        break;

                    case FieldDataType.PackedSigned:
                        var numeric = value.StartsWith('-') ? value[1..] : value;
                        if (numeric.Length == 0 || !numeric.All(char.IsDigit))
                        {
                            errors.Add($"{location}: 符号付き数値を入力してください。");
                        }
                        if (numeric.Length > field.Length)
                        {
                            errors.Add($"{location}: 桁数 {field.Length} を超えています。");
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
}
