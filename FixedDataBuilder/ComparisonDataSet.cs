using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FixedDataBuilder;

/// <summary>Every encoding/format choice belongs to one side of a comparison.</summary>
public sealed record ComparisonDataProfile(
    DataFileFormat Format = DataFileFormat.FixedLength,
    CsvTextEncoding CsvEncoding = CsvTextEncoding.Utf8,
    bool CsvHasHeader = false,
    RecordSeparatorMode SeparatorMode = RecordSeparatorMode.CrLfOrLf,
    DataEncodingProfile DataEncoding = DataEncodingProfile.ShiftJis,
    NationalTextEncoding NationalEncoding = NationalTextEncoding.ShiftJis);

public enum ComparisonSourceKind
{
    LogicalValue,
    FixedStorageText,
    RawFixedBytes,
    EncodedCsvLogicalValue
}

public sealed record ComparisonSourceValue(bool Success, string? Value, ComparisonSourceKind Kind,
    string Description, string? Reason = null);

/// <summary>Zero-based source record index and absolute byte range, excluding separators.</summary>
public sealed record ComparisonRawRecord(int RecordIndex, int ByteOffset, int ByteLength);

/// <summary>
/// Immutable snapshot. Logical fixed-length values follow the existing reader's trimming/number rules.
/// Source bytes and exact fixed storage remain separate; CSV logical fields exclude CSV quote syntax.
/// </summary>
public sealed class ComparisonDataSet
{
    private readonly byte[]? sourceBytes;
    private readonly IReadOnlyList<ComparisonRawRecord> rawRecords;
    private readonly int[] fieldOffsets;

    public IReadOnlyList<FieldDefinition> Fields { get; }
    public IReadOnlyList<IReadOnlyList<string>> Records { get; }
    public ComparisonDataProfile Profile { get; }
    /// <summary>The requested definition codec, including Auto, retained independently for this side.</summary>
    public DefinitionTextEncoding DefinitionEncoding { get; }
    public string? DefinitionPath { get; }
    public string? DataPath { get; }
    /// <summary>SHA-256 of the complete logical field definitions, including comments.</summary>
    public string DefinitionFingerprint { get; }
    public string? DefinitionFileFingerprint { get; }
    public string? SourceFingerprint { get; }
    /// <summary>Logical records, profile and available original-byte fingerprint; protects saved row pairs.</summary>
    public string RecordFingerprint { get; }
    public IReadOnlyList<ComparisonRawRecord> RawRecords => rawRecords;
    public bool HasSourceBytes => sourceBytes is not null;

    private ComparisonDataSet(IEnumerable<FieldDefinition> fields, IEnumerable<IEnumerable<string>> records,
        ComparisonDataProfile profile, string? definitionPath, string? dataPath,
        byte[]? source, byte[]? definitionBytes, DefinitionTextEncoding definitionEncoding)
    {
        ValidateProfile(profile);
        if (!Enum.IsDefined(definitionEncoding)) throw new ArgumentException("定義の文字コード指定が不正です。", nameof(definitionEncoding));
        var copiedFields = fields.ToArray();
        if (copiedFields.Any(field => field is null || field.Length <= 0
            || profile.Format == DataFileFormat.FixedLength && field.PhysicalByteLength <= 0))
            throw new ArgumentException("定義に無効な項目があります。", nameof(fields));
        Fields = Array.AsReadOnly(copiedFields);
        var copiedRecords = records.Select(record =>
        {
            var cells = record.ToArray();
            if (cells.Length != copiedFields.Length || cells.Any(value => value is null))
                throw new ArgumentException("レコードの項目数・値が定義と一致しません。", nameof(records));
            return (IReadOnlyList<string>)Array.AsReadOnly(cells);
        }).ToArray();
        Records = Array.AsReadOnly(copiedRecords);
        Profile = profile;
        DefinitionEncoding = definitionEncoding;
        DefinitionPath = definitionPath;
        DataPath = dataPath;
        DefinitionFingerprint = Hash(JsonSerializer.SerializeToUtf8Bytes(copiedFields));
        DefinitionFileFingerprint = definitionBytes is null ? null : Hash(definitionBytes);
        sourceBytes = source?.ToArray();
        SourceFingerprint = sourceBytes is null ? null : Hash(sourceBytes);
        RecordFingerprint = Hash(JsonSerializer.SerializeToUtf8Bytes(new { Profile, Records, SourceFingerprint }));
        // CSV has logical columns, not fixed storage ranges; a skipped REDEFINES alias
        // must not impose a physical boundary on a CSV record.
        fieldOffsets = profile.Format == DataFileFormat.FixedLength ? BuildFieldOffsets(copiedFields) : [];
        rawRecords = Array.AsReadOnly(sourceBytes is not null && profile.Format == DataFileFormat.FixedLength
            ? FindRawRecords(sourceBytes, copiedFields.Sum(field => field.StorageByteLength), profile.SeparatorMode)
            : []);
        if (sourceBytes is not null && profile.Format == DataFileFormat.FixedLength && rawRecords.Count != Records.Count)
            throw new InvalidDataException("元バイトと論理レコードの件数が一致しません。");
    }

    /// <summary>Copy the current output model without reading or replacing a MainForm model.</summary>
    public static ComparisonDataSet FromRecords(IEnumerable<FieldDefinition> fields,
        IEnumerable<IEnumerable<string>> records, ComparisonDataProfile? profile = null,
        string? definitionPath = null, string? dataPath = null, DefinitionTextEncoding definitionEncoding = DefinitionTextEncoding.Auto) =>
        new(fields, records, profile ?? new(), definitionPath, dataPath, null, null, definitionEncoding);

    /// <summary>Read CSV/COPY definitions with existing readers, preserving a stable source-file snapshot.</summary>
    public static ComparisonDataSet Load(string definitionPath, string dataPath, ComparisonDataProfile profile,
        DefinitionTextEncoding definitionEncoding = DefinitionTextEncoding.Auto)
    {
        ValidateProfile(profile);
        // Hold read-only handles while the existing path-based readers parse the same files.
        // FileShare.Read denies concurrent writes/deletion rather than mixing parsed and raw snapshots.
        using var definitionSnapshot = new FileStream(definitionPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var dataSnapshot = new FileStream(dataPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var definitionBytes = SnapshotBytes(definitionSnapshot);
        var extension = Path.GetExtension(definitionPath);
        IReadOnlyList<FieldDefinition> fields = extension.Equals(".cbl", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".cpy", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".cob", StringComparison.OrdinalIgnoreCase)
            ? CopybookDefinitionReader.Read(definitionPath, definitionEncoding)
            : extension.Equals(".csv", StringComparison.OrdinalIgnoreCase)
                ? DefinitionCsvReader.Read(definitionPath, definitionEncoding)
                : throw new NotSupportedException("比較用の定義読込はCSV・COBOLコピー句が対象です。");
        if (profile.Format == DataFileFormat.FixedLength)
        {
            var width = NationalTextEncodingHelper.FixedByteWidth(profile.NationalEncoding);
            fields = fields.Select(field => field.Type == FieldDataType.FullWidthText
                ? field with { NationalByteWidth = width } : field).ToArray();
        }
        var bytes = SnapshotBytes(dataSnapshot);
        var records = profile.Format == DataFileFormat.Csv
            ? CsvDataReader.Read(dataPath, fields, profile.CsvEncoding, profile.CsvHasHeader)
            : FixedLengthDataReader.Read(dataPath, fields, profile.SeparatorMode, profile.DataEncoding, profile.NationalEncoding);
        return new(fields, records, profile, definitionPath, dataPath, bytes, definitionBytes, definitionEncoding);
    }

    private static byte[] SnapshotBytes(FileStream stream)
    {
        if (stream.Length > int.MaxValue) throw new InvalidDataException("比較元ファイルが保持可能なサイズを超えています。");
        var bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        return bytes;
    }

    public byte[]? CopySourceBytes() => sourceBytes?.ToArray();

    public byte[] CopyRawRecordBytes(int recordIndex)
    {
        if (sourceBytes is null || recordIndex < 0 || recordIndex >= rawRecords.Count)
            throw new InvalidOperationException("このレコードには元固定長バイトがありません。");
        var range = rawRecords[recordIndex];
        return sourceBytes.AsSpan(range.ByteOffset, range.ByteLength).ToArray();
    }

    public byte[] CopyRawFieldBytes(int recordIndex, int fieldOrdinal)
    {
        ValidateReference(recordIndex, fieldOrdinal);
        if (Profile.Format != DataFileFormat.FixedLength || sourceBytes is null)
            throw new InvalidOperationException("元固定長バイトは保持されていません。再エンコード値で代用しません。");
        var record = rawRecords[recordIndex];
        return sourceBytes.AsSpan(checked(record.ByteOffset + fieldOffsets[fieldOrdinal]), Fields[fieldOrdinal].PhysicalByteLength).ToArray();
    }

    public ComparisonSourceValue ReadSource(int recordIndex, int fieldOrdinal,
        ComparisonSourceKind kind = ComparisonSourceKind.LogicalValue)
    {
        try
        {
            ValidateReference(recordIndex, fieldOrdinal);
            if (Profile.Format == DataFileFormat.Csv && Fields[fieldOrdinal].IsRedefines)
                throw new InvalidOperationException("CSVに物理列を持たないREDEFINESです。内部初期値を元CSVの値として比較しません。");
            if (kind == ComparisonSourceKind.LogicalValue)
                return new(true, Records[recordIndex][fieldOrdinal], kind,
                    Profile.Format == DataFileFormat.Csv ? "CSV論理値（引用符構文を除去・空白と先頭ゼロを保持）" : "固定長の論理表示値（読込時の空白除去・数値整形あり）");
            if (kind == ComparisonSourceKind.EncodedCsvLogicalValue)
            {
                var bytes = GetByteSource(recordIndex, fieldOrdinal, kind);
                return new(true, GetFieldEncoding(fieldOrdinal).GetString(bytes), kind, "CSV論理値を選択文字コードで符号化（元CSV構文のバイトではありません）");
            }
            if (kind is not (ComparisonSourceKind.FixedStorageText or ComparisonSourceKind.RawFixedBytes))
                throw new NotSupportedException("未対応の入力値種別です。");
            RequireTextStorage(fieldOrdinal);
            return new(true, GetFieldEncoding(fieldOrdinal).GetString(CopyRawFieldBytes(recordIndex, fieldOrdinal)), kind,
                "元固定長項目の格納文字列（末尾空白保持・数値化なし）");
        }
        catch (Exception ex) when (IsSourceFailure(ex))
        {
            return new(false, null, kind, "取得できません", ex.Message);
        }
    }

    internal byte[] GetByteSource(int recordIndex, int fieldOrdinal, ComparisonSourceKind kind)
    {
        ValidateReference(recordIndex, fieldOrdinal);
        if (Profile.Format == DataFileFormat.Csv)
        {
            if (kind is not (ComparisonSourceKind.LogicalValue or ComparisonSourceKind.EncodedCsvLogicalValue))
                throw new InvalidOperationException("CSVには元固定長バイトがありません。");
            if (Fields[fieldOrdinal].IsRedefines) throw new InvalidOperationException("CSVに物理列を持たないREDEFINESです。");
            return GetFieldEncoding(fieldOrdinal).GetBytes(Records[recordIndex][fieldOrdinal]);
        }
        if (kind is not (ComparisonSourceKind.RawFixedBytes or ComparisonSourceKind.FixedStorageText))
            throw new InvalidOperationException("固定長のバイト切取には元固定長バイトの明示指定が必要です。");
        RequireTextStorage(fieldOrdinal);
        return CopyRawFieldBytes(recordIndex, fieldOrdinal);
    }

    internal Encoding GetFieldEncoding(int fieldOrdinal)
    {
        var selected = Profile.Format == DataFileFormat.Csv ? JapaneseEncodings.ForCsv(Profile.CsvEncoding)
            : Fields[fieldOrdinal].Type == FieldDataType.FullWidthText
                ? NationalTextEncodingHelper.GetEncoding(Profile.NationalEncoding)
                : JapaneseEncodings.ForData(Profile.DataEncoding);
        var strict = (Encoding)selected.Clone();
        strict.EncoderFallback = EncoderFallback.ExceptionFallback;
        strict.DecoderFallback = DecoderFallback.ExceptionFallback;
        return strict;
    }

    private void RequireTextStorage(int fieldOrdinal)
    {
        if (Fields[fieldOrdinal].Type is FieldDataType.PackedSigned or FieldDataType.PackedUnsigned)
            throw new NotSupportedException("PACKED格納バイトを文字列として評価できません。論理値を明示してください。");
    }

    private void ValidateReference(int recordIndex, int fieldOrdinal)
    {
        if (recordIndex < 0 || recordIndex >= Records.Count) throw new ArgumentOutOfRangeException(nameof(recordIndex), "入力レコードが範囲外です。");
        if (fieldOrdinal < 0 || fieldOrdinal >= Fields.Count) throw new ArgumentOutOfRangeException(nameof(fieldOrdinal), "項目番号が範囲外です。");
    }

    private static int[] BuildFieldOffsets(IReadOnlyList<FieldDefinition> fields)
    {
        var result = new int[fields.Count];
        var names = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var offset = 0;
        for (var index = 0; index < fields.Count; index++)
        {
            var field = fields[index];
            result[index] = field.IsRedefines && field.RedefinesName is not null
                && names.TryGetValue(field.RedefinesName, out var reference) ? reference : offset;
            if (!field.IsRedefines) { names[field.Name] = offset; offset = checked(offset + field.StorageByteLength); }
        }
        foreach (var (field, index) in fields.Select((field, index) => (field, index)))
            if ((long)result[index] + field.PhysicalByteLength > offset)
                throw new InvalidDataException("REDEFINESの格納範囲がレコード長を超えています。");
        return result;
    }

    private static ComparisonRawRecord[] FindRawRecords(byte[] bytes, int length, RecordSeparatorMode separator)
    {
        if (length <= 0) throw new InvalidDataException("定義に物理項目がありません。");
        var result = new List<ComparisonRawRecord>();
        var offset = 0;
        // Match FixedLengthDataReader's separator handling exactly, retaining original absolute offsets.
        void SkipBreak() { offset += bytes[offset] == 13 && offset + 1 < bytes.Length && bytes[offset + 1] == 10 ? 2 : 1; }
        while (offset < bytes.Length)
        {
            if (separator == RecordSeparatorMode.CrLfOrLf && bytes[offset] is 13 or 10) { SkipBreak(); continue; }
            if ((long)offset + length > bytes.Length) throw new InvalidDataException("元固定長データがレコード長に足りません。");
            result.Add(new(result.Count, offset, length));
            offset += length;
            if (offset < bytes.Length && separator == RecordSeparatorMode.CrLfOrLf && bytes[offset] is 13 or 10) SkipBreak();
        }
        return result.ToArray();
    }

    internal static void ValidateProfile(ComparisonDataProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!Enum.IsDefined(profile.Format) || !Enum.IsDefined(profile.CsvEncoding) || !Enum.IsDefined(profile.SeparatorMode)
            || !Enum.IsDefined(profile.DataEncoding) || !Enum.IsDefined(profile.NationalEncoding))
            throw new ArgumentException("比較用の読込条件が不正です。", nameof(profile));
    }

    internal static bool IsSourceFailure(Exception ex) => ex is ArgumentException or InvalidOperationException
        or NotSupportedException or DecoderFallbackException or EncoderFallbackException or OverflowException;
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
