namespace FixedDataBuilder;

public sealed record FieldDefinition(string Name, FieldDataType Type, int Length, string DefinitionText = "", int DecimalScale = 0)
{
    public string DisplayDefinition => string.IsNullOrWhiteSpace(DefinitionText)
        ? $"{Type}({Length})"
        : DefinitionText;

    public int IntegerDigitLength => Length - DecimalScale;

    public int StorageByteLength => Type switch
    {
        FieldDataType.PackedUnsigned => (Length + 1) / 2,
        FieldDataType.PackedSigned => (Length + 2) / 2,
        FieldDataType.FullWidthText => Length * 2,
        _ => Length
    };
}

public enum FieldDataType
{
    PlainNumber,
    SignedNumber,
    PackedUnsigned,
    PackedSigned,
    Text,
    HalfWidthText,
    FullWidthText
}
