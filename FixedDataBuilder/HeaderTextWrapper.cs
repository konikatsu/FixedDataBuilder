using System.Globalization;

namespace FixedDataBuilder;

public static class HeaderTextWrapper
{
    public static IReadOnlyList<string> Lines(string text, int width, Func<string, int> measure, int maximumLines = 3)
    {
        if (width < 1) throw new ArgumentOutOfRangeException(nameof(width));
        if (maximumLines < 1) throw new ArgumentOutOfRangeException(nameof(maximumLines));
        var lines = new List<string> { "" };
        var elements = StringInfo.GetTextElementEnumerator(text.Replace("\r\n", "\n").Replace('\r', '\n'));
        var truncated = false;
        while (elements.MoveNext())
        {
            var element = elements.GetTextElement();
            if (element != "\n" && (lines[^1].Length == 0 || measure(lines[^1] + element) <= width))
            {
                lines[^1] += element;
                continue;
            }
            if (lines.Count == maximumLines) { truncated = true; break; }
            lines.Add(element == "\n" ? "" : element);
        }
        if (truncated)
        {
            var last = lines[^1];
            while (last.Length > 0 && measure(last + "…") > width)
            {
                var indexes = StringInfo.ParseCombiningCharacters(last);
                last = last[..indexes[^1]];
            }
            lines[^1] = last + "…";
        }
        return lines.ToArray();
    }
}
