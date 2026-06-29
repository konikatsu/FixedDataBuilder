namespace FixedDataBuilder;

public sealed record FieldDefinition(
    string Name,
    FieldDataType Type,
    int Length,
    string DefinitionText = "",
    int DecimalScale = 0,
    int NationalByteWidth = 2,
    string? RedefinesName = null)
{
    public string DisplayDefinition => string.IsNullOrWhiteSpace(DefinitionText)
        ? $"{Type}({Length})"
        : DefinitionText;

    public int IntegerDigitLength => Length - DecimalScale;

    public bool IsRedefines => !string.IsNullOrWhiteSpace(RedefinesName);

    public int StorageByteLength => IsRedefines ? 0 : PhysicalByteLength;

    public int PhysicalByteLength => Type switch
    {
        FieldDataType.PackedUnsigned => (Length + 1) / 2,
        FieldDataType.PackedSigned => (Length + 2) / 2,
        FieldDataType.FullWidthText => Length * NationalByteWidth,
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
