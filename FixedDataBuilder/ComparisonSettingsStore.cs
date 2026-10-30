using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FixedDataBuilder;

/// <summary>Versioned, inert comparison data. Prose comments are never changed by this store.</summary>
public sealed record ComparisonSettings(
    int SchemaVersion,
    int SemanticsVersion,
    string InputDefinitionFingerprint,
    string OutputDefinitionFingerprint,
    string InputRecordFingerprint,
    string OutputRecordFingerprint,
    IReadOnlyList<ComparisonFieldMapping> Mappings,
    IReadOnlyList<ComparisonRecordPair> RecordPairs);

public sealed record ComparisonSettingsLoadResult(ComparisonSettings Settings, bool DefinitionsMatch,
    bool RecordsMatch, IReadOnlyList<string> Issues)
{
    /// <summary>Mismatch requires review; callers must not apply saved row indices automatically.</summary>
    public bool CanApply => DefinitionsMatch && RecordsMatch && Issues.Count == 0;
}

public static class ComparisonSettingsStore
{
    public const int CurrentSchemaVersion = 1;
    public const int CurrentSemanticsVersion = 1;
    public const int MaxFileBytes = 4_000_000;
    public const int MaxMappings = 10_000;
    public const int MaxRecordPairs = 100_000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        MaxDepth = 128,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    public static ComparisonSettings Create(ComparisonDataSet input, ComparisonDataSet output,
        IReadOnlyList<ComparisonFieldMapping> mappings, IReadOnlyList<ComparisonRecordPair> recordPairs)
    {
        var settings = new ComparisonSettings(CurrentSchemaVersion, CurrentSemanticsVersion,
            input.DefinitionFingerprint, output.DefinitionFingerprint, input.RecordFingerprint, output.RecordFingerprint,
            mappings, recordPairs);
        Validate(settings, input, output);
        // Clone using the same serialization contract to detach mutable caller-owned arrays/lists.
        return JsonSerializer.Deserialize<ComparisonSettings>(JsonSerializer.Serialize(settings, JsonOptions), JsonOptions)!;
    }

    public static void Save(string path, ComparisonSettings settings, ComparisonDataSet input, ComparisonDataSet output)
    {
        Validate(settings, input, output);
        if (!FingerprintsMatch(settings, input, output))
            throw new InvalidDataException("比較設定の定義・レコードが現在のデータと一致しません。再確認してください。");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(settings, JsonOptions);
        if (bytes.Length > MaxFileBytes) throw new InvalidDataException("比較設定のファイルサイズが上限を超えます。");
        var target = Path.GetFullPath(path);
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, target, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static ComparisonSettingsLoadResult Load(string path, ComparisonDataSet input, ComparisonDataSet output)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaxFileBytes) throw new InvalidDataException("比較設定のファイルサイズが上限を超えます。");
        ComparisonSettings settings;
        try
        {
            settings = JsonSerializer.Deserialize<ComparisonSettings>(stream, JsonOptions)
                ?? throw new InvalidDataException("比較設定が空です。");
        }
        catch (JsonException ex) { throw new InvalidDataException("比較設定のJSONまたは式の形式が不正です。", ex); }
        catch (NotSupportedException ex) { throw new InvalidDataException("比較設定に未対応の式型があります。", ex); }
        ValidateShape(settings);
        var definitions = settings.InputDefinitionFingerprint == input.DefinitionFingerprint
            && settings.OutputDefinitionFingerprint == output.DefinitionFingerprint;
        var records = settings.InputRecordFingerprint == input.RecordFingerprint
            && settings.OutputRecordFingerprint == output.RecordFingerprint;
        var issues = new List<string>();
        if (!definitions) issues.Add("定義の指紋が変わっています。項目の対応・式を再確認してください。");
        if (!records) issues.Add("レコードの指紋が変わっています。保存した行番号の対応を再確認してください。");
        // A changed definition/count should be reviewable, not silently applied or discarded.
        try { Validate(settings, input, output); }
        catch (InvalidDataException ex) { issues.Add(ex.Message); }
        return new(settings, definitions, records, issues.AsReadOnly());
    }

    public static void Validate(ComparisonSettings settings, ComparisonDataSet input, ComparisonDataSet output)
    {
        ValidateShape(settings);
        var outputs = new HashSet<int>();
        foreach (var mapping in settings.Mappings)
        {
            if (mapping.OutputFieldOrdinal < 0 || mapping.OutputFieldOrdinal >= output.Fields.Count
                || !outputs.Add(mapping.OutputFieldOrdinal))
                throw new InvalidDataException("出力項目番号が範囲外、または対応設定が重複しています。");
            if (mapping.Inputs.Any(reference => reference.FieldOrdinal < 0 || reference.FieldOrdinal >= input.Fields.Count))
                throw new InvalidDataException("対応元の入力項目番号が範囲外です。");
            if (mapping.Expression is not null) ValidateExpression(mapping.Expression, input.Fields.Count);
            if (mapping.Kind == ComparisonMappingKind.Expression && mapping.Expression is not null
                && !mapping.Inputs.ToHashSet().SetEquals(ComparisonEngine.GetInputReferences(mapping.Expression)))
                throw new InvalidDataException("表示用の入力参照と加工式の入力参照が一致しません。項目番号・入力値種別を再確認してください。");
        }
        var pairedOutputs = new HashSet<int>();
        foreach (var pair in settings.RecordPairs)
        {
            if (pair.OutputRecordIndex is int pairedOutput && !pairedOutputs.Add(pairedOutput))
                throw new InvalidDataException("同じ出力レコードに複数の対応設定があります。1件に確定してください。");
            if (pair.InputRecordIndex is int i && (i < 0 || i >= input.Records.Count)
                || pair.OutputRecordIndex is int o && (o < 0 || o >= output.Records.Count)
                || pair.InputCandidates?.Any(index => index < 0 || index >= input.Records.Count) == true
                || pair.OutputCandidates?.Any(index => index < 0 || index >= output.Records.Count) == true)
                throw new InvalidDataException("レコードの対応番号・候補が範囲外です。");
        }
    }

    private static void ValidateShape(ComparisonSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.SchemaVersion != CurrentSchemaVersion || settings.SemanticsVersion != CurrentSemanticsVersion)
            throw new InvalidDataException("未対応の比較設定・式の意味規約バージョンです。");
        foreach (var fingerprint in new[] { settings.InputDefinitionFingerprint, settings.OutputDefinitionFingerprint,
                     settings.InputRecordFingerprint, settings.OutputRecordFingerprint })
            if (fingerprint is null || fingerprint.Length != 64 || fingerprint.Any(ch => !Uri.IsHexDigit(ch)))
                throw new InvalidDataException("比較設定の指紋が不正です。");
        if (settings.Mappings is null || settings.Mappings.Count > MaxMappings
            || settings.RecordPairs is null || settings.RecordPairs.Count > MaxRecordPairs)
            throw new InvalidDataException("比較設定の件数が不正、または上限を超えます。");
        foreach (var mapping in settings.Mappings)
        {
            if (mapping is null || !Enum.IsDefined(mapping.Kind) || !Enum.IsDefined(mapping.OutputSourceKind)
                || mapping.Inputs is null || mapping.Inputs.Count > ComparisonExpressionEvaluator.MaxReferences
                || mapping.Note is null || mapping.Note.Length > ComparisonExpressionEvaluator.MaxValueLength
                || mapping.Inputs.Any(reference => reference is null || reference.FieldOrdinal < 0 || !Enum.IsDefined(reference.SourceKind)))
                throw new InvalidDataException("項目対応の形式が不正です。");
            if (mapping.Kind == ComparisonMappingKind.Input && (mapping.Inputs.Count != 1 || mapping.Expression is not null))
                throw new InvalidDataException("直接比較は入力参照1個とし、加工式は指定しません。");
            if (mapping.Kind is ComparisonMappingKind.Fixed or ComparisonMappingKind.Expression && mapping.Expression is null)
                throw new InvalidDataException("固定値・式の対応には構造化した式が必要です。");
            if (mapping.Expression is not null)
            {
                // Bound depth/nodes even before consulting current fingerprints or definition counts.
                ValidateExpression(mapping.Expression, int.MaxValue);
                if (mapping.Kind == ComparisonMappingKind.Fixed && (mapping.Inputs.Count != 0 || ComparisonEngine.ContainsInput(mapping.Expression)))
                    throw new InvalidDataException("固定値の対応に入力参照が含まれています。");
            }
        }
        foreach (var pair in settings.RecordPairs)
        {
            if (pair is null || !Enum.IsDefined(pair.Kind) || pair.InputRecordIndex is < 0 || pair.OutputRecordIndex is < 0
                || pair.InputRecordIndex is null && pair.OutputRecordIndex is null
                || pair.InputCandidates?.Count > MaxRecordPairs || pair.OutputCandidates?.Count > MaxRecordPairs
                || pair.InputCandidates?.Any(index => index < 0) == true || pair.OutputCandidates?.Any(index => index < 0) == true)
                throw new InvalidDataException("レコード対応の形式が不正です。");
            if (pair.Kind is ComparisonPairKind.Manual or ComparisonPairKind.KeyUnique
                && (pair.InputRecordIndex is null || pair.OutputRecordIndex is null)
                || pair.Kind == ComparisonPairKind.InputOnly && (pair.InputRecordIndex is null || pair.OutputRecordIndex is not null)
                || pair.Kind == ComparisonPairKind.OutputOnly && (pair.OutputRecordIndex is null || pair.InputRecordIndex is not null))
                throw new InvalidDataException("レコード対応の状態と行番号が一致しません。");
        }
    }

    private static void ValidateExpression(ComparisonExpression expression, int inputFieldCount)
    {
        try { ComparisonExpressionEvaluator.Validate(expression, inputFieldCount); }
        catch (ComparisonExpressionEvaluator.ComparisonExpressionException ex) { throw new InvalidDataException(ex.Message, ex); }
    }

    private static bool FingerprintsMatch(ComparisonSettings settings, ComparisonDataSet input, ComparisonDataSet output) =>
        settings.InputDefinitionFingerprint == input.DefinitionFingerprint && settings.OutputDefinitionFingerprint == output.DefinitionFingerprint
        && settings.InputRecordFingerprint == input.RecordFingerprint && settings.OutputRecordFingerprint == output.RecordFingerprint;
}
