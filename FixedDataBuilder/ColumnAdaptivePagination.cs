namespace FixedDataBuilder;

// A page owns consecutive whole groups of physical lines; groups can have different widths.
public sealed class ColumnAdaptivePagination
{
    public sealed record Page(int Start, int Columns, int End);
    private readonly List<Page> pages;
    public int RowsPerColumn { get; }
    public int Count => pages.Count;
    public Page this[int index] => pages[Math.Clamp(index, 0, pages.Count - 1)];

    private ColumnAdaptivePagination(List<Page> pages, int rows) { this.pages = pages; RowsPerColumn = rows; }

    public static ColumnAdaptivePagination Build(int lineCount, int rows, int availableWidth,
        Func<int, int, int> groupWidth, CancellationToken cancellationToken = default)
    {
        rows = Math.Max(1, rows);
        availableWidth = Math.Max(1, availableWidth);
        var result = new List<Page>();
        var start = 0;
        while (start < lineCount)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var end = start;
            var columns = 0;
            long used = 0;
            while (end < lineCount && columns < 32)
            {
                var next = (int)Math.Min((long)end + rows, lineCount);
                var width = Math.Max(1, groupWidth(end, next));
                if (columns > 0 && used + width > availableWidth) break;
                used += width;
                columns++;
                end = next;
            }
            result.Add(new Page(start, columns, end));
            start = end;
        }
        if (result.Count == 0) result.Add(new Page(0, 1, 0));
        return new ColumnAdaptivePagination(result, rows);
    }

    public int PageForLine(int line)
    {
        var low = 0;
        var high = pages.Count - 1;
        while (low < high)
        {
            var mid = low + (high - low + 1) / 2;
            if (pages[mid].Start <= line) low = mid; else high = mid - 1;
        }
        return low;
    }
}

