using System.Text;

namespace FixedDataBuilder;

public enum DefinitionTextEncoding { Auto = 0, Utf8 = 1, ShiftJis = 2, Ms932 = 3 }

public static class DefinitionFileText
{
    public static readonly string[] Names = ["自動判別（UTF-8 / MS932）", "UTF-8", "SJIS (JIS X 0208)", "MS932 (CP932)"];

    public static string Name(DefinitionTextEncoding value) => Names[(int)value];

    public static Encoding ForEncoding(DefinitionTextEncoding value) => value switch
    {
        DefinitionTextEncoding.Utf8 => JapaneseEncodings.ForCsv(CsvTextEncoding.Utf8),
        DefinitionTextEncoding.ShiftJis => JapaneseEncodings.ForCsv(CsvTextEncoding.StrictShiftJis),
        DefinitionTextEncoding.Ms932 => JapaneseEncodings.ForCsv(CsvTextEncoding.ShiftJis),
        _ => throw new ArgumentOutOfRangeException(nameof(value), "保存時はUTF-8・SJIS・MS932のいずれかを指定してください。")
    };

    public static string Read(string path, DefinitionTextEncoding mode = DefinitionTextEncoding.Auto)
    {
        var bytes = File.ReadAllBytes(path);
        if (mode != DefinitionTextEncoding.Auto)
        {
            var offset = mode == DefinitionTextEncoding.Utf8 && bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }) ? 3 : 0;
            return ForEncoding(mode).GetString(bytes, offset, bytes.Length - offset);
        }
        // Keep the BOM-based formats accepted by the previous StreamReader.
        if (bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xfe, 0, 0 })) return new UTF32Encoding(false, false, true).GetString(bytes, 4, bytes.Length - 4);
        if (bytes.AsSpan().StartsWith(new byte[] { 0, 0, 0xfe, 0xff })) return new UTF32Encoding(true, false, true).GetString(bytes, 4, bytes.Length - 4);
        if (bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xfe })) return new UnicodeEncoding(false, false, true).GetString(bytes, 2, bytes.Length - 2);
        if (bytes.AsSpan().StartsWith(new byte[] { 0xfe, 0xff })) return new UnicodeEncoding(true, false, true).GetString(bytes, 2, bytes.Length - 2);
        if (bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) return ForEncoding(DefinitionTextEncoding.Utf8).GetString(bytes, 3, bytes.Length - 3);
        try { return ForEncoding(DefinitionTextEncoding.Utf8).GetString(bytes); }
        catch (DecoderFallbackException) { return ForEncoding(DefinitionTextEncoding.Ms932).GetString(bytes); }
    }

    public static DefinitionTextEncoding SuggestedSaveEncoding(string path, DefinitionTextEncoding mode)
    {
        if (mode != DefinitionTextEncoding.Auto) return mode;
        var bytes = File.ReadAllBytes(path);
        // UTF-16/32 are read for compatibility; new CSV files use one of the three explicit save choices.
        if (bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xfe }) || bytes.AsSpan().StartsWith(new byte[] { 0xfe, 0xff })
            || bytes.AsSpan().StartsWith(new byte[] { 0, 0, 0xfe, 0xff })) return DefinitionTextEncoding.Utf8;
        try { ForEncoding(DefinitionTextEncoding.Utf8).GetString(bytes); return DefinitionTextEncoding.Utf8; }
        catch (DecoderFallbackException) { return DefinitionTextEncoding.Ms932; }
    }

    public static DefinitionTextEncoding Parse(string? value) => value?.Trim().ToLowerInvariant().Replace("_", "").Replace("-", "") switch
    {
        null or "auto" => DefinitionTextEncoding.Auto,
        "utf8" => DefinitionTextEncoding.Utf8,
        "sjis" or "shiftjis" => DefinitionTextEncoding.ShiftJis,
        "ms932" or "cp932" or "windows31j" => DefinitionTextEncoding.Ms932,
        _ => throw new ArgumentException("定義文字コードはauto・utf8・sjis・ms932のいずれかを指定してください。")
    };
}
