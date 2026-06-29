namespace FixedDataBuilder;

public static class NationalTextByteWidthDetector
{
    public static IReadOnlyList<FieldDefinition> Detect(
        string path,
        IReadOnlyList<FieldDefinition> fields,
        RecordSeparatorMode separatorMode)
    {
        var nationalIndexes = fields
            .Select((field, index) => (field, index))
            .Where(item => !item.field.IsRedefines && item.field.Type == FieldDataType.FullWidthText)
            .Select(item => item.index)
            .ToList();
        if (nationalIndexes.Count == 0)
        {
            return fields;
        }

        var bytes = File.ReadAllBytes(path);
        var targetLength = separatorMode == RecordSeparatorMode.CrLfOrLf
            ? FirstRecordLengthWithLineBreaks(bytes)
            : (int?)null;

        var staticLength = fields
            .Where(field => !field.IsRedefines && field.Type != FieldDataType.FullWidthText)
            .Sum(field => field.PhysicalByteLength);

        var candidates = EnumerateWidthCandidates(nationalIndexes.Count)
            .Select(widths => new
            {
                Widths = widths,
                RecordLength = staticLength + nationalIndexes
                    .Select((fieldIndex, widthIndex) => fields[fieldIndex].Length * widths[widthIndex])
                    .Sum()
            })
            .Where(candidate => separatorMode == RecordSeparatorMode.CrLfOrLf
                ? candidate.RecordLength == targetLength
                : candidate.RecordLength > 0 && bytes.Length % candidate.RecordLength == 0)
            .OrderBy(candidate => candidate.Widths.Count(width => width == 4))
            .FirstOrDefault();

        if (candidates is null)
        {
            return fields;
        }

        var detectedFields = fields.ToList();
        for (var index = 0; index < nationalIndexes.Count; index++)
        {
            var fieldIndex = nationalIndexes[index];
            detectedFields[fieldIndex] = detectedFields[fieldIndex] with { NationalByteWidth = candidates.Widths[index] };
        }

        return detectedFields;
    }

    private static int FirstRecordLengthWithLineBreaks(byte[] bytes)
    {
        var offset = 0;
        while (offset < bytes.Length && IsLineBreak(bytes[offset]))
        {
            offset = SkipLineBreak(bytes, offset);
        }

        var start = offset;
        while (offset < bytes.Length && !IsLineBreak(bytes[offset]))
        {
            offset++;
        }

        return offset - start;
    }

    private static IEnumerable<int[]> EnumerateWidthCandidates(int count)
    {
        var widths = Enumerable.Repeat(2, count).ToArray();
        foreach (var candidate in EnumerateWidthCandidates(widths, 0))
        {
            yield return candidate;
        }
    }

    private static IEnumerable<int[]> EnumerateWidthCandidates(int[] widths, int index)
    {
        if (index == widths.Length)
        {
            yield return widths.ToArray();
            yield break;
        }

        widths[index] = 2;
        foreach (var candidate in EnumerateWidthCandidates(widths, index + 1))
        {
            yield return candidate;
        }

        widths[index] = 4;
        foreach (var candidate in EnumerateWidthCandidates(widths, index + 1))
        {
            yield return candidate;
        }
    }

    private static bool IsLineBreak(byte value) => value is 0x0D or 0x0A;

    private static int SkipLineBreak(byte[] bytes, int offset)
    {
        if (bytes[offset] == 0x0D && offset + 1 < bytes.Length && bytes[offset + 1] == 0x0A)
        {
            return offset + 2;
        }

        return offset + 1;
    }
}
