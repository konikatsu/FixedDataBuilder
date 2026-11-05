using System.Text;

namespace FixedDataBuilder;

public static class JapaneseEncodings
{
    // Enum values named ShiftJis predate the split and intentionally remain CP932
    // so existing saved settings and callers retain their original behavior.
    public static readonly string[] CsvNames = ["UTF-8", "MS932 (CP932)", "SJIS (JIS X 0208)"];
    public static readonly string[] TextNames = ["MS932 (CP932)", "UTF-8", "SJIS (JIS X 0208)"];
    public static Encoding ForCsv(CsvTextEncoding value) => value switch
    {
        CsvTextEncoding.Utf8 => new UTF8Encoding(false, true),
        CsvTextEncoding.ShiftJis => Ms932(strict: true),
        CsvTextEncoding.StrictShiftJis => StrictShiftJisEncoding.Instance,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
    public static Encoding ForData(DataEncodingProfile value) => value switch
    {
        DataEncodingProfile.ShiftJis => Ms932(strict: false),
        DataEncodingProfile.Utf8WithNationalText => Encoding.UTF8,
        DataEncodingProfile.StrictShiftJis => StrictShiftJisEncoding.Instance,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
    public static string Name(CsvTextEncoding value) => CsvNames[(int)value];
    public static string Name(DataEncodingProfile value) => TextNames[(int)value];
    private static Encoding Ms932(bool strict)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return strict ? Encoding.GetEncoding(932, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)
            : Encoding.GetEncoding(932);
    }
}

/// <summary>ASCII, JIS X 0201 kana and the 6,879 JIS X 0208 characters.
/// No vendor extensions, private-use characters or best-fit substitution.
/// See docs/character-encodings.md for mapping conventions and provenance.</summary>
public sealed class StrictShiftJisEncoding : Encoding
{
    private static readonly Lazy<StrictShiftJisEncoding> Shared = new(() => new());
    public static StrictShiftJisEncoding Instance => Shared.Value;
    private readonly Dictionary<int, char> decode = new();
    private readonly Dictionary<char, int> encode = new();
    private StrictShiftJisEncoding() : base(932, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cp932 = Encoding.GetEncoding(932, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        void Add(int code, char value) { decode.Add(code, value); encode.Add(value, code); }
        for (var value = 0; value <= 0x7f; value++) Add(value, (char)value);
        for (var value = 0xa1; value <= 0xdf; value++) Add(value, (char)(0xff61 + value - 0xa1));
        foreach (var (first, last) in AssignedRanges)
            for (var code = first; code <= last; code++)
            {
                var value = code switch
                {
                    0x8160 => '\u301c', 0x8161 => '\u2016', 0x817c => '\u2212',
                    0x8191 => '\u00a2', 0x8192 => '\u00a3', 0x81ca => '\u00ac',
                    _ => cp932.GetString([(byte)(code >> 8), (byte)code])[0]
                };
                Add(code, value);
            }
    }
    public override string WebName => "shift_jis";
    public override string EncodingName => "SJIS (JIS X 0208)";
    public override int GetMaxByteCount(int charCount) => checked(2 * Math.Max(0, charCount));
    public override int GetMaxCharCount(int byteCount) => checked(Math.Max(0, byteCount) + 1);
    public override int GetByteCount(char[] chars, int index, int count) => GetByteCount(chars.AsSpan(index, count));
    public override int GetByteCount(ReadOnlySpan<char> chars)
    {
        var count = 0;
        foreach (var value in chars) count += Code(value) > 0xff ? 2 : 1;
        return count;
    }
    private int Code(char value) => encode.TryGetValue(value, out var code) ? code
        : throw new EncoderFallbackException($"SJISでは U+{(int)value:X4}（{value}）を表現できません。MS932またはUTF-8を選択してください。");
    public override int GetBytes(char[] chars, int charIndex, int charCount, byte[] bytes, int byteIndex)
        => GetBytes(chars.AsSpan(charIndex, charCount), bytes.AsSpan(byteIndex));
    public override int GetBytes(ReadOnlySpan<char> chars, Span<byte> bytes)
    {
        var needed = GetByteCount(chars);
        if (bytes.Length < needed) throw new ArgumentException("出力バッファが不足しています。");
        var index = 0;
        foreach (var value in chars)
        {
            var code = Code(value);
            if (code > 0xff) bytes[index++] = (byte)(code >> 8);
            bytes[index++] = (byte)code;
        }
        return index;
    }
    private DecoderFallbackException Invalid(int code) => new($"SJISとして読み取れないバイトです: {code:X2}。MS932のファイルの場合はMS932を選択してください。");
    private char Decode(int code) => decode.TryGetValue(code, out var value) ? value : throw Invalid(code);
    private static bool IsLead(byte value) => value is >= 0x81 and <= 0x9f or >= 0xe0 and <= 0xea;
    public override int GetCharCount(byte[] bytes, int index, int count) => GetCharCount(bytes.AsSpan(index, count));
    public override int GetCharCount(ReadOnlySpan<byte> bytes)
    {
        var count = 0;
        for (var index = 0; index < bytes.Length; count++)
        {
            var code = (int)bytes[index++];
            if (IsLead((byte)code))
            {
                if (index == bytes.Length) throw Invalid(code);
                code = (code << 8) | bytes[index++];
            }
            Decode(code);
        }
        return count;
    }
    public override int GetChars(byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex)
        => GetChars(bytes.AsSpan(byteIndex, byteCount), chars.AsSpan(charIndex));
    public override int GetChars(ReadOnlySpan<byte> bytes, Span<char> chars)
    {
        if (chars.Length < GetCharCount(bytes)) throw new ArgumentException("出力バッファが不足しています。");
        var output = 0;
        for (var index = 0; index < bytes.Length;)
        {
            var code = (int)bytes[index++];
            if (IsLead((byte)code)) code = (code << 8) | bytes[index++];
            chars[output++] = Decode(code);
        }
        return output;
    }
    public override Decoder GetDecoder() => new StreamingDecoder(this);
    private sealed class StreamingDecoder(StrictShiftJisEncoding encoding) : Decoder
    {
        private byte? lead;
        public override void Reset() { lead = null; base.Reset(); }
        public override int GetCharCount(byte[] bytes, int index, int count) => GetCharCount(bytes, index, count, false);
        public override int GetCharCount(byte[] bytes, int index, int count, bool flush)
        {
            var copy = new StreamingDecoder(encoding) { lead = lead };
            var chars = new char[encoding.GetMaxCharCount(count)];
            return copy.GetChars(bytes, index, count, chars, 0, flush);
        }
        public override int GetChars(byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex)
            => GetChars(bytes, byteIndex, byteCount, chars, charIndex, false);
        public override int GetChars(byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex, bool flush)
        {
            Convert(bytes, byteIndex, byteCount, chars, charIndex, chars.Length - charIndex, flush,
                out var used, out var written, out _);
            if (used != byteCount) throw new ArgumentException("出力バッファが不足しています。");
            return written;
        }
        public override void Convert(byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex, int charCount,
            bool flush, out int bytesUsed, out int charsUsed, out bool completed)
        {
            var source = bytes.AsSpan(byteIndex, byteCount);
            var target = chars.AsSpan(charIndex, charCount);
            bytesUsed = charsUsed = 0;
            while (bytesUsed < source.Length && charsUsed < target.Length)
            {
                var value = source[bytesUsed++];
                if (lead.HasValue)
                {
                    target[charsUsed++] = encoding.Decode((lead.Value << 8) | value);
                    lead = null;
                }
                else if (IsLead(value)) lead = value;
                else target[charsUsed++] = encoding.Decode(value);
            }
            if (flush && bytesUsed == source.Length && lead.HasValue) throw encoding.Invalid(lead.Value);
            completed = bytesUsed == source.Length && (!flush || !lead.HasValue);
        }
    }

    // Assigned JIS X 0208 positions only; excludes NEC/IBM extensions and gaiji.
    private static readonly (int First, int Last)[] AssignedRanges =
    [
        (0x8140,0x817e),(0x8180,0x81ac),(0x81b8,0x81bf),(0x81c8,0x81ce),(0x81da,0x81e8),(0x81f0,0x81f7),(0x81fc,0x81fc),
        (0x824f,0x8258),(0x8260,0x8279),(0x8281,0x829a),(0x829f,0x82f1),(0x8340,0x837e),(0x8380,0x8396),(0x839f,0x83b6),(0x83bf,0x83d6),
        (0x8440,0x8460),(0x8470,0x847e),(0x8480,0x8491),(0x849f,0x84be),(0x889f,0x88fc),(0x8940,0x897e),(0x8980,0x89fc),
        (0x8a40,0x8a7e),(0x8a80,0x8afc),(0x8b40,0x8b7e),(0x8b80,0x8bfc),(0x8c40,0x8c7e),(0x8c80,0x8cfc),(0x8d40,0x8d7e),(0x8d80,0x8dfc),
        (0x8e40,0x8e7e),(0x8e80,0x8efc),(0x8f40,0x8f7e),(0x8f80,0x8ffc),(0x9040,0x907e),(0x9080,0x90fc),(0x9140,0x917e),(0x9180,0x91fc),
        (0x9240,0x927e),(0x9280,0x92fc),(0x9340,0x937e),(0x9380,0x93fc),(0x9440,0x947e),(0x9480,0x94fc),(0x9540,0x957e),(0x9580,0x95fc),
        (0x9640,0x967e),(0x9680,0x96fc),(0x9740,0x977e),(0x9780,0x97fc),(0x9840,0x9872),(0x989f,0x98fc),(0x9940,0x997e),(0x9980,0x99fc),
        (0x9a40,0x9a7e),(0x9a80,0x9afc),(0x9b40,0x9b7e),(0x9b80,0x9bfc),(0x9c40,0x9c7e),(0x9c80,0x9cfc),(0x9d40,0x9d7e),(0x9d80,0x9dfc),
        (0x9e40,0x9e7e),(0x9e80,0x9efc),(0x9f40,0x9f7e),(0x9f80,0x9ffc),(0xe040,0xe07e),(0xe080,0xe0fc),(0xe140,0xe17e),(0xe180,0xe1fc),
        (0xe240,0xe27e),(0xe280,0xe2fc),(0xe340,0xe37e),(0xe380,0xe3fc),(0xe440,0xe47e),(0xe480,0xe4fc),(0xe540,0xe57e),(0xe580,0xe5fc),
        (0xe640,0xe67e),(0xe680,0xe6fc),(0xe740,0xe77e),(0xe780,0xe7fc),(0xe840,0xe87e),(0xe880,0xe8fc),(0xe940,0xe97e),(0xe980,0xe9fc),
        (0xea40,0xea7e),(0xea80,0xeaa4)
    ];
}
