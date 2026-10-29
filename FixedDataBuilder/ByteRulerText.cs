using System.Globalization;

namespace FixedDataBuilder;

public static class ByteRulerText
{
    // Bound output by the current column width, even for a huge defined byte length.
    public static string Format(int byteLength, int visiblePositions)
    {
        if (byteLength < 0) throw new ArgumentOutOfRangeException(nameof(byteLength));
        if (byteLength == 0) return "0バイト";
        var slots = Math.Clamp(visiblePositions, 1, 2048);
        var truncated = byteLength > slots;
        var count = Math.Min(byteLength, truncated ? slots - 1 : slots);
        var tens = new string(' ', count).ToCharArray();
        var units = new char[count];
        for (var index = 0; index < count; index++) units[index] = (char)('0' + (index + 1) % 10);
        for (var position = 10; position <= count; position += 10)
        {
            var label = position.ToString(CultureInfo.InvariantCulture);
            label.CopyTo(0, tens, position - label.Length, label.Length);
        }
        return new string(tens) + "\n" + new string(units) + (truncated ? "…" : "");
    }
}
