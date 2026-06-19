namespace FixedDataBuilder;

public sealed record FieldDefinition(string Name, FieldDataType Type, int Length)
{
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
    PackedUnsigned,
    PackedSigned,
    HalfWidthText,
    FullWidthText
}
