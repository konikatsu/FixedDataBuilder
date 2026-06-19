namespace FixedDataBuilder;

public static class PackedDecimal
{
    public static byte[] Encode(string value, int digitLength, bool signed)
    {
        var negative = signed && value.StartsWith('-');
        var digits = negative ? value[1..] : value;

        if (digits.Length > digitLength)
        {
            throw new InvalidDataException($"{value} は桁数 {digitLength} を超えています。");
        }

        digits = digits.PadLeft(digitLength, '0');
        var nibbles = signed
            ? digits.Select(ToNibble).Append(negative ? 0x0D : 0x0C).ToList()
            : digits.Select(ToNibble).ToList();

        if (nibbles.Count % 2 != 0)
        {
            nibbles.Insert(0, 0);
        }

        var bytes = new byte[nibbles.Count / 2];
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] = (byte)((nibbles[i * 2] << 4) | nibbles[i * 2 + 1]);
        }

        return bytes;
    }

    private static int ToNibble(char ch)
    {
        if (ch is < '0' or > '9')
        {
            throw new InvalidDataException($"PAC には数値のみ指定できます: {ch}");
        }

        return ch - '0';
    }
}
