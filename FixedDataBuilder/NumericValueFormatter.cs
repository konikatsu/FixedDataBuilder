namespace FixedDataBuilder;

public static class NumericValueFormatter
{
    public static bool TryFormatDigits(string value, FieldDefinition field, bool signed, out string digits, out bool negative, out string error)
    {
        digits = string.Empty;
        negative = false;
        error = string.Empty;

        value = value.Trim();
        if (string.IsNullOrEmpty(value))
        {
            error = "数値を入力してください。";
            return false;
        }

        if (value.StartsWith('-'))
        {
            negative = true;
            value = value[1..];
        }
        else if (value.StartsWith('+'))
        {
            value = value[1..];
        }

        if (negative && !signed)
        {
            error = "0以上の数値を入力してください。";
            return false;
        }

        var parts = value.Split('.');
        if (parts.Length > 2 || parts.Any(part => part.Length == 0 || !part.All(char.IsDigit)))
        {
            error = "数値を入力してください。";
            return false;
        }

        var integerPart = parts[0].TrimStart('0');
        if (integerPart.Length == 0)
        {
            integerPart = "0";
        }

        var decimalPart = parts.Length == 2 ? parts[1] : string.Empty;
        if (decimalPart.Length > field.DecimalScale)
        {
            error = $"小数桁数 {field.DecimalScale} を超えています。";
            return false;
        }

        if (integerPart.Length > field.IntegerDigitLength)
        {
            error = $"整数桁数 {field.IntegerDigitLength} を超えています。";
            return false;
        }

        if (field.DecimalScale == 0 && decimalPart.Length > 0)
        {
            error = "小数は入力できません。";
            return false;
        }

        var normalizedInteger = integerPart.PadLeft(field.IntegerDigitLength, '0');
        var normalizedDecimal = decimalPart.PadRight(field.DecimalScale, '0');
        digits = string.Concat(normalizedInteger, normalizedDecimal);

        if (digits.Length != field.Length)
        {
            error = $"桁数 {field.Length} に正規化できません。";
            return false;
        }

        return true;
    }

    public static bool TryFormatDisplayValue(string value, FieldDefinition field, bool signed, out string displayValue, out string error)
    {
        displayValue = value;
        if (!TryFormatDigits(value, field, signed, out var digits, out var negative, out error))
        {
            return false;
        }

        var sign = signed ? negative ? "-" : "+" : string.Empty;
        if (field.DecimalScale == 0)
        {
            displayValue = sign + digits;
            return true;
        }

        var integerPart = digits[..field.IntegerDigitLength];
        var decimalPart = digits[field.IntegerDigitLength..];
        displayValue = $"{sign}{integerPart}.{decimalPart}";
        return true;
    }
}
