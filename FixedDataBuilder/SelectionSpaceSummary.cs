using System.Text;

namespace FixedDataBuilder;

public sealed record SelectionSpaceSummary(int HalfWidth, int FullWidth, int PaddingHalfWidth, int PaddingFullWidth)
{
    public static SelectionSpaceSummary Count(string value, FieldDefinition field, bool isCsv, Encoding textEncoding)
    {
        var half = value.Count(ch => ch == ' ');
        var full = value.Count(ch => ch == '\u3000');
        var padding = 0;
        if (!isCsv && field.Type is FieldDataType.Text or FieldDataType.HalfWidthText or FieldDataType.FullWidthText)
        {
            try
            {
                var used = field.Type == FieldDataType.Text ? textEncoding.GetByteCount(value) : value.Length;
                padding = Math.Max(0, field.Length - used);
            }
            catch (EncoderFallbackException) { /* Invalid values still show their actual spaces. */ }
        }
        var paddingFull = field.Type == FieldDataType.FullWidthText ? padding : 0;
        var paddingHalf = padding - paddingFull;
        return new(half + paddingHalf, full + paddingFull, paddingHalf, paddingFull);
    }

    public string DisplayText => $"半角スペース: {HalfWidth}文字 / 全角スペース: {FullWidth}文字"
        + (PaddingHalfWidth + PaddingFullWidth > 0
            ? $"（うち補完: 半角{PaddingHalfWidth}・全角{PaddingFullWidth}）" : string.Empty);
}
