using System.Globalization;

namespace FixedDataBuilder;

public static class DisplayRulerText
{
    public const string Description = "半角1桁・全角2桁の表示幅を示す目盛りです。等幅フォントを基準に、各行の左端を1として5桁ごとに数字と青い破線を表示します。文字コードや格納バイト数には依存しません。";

    public static IEnumerable<int> GuidePositions(int visiblePositions)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(visiblePositions);
        var end = Math.Min(visiblePositions, visiblePositions > 2048 ? 2047 : 2048);
        for (var position = 5; position <= end; position += 5) yield return position;
    }

    // The ruler follows screen columns, regardless of data format or encoding.
    public static string Format(int visiblePositions)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(visiblePositions);
        if (visiblePositions == 0) return "0桁";
        var truncated = visiblePositions > 2048;
        var count = Math.Min(visiblePositions, truncated ? 2047 : 2048);
        var tens = new string(' ', count).ToCharArray();
        var units = new char[count];
        for (var index = 0; index < count; index++) units[index] = (char)('0' + (index + 1) % 10);
        for (var position = 5; position <= count; position += 5)
        {
            var label = position.ToString(CultureInfo.InvariantCulture);
            label.CopyTo(0, tens, position - label.Length, label.Length);
        }
        return new string(tens) + "\n" + new string(units) + (truncated ? "…" : "");
    }
}
