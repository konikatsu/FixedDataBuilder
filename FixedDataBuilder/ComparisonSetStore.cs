using System.Text.Json;
using System.Text.Json.Serialization;

namespace FixedDataBuilder;

public sealed record ReviewCell(int Record, int Field);
public sealed record ComparisonSnapshot(FieldDefinition[] Fields, string[][] Records, ComparisonDataProfile Profile,
    DefinitionTextEncoding DefinitionEncoding, string? DefinitionPath, string? DataPath, byte[]? SourceBytes,
    string DefinitionFingerprint, string RecordFingerprint)
{
    public static ComparisonSnapshot FromData(ComparisonDataSet data) => new(data.Fields.ToArray(),
        data.Records.Select(row => row.ToArray()).ToArray(), data.Profile, data.DefinitionEncoding,
        data.DefinitionPath, data.DataPath, data.CopySourceBytes(), data.DefinitionFingerprint, data.RecordFingerprint);
}
public sealed record ComparisonPaneView(bool ShowSpaces, bool ShowRuler, int CurrentRecord, int CurrentField,
    int FirstRecord, int FirstField, int HorizontalOffset, int[] ColumnWidths, Dictionary<int, int> RowHeights);
public sealed record ComparisonSetSide(ComparisonSnapshot Data, ReviewCell[] Reviewed, ComparisonPaneView View);
public sealed record ComparisonSetDocument(int Version, bool InputOnTop, ComparisonSetSide? Input, ComparisonSetSide? Output);

/// <summary>A comparison set stores snapshots and human review marks; it never reloads source paths.</summary>
public static class ComparisonSetStore
{
    public const int MaximumBytes = 64 * 1024 * 1024;
    public const int MaximumCells = 1_000_000;
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true, MaxDepth = 64, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static void Validate(ComparisonSetDocument document)
    {
        if (document is null || document.Version != 1)
            throw new InvalidDataException("比較セットの形式・版が正しくありません。");
        if (document.Input is null && document.Output is null)
            throw new InvalidDataException("入力・出力の少なくとも一方を開いてから比較セットを保存してください。");
        foreach (var side in new[] { document.Input, document.Output })
        {
            if (side is null) continue;
            var data = side.Data;
            if (data?.Fields is null || data.Records is null || data.Profile is null || side.Reviewed is null || side.View is null
                || data.Fields.Length is < 1 or > 10_000 || (long)data.Fields.Length * data.Records.Length > MaximumCells)
                throw new InvalidDataException("比較セットのデータ・定義・件数を確認してください。");
            if (data.Fields.Any(field => field is null || !Enum.IsDefined(field.Type) || string.IsNullOrWhiteSpace(field.Name)
                || field.Length <= 0 || field.DecimalScale < 0 || field.DecimalScale >= field.Length
                || field.NationalByteWidth is not (1 or 2 or 4) || field.Comment is null)
                || data.Records.Any(row => row is null || row.Length != data.Fields.Length || row.Any(value => value is null)))
                throw new InvalidDataException("比較セットの項目・行に不正な値があります。");
            var restored = ComparisonDataSet.FromSnapshot(data);
            if (restored.DefinitionFingerprint != data.DefinitionFingerprint || restored.RecordFingerprint != data.RecordFingerprint)
                throw new InvalidDataException("比較セットのデータと確認記録が一致しません。変更・破損を確認してください。");
            bool ValidCell(ReviewCell cell) => cell is not null && cell.Record >= 0 && cell.Record < data.Records.Length
                && cell.Field >= 0 && cell.Field < data.Fields.Length
                && !(data.Profile.Format == DataFileFormat.Csv && data.Fields[cell.Field].IsRedefines);
            if (side.Reviewed.Any(cell => !ValidCell(cell)) || side.Reviewed.Distinct().Count() != side.Reviewed.Length)
                throw new InvalidDataException("確認済みの位置が不正です。");
            var view = side.View;
            if (view.ColumnWidths is null || view.RowHeights is null || view.ColumnWidths.Length != data.Fields.Length + 1
                || view.ColumnWidths.Any(width => width is < 2 or > 65536)
                || view.RowHeights.Any(item => item.Key < 0 || item.Key >= data.Records.Length || item.Value is < 2 or > 65536)
                || view.CurrentRecord < -1 || view.CurrentRecord >= data.Records.Length
                || view.CurrentField < -1 || view.CurrentField >= data.Fields.Length
                || view.FirstRecord < 0 || view.FirstRecord > Math.Max(0, data.Records.Length - 1)
                || view.FirstField < 0 || view.FirstField > data.Fields.Length || view.HorizontalOffset < 0
                || view.HorizontalOffset > view.ColumnWidths.Sum(width => (long)width))
                throw new InvalidDataException("比較セットの表示位置・サイズが不正です。");
        }
    }

    public static ComparisonSetDocument Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaximumBytes) throw new InvalidDataException("比較セットは64MB以内で保存してください。");
        try
        {
            var document = JsonSerializer.Deserialize<ComparisonSetDocument>(stream, Options)
                ?? throw new InvalidDataException("比較セットが空です。");
            Validate(document);
            return document;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or OverflowException)
        { throw new InvalidDataException("比較セットを読み込めません。形式・内容を確認してください。", ex); }
    }

    public static void Write(string path, ComparisonSetDocument document)
    {
        Validate(document);
        var target = Path.GetFullPath(path);
        foreach (var side in new[] { document.Input, document.Output })
        foreach (var source in new[] { side?.Data.DefinitionPath, side?.Data.DataPath })
            if (!string.IsNullOrWhiteSpace(source) && string.Equals(target, Path.GetFullPath(source), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("元の定義・データとは別の名前で比較セットを保存してください。");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document, Options);
        if (bytes.Length > MaximumBytes) throw new InvalidDataException("比較セットが64MBを超えています。比較する範囲を分けてください。");
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) output.Write(bytes);
            File.Move(temporary, target, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
