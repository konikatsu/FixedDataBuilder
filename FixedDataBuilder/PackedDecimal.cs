namespace FixedDataBuilder;

public static class PackedDecimal
{
    public static byte[] EncodeDigits(string digits, bool signed, bool negative)
    {
        if (digits.Length == 0 || !digits.All(char.IsDigit))
        {
            throw new InvalidDataException("PAC には数値のみ指定できます。");
        }

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

    public static string DecodeDigits(ReadOnlySpan<byte> bytes, int digitLength, bool signed, out bool negative)
    {
        negative = false;
        var nibbles = new List<int>(bytes.Length * 2);
        foreach (var value in bytes)
        {
            nibbles.Add((value >> 4) & 0x0F);
            nibbles.Add(value & 0x0F);
        }

        if (signed)
        {
            if (nibbles.Count == 0)
            {
                throw new InvalidDataException("PAC データが空です。");
            }

            var signNibble = nibbles[^1];
            negative = signNibble == 0x0D;
            if (signNibble is not (0x0C or 0x0D or 0x0F))
            {
                throw new InvalidDataException($"未対応の PAC 符号ニブルです: {signNibble:X}");
            }
            nibbles.RemoveAt(nibbles.Count - 1);
        }

        while (nibbles.Count > digitLength)
        {
            if (nibbles[0] != 0)
            {
                throw new InvalidDataException("PAC データの桁数が定義より長いです。");
            }
            nibbles.RemoveAt(0);
        }

        if (nibbles.Count < digitLength)
        {
            throw new InvalidDataException("PAC データの桁数が定義より短いです。");
        }

        if (nibbles.Any(nibble => nibble is < 0 or > 9))
        {
            throw new InvalidDataException("PAC データに数値以外のニブルがあります。");
        }

        return string.Concat(nibbles.Select(nibble => (char)('0' + nibble)));
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
