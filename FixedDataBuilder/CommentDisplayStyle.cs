namespace FixedDataBuilder;

internal static class CommentDisplayStyle
{
    internal static readonly Color ForeColor = Color.FromArgb(80, 101, 122);

    internal static Font CreateFont(Font dataFont) => new(dataFont.FontFamily,
        Math.Max(1f, dataFont.SizeInPoints - 2f), dataFont.Style, GraphicsUnit.Point, dataFont.GdiCharSet);
}
