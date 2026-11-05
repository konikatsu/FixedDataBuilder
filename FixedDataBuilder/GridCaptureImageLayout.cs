namespace FixedDataBuilder;

public sealed record GridCaptureImageLayout(GridCaptureAxisPage Columns, GridCaptureAxisPage Rows, Size ImageSize)
{
    // Bound the allocations for one bitmap and the clipboard's additional copy.
    // These are application limits, not Windows clipboard format limits.
    public const int MaximumDimension = 32767;
    public const long MaximumPixels = 32_000_000;

    public static GridCaptureImageLayout Create(IReadOnlyList<GridCaptureBand> columns,
        IReadOnlyList<GridCaptureBand> rows, int extraHeight)
    {
        if (extraHeight < 0) throw new ArgumentOutOfRangeException(nameof(extraHeight));
        Validate(columns);
        Validate(rows);
        var width = Math.Max(640L, columns.Sum(band => (long)band.Length));
        var height = rows.Sum(band => (long)band.Length) + extraHeight;
        if (width > MaximumDimension || height > MaximumDimension || width * height > MaximumPixels)
            throw new InvalidOperationException($"画像が大きいため、1枚の画像としてコピーできません（{width:N0} × {height:N0} ピクセル）。\r\n"
                + "文字サイズや列幅を小さくするか、「ファイル → 一覧の全範囲を画像保存...」で分割PNGを保存してください。");
        return new(Join(columns), Join(rows), new Size((int)width, (int)height));
    }

    private static void Validate(IReadOnlyList<GridCaptureBand> bands)
    {
        if (bands.Count == 0 || bands.Any(band => band.Length < 1)
            || bands.Select(band => band.Index).Distinct().Count() != bands.Count)
            throw new ArgumentException("見出し・セルの大きさと番号が不正です。", nameof(bands));
    }

    private static GridCaptureAxisPage Join(IReadOnlyList<GridCaptureBand> bands)
    {
        var position = 0;
        var slices = new List<GridCaptureSlice>();
        foreach (var band in bands)
        {
            slices.Add(new(band.Index, 0, band.Length, position, band.Length, false));
            position += band.Length;
        }
        return new(slices.ToArray(), position);
    }
}
