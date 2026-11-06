using System.Text;

namespace FixedDataBuilder;

/// <summary>CSV field encodings remain BOM-less; a BOM is a file-level prefix only.</summary>
public static class CsvUnicodeEncoding
{
    public static (CsvTextEncoding? Encoding, int Length) Bom(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith(new byte[] { 0xff, 0xfe, 0, 0 })) return (CsvTextEncoding.Utf32Le, 4);
        if (bytes.StartsWith(new byte[] { 0, 0, 0xfe, 0xff }) || bytes.StartsWith(new byte[] { 0xfe, 0xff }))
            throw new InvalidDataException("CSVのUTF-16BE・UTF-32BEには対応していません。LE（リトルエンディアン）を使用してください。");
        if (bytes.StartsWith(new byte[] { 0xff, 0xfe })) return (CsvTextEncoding.Utf16Le, 2);
        if (bytes.StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) return (CsvTextEncoding.Utf8, 3);
        return (null, 0);
    }

    public static StreamReader OpenReader(string path, CsvTextEncoding selected)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            Span<byte> prefix = stackalloc byte[4];
            var length = stream.ReadAtLeast(prefix, 4, throwOnEndOfStream: false);
            var bom = Bom(prefix[..length]);
            if (bom.Encoding is { } actual && actual != selected)
                throw new InvalidDataException($"CSVのBOMは{JapaneseEncodings.Name(actual)}ですが、指定は{JapaneseEncodings.Name(selected)}です。文字コードを選び直してください。");
            // Remove exactly one matching file preamble; preserve U+FEFF inside data.
            stream.Position = bom.Length;
            return new StreamReader(stream, JapaneseEncodings.ForCsv(selected), detectEncodingFromByteOrderMarks: false);
        }
        catch { stream.Dispose(); throw; }
    }

    public static CsvTextEncoding? BomlessCandidate(byte[] bytes)
    {
        if (!bytes.Contains((byte)0)) return null;
        // UTF-32 must precede UTF-16: its high zero bytes can look like UTF-16 NULs.
        foreach (var candidate in new[] { CsvTextEncoding.Utf32Le, CsvTextEncoding.Utf16Le })
        {
            var width = candidate == CsvTextEncoding.Utf32Le ? 4 : 2;
            if (bytes.Length % width != 0) continue;
            try
            {
                var text = JapaneseEncodings.ForCsv(candidate).GetString(bytes);
                if (text.Length > 0 && text.All(ch => !char.IsControl(ch) || ch is '\r' or '\n' or '\t'))
                    return candidate;
            }
            catch (DecoderFallbackException) { }
        }
        return null;
    }
}
