namespace FixedDataBuilder;

public sealed record GridCaptureBand(int Index, int Length, bool Repeat);
public sealed record GridCaptureSlice(int Index, int Offset, int Length, int Position, int FullLength, bool Repeated);
public sealed record GridCaptureAxisPage(IReadOnlyList<GridCaptureSlice> Slices, int Length);

public static class GridCapturePagination
{
    // Paginate pixels rather than just row/column indices: even an oversized cell
    // must have every part saved. Frozen headings repeat while there is room.
    public static IReadOnlyList<GridCaptureAxisPage> Paginate(IReadOnlyList<GridCaptureBand> bands, int pageSize)
    {
        if (pageSize < 1) throw new ArgumentOutOfRangeException(nameof(pageSize));
        if (bands.Any(band => band.Length < 1) || bands.Select(band => band.Index).Distinct().Count() != bands.Count)
            throw new ArgumentException("見出し・セルの大きさと番号が不正です。", nameof(bands));
        if (bands.Count == 0) return [];
        var fixedLength = bands.Where(band => band.Repeat).Sum(band => (long)band.Length);
        // Very large frozen headings are themselves paginated, never discarded.
        var repeat = fixedLength < pageSize && bands.Any(band => !band.Repeat);
        var headings = repeat ? bands.Where(band => band.Repeat).ToArray() : [];
        var body = (repeat ? bands.Where(band => !band.Repeat) : bands).ToArray();
        var pages = new List<GridCaptureAxisPage>();
        var index = 0;
        var offset = 0;
        while (index < body.Length)
        {
            var slices = new List<GridCaptureSlice>();
            var position = 0;
            foreach (var heading in headings)
            {
                slices.Add(new(heading.Index, 0, heading.Length, position, heading.Length, true));
                position += heading.Length;
            }
            var start = position;
            while (index < body.Length && position < pageSize)
            {
                var band = body[index];
                var remaining = band.Length - offset;
                // Start ordinary cells on the next page instead of cutting them.
                if (offset == 0 && remaining <= pageSize - start && remaining > pageSize - position && position > start) break;
                var length = Math.Min(remaining, pageSize - position);
                slices.Add(new(band.Index, offset, length, position, band.Length, false));
                position += length;
                offset += length;
                if (offset == band.Length) { index++; offset = 0; }
            }
            pages.Add(new(slices.ToArray(), position));
            if (pages.Count > 10000) throw new InvalidOperationException("画像が10,000ページを超えます。文字サイズや列幅を小さくして実行してください。");
        }
        return pages;
    }
}
