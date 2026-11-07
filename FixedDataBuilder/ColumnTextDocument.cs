using System.Text;

namespace FixedDataBuilder;

public enum ColumnTextEncoding { Utf8, Ms932, ShiftJis, Utf16Le, Utf32Le }

/// <summary>A read-only snapshot of physical file lines, including whitespace and CSV quoting.</summary>
public sealed record ColumnTextDocument(string Path, ColumnTextEncoding Encoding, IReadOnlyList<string> Lines)
{
    public const int MaximumBytes = 64 * 1024 * 1024;
    public static readonly string[] EncodingNames = ["UTF-8", "MS932 (CP932)", "SJIS (JIS X 0208)", "UTF-16LE", "UTF-32LE"];

    public static ColumnTextDocument Load(string path, ColumnTextEncoding? encoding = null)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaximumBytes) throw new InvalidDataException("段組みビューでは64MBまでの文字ファイルを開けます。");
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        var selected = encoding ?? Detect(bytes);
        var codec = Codec(selected);
        var preamble = codec.GetPreamble();
        var offset = preamble.Length > 0 && bytes.AsSpan().StartsWith(preamble) ? preamble.Length : 0;
        var text = codec.GetString(bytes.AsSpan(offset));
        if (text.Contains('\0')) throw new InvalidDataException("文字ファイルとして表示できません。文字コードを確認してください。");
        var lines = new List<string>();
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } line) lines.Add(line);
        return new(System.IO.Path.GetFullPath(path), selected, lines.AsReadOnly());
    }

    private static ColumnTextEncoding Detect(byte[] bytes)
    {
        if (bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) return ColumnTextEncoding.Utf8;
        if (bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xfe, 0, 0 })) return ColumnTextEncoding.Utf32Le;
        if (bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xfe })) return ColumnTextEncoding.Utf16Le;
        if (bytes.AsSpan().StartsWith(new byte[] { 0xfe, 0xff }) || bytes.AsSpan().StartsWith(new byte[] { 0, 0, 0xfe, 0xff }))
            throw new InvalidDataException("この文字コードには対応していません。UTF-8・SJIS・MS932・UTF-16LE・UTF-32LEを使用してください。");
        try { Codec(ColumnTextEncoding.Utf8).GetCharCount(bytes); return ColumnTextEncoding.Utf8; }
        catch (DecoderFallbackException) { Codec(ColumnTextEncoding.Ms932).GetCharCount(bytes); return ColumnTextEncoding.Ms932; }
    }

    public static Encoding Codec(ColumnTextEncoding encoding) => encoding switch
    {
        ColumnTextEncoding.Utf8 => new UTF8Encoding(true, true),
        ColumnTextEncoding.Ms932 => JapaneseEncodings.ForCsv(CsvTextEncoding.ShiftJis),
        ColumnTextEncoding.ShiftJis => JapaneseEncodings.ForCsv(CsvTextEncoding.StrictShiftJis),
        ColumnTextEncoding.Utf16Le => new UnicodeEncoding(false, true, true),
        ColumnTextEncoding.Utf32Le => new UTF32Encoding(false, true, true),
        _ => throw new ArgumentOutOfRangeException(nameof(encoding))
    };
}

public sealed record ColumnPageLayout(int Columns, int RowsPerColumn)
{
    public int Capacity => checked(Columns * RowsPerColumn);
    public static ColumnPageLayout Fit(int width, int height, int columnWidth, int rowHeight, int reservedHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(columnWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rowHeight);
        return new(Math.Clamp(width / columnWidth, 1, 32), Math.Clamp((Math.Max(0, height - reservedHeight)) / rowHeight, 1, 10000));
    }

    public static int[] FitPaneWidths(IReadOnlyList<int> preferredWidths, int availableWidth)
    {
        var widths = new int[preferredWidths.Count];
        if (availableWidth <= 0) return widths;

        var byWidth = Enumerable.Range(0, preferredWidths.Count)
            .Where(index => preferredWidths[index] > 0)
            .OrderBy(index => preferredWidths[index])
            .ToArray();
        // Give smaller requests their preferred width when it fits within their share.
        for (var i = 0; i < byWidth.Length; i++)
        {
            var index = byWidth[i];
            var width = Math.Min(preferredWidths[index], availableWidth / (byWidth.Length - i));
            widths[index] = width;
            availableWidth -= width;
        }
        return widths;
    }

    public int PageCount(int lineCount) => (int)Math.Max(1, ((long)lineCount + Capacity - 1) / Capacity);
    public int ClampPage(int page, int lineCount) => Math.Clamp(page, 0, PageCount(lineCount) - 1);
    public int FirstLine(int page, int lineCount) => ClampPage(page, lineCount) * Capacity;
    public int LineIndex(int page, int column, int row, int lineCount)
    {
        if (column < 0 || column >= Columns || row < 0 || row >= RowsPerColumn) return -1;
        var index = (long)FirstLine(page, lineCount) + (long)column * RowsPerColumn + row;
        return index < lineCount ? (int)index : -1;
    }
}
