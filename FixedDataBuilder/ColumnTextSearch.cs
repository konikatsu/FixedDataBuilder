namespace FixedDataBuilder;

public sealed record ColumnTextMatch(int LineIndex, int CharacterIndex, int Length, bool Wrapped);

public static class ColumnTextSearch
{
    // Search without allocating an index of every occurrence in a potentially large file.
    public static ColumnTextMatch? Find(IReadOnlyList<string> lines, string query, bool matchCase,
        ColumnTextMatch? current, bool forward, int startLine = 0)
    {
        if (lines.Count == 0 || query.Length == 0) return null;
        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var first = Math.Clamp(current?.LineIndex ?? startLine, 0, lines.Count - 1);
        for (var step = 0; step <= lines.Count; step++)
        {
            var unwrapped = (long)first + (forward ? step : -step);
            var lineIndex = (int)((unwrapped % lines.Count + lines.Count) % lines.Count);
            var text = lines[lineIndex];
            var minimum = 0;
            var maximum = text.Length - query.Length;
            if (step == 0 && current is not null)
            {
                if (forward) minimum = current.CharacterIndex + 1;
                else maximum = Math.Min(maximum, current.CharacterIndex - 1);
            }
            if (minimum > maximum) continue;
            var range = text.AsSpan(minimum, maximum - minimum + query.Length);
            var found = forward ? range.IndexOf(query.AsSpan(), comparison) : range.LastIndexOf(query.AsSpan(), comparison);
            if (found >= 0)
                return new(lineIndex, minimum + found, query.Length,
                    unwrapped < 0 || unwrapped >= lines.Count || step == lines.Count);
        }
        return null;
    }
}
