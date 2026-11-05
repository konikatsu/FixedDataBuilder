using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FixedDataBuilder;

public sealed record ComparisonEvidenceContext(int? InspectedInputRecordIndex, bool IsCurrentPairConfirmed,
    ComparisonRecordPair? ConfiguredPair, IReadOnlyList<ComparisonMappingSuggestion> Suggestions);

/// <summary>Records what was compared and how; equality is not a business test verdict.</summary>
public static class ComparisonEvidence
{
    public static string BuildJson(ComparisonDataSet input, ComparisonDataSet output,
        ComparisonRecordPair pair, IReadOnlyList<ComparisonFieldMapping> mappings, ComparisonEvidenceContext? context = null) =>
        JsonSerializer.Serialize(BuildRecord(input, output, pair, mappings, context), JsonOptions);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true, MaxDepth = 128, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    private static object BuildRecord(ComparisonDataSet input, ComparisonDataSet output,
        ComparisonRecordPair pair, IReadOnlyList<ComparisonFieldMapping> mappings, ComparisonEvidenceContext? context)
    {
        context ??= new(pair.InputRecordIndex, pair.Kind is ComparisonPairKind.Manual or ComparisonPairKind.KeyUnique,
            pair.Kind == ComparisonPairKind.Unconfirmed ? null : pair, []);
        if (context.InspectedInputRecordIndex is int preview && (preview < 0 || preview >= input.Records.Count))
            throw new InvalidDataException("確認中の入力行番号が範囲外です。");
        if (context.ConfiguredPair is { } stored && stored.OutputRecordIndex != pair.OutputRecordIndex)
            throw new InvalidDataException("保存済みの行対応と選択中の出力行が一致しません。");
        ComparisonSettingsStore.Validate(input, output, mappings, [pair]);
        var settings = ComparisonSettingsStore.Create(input, output, mappings,
            context.ConfiguredPair is { } configuredPair ? [configuredPair] : []);
        var byOutput = mappings.ToDictionary(mapping => mapping.OutputFieldOrdinal);
        var suggestions = (context.Suggestions ?? []).ToDictionary(suggestion => suggestion.OutputFieldOrdinal);
        var results = output.Fields.Select((field, ordinal) =>
        {
            var mapping = byOutput.TryGetValue(ordinal, out var configured) ? configured
                : new ComparisonFieldMapping(ordinal, ComparisonMappingKind.Unmapped, []);
            suggestions.TryGetValue(ordinal, out var suggestion);
            var evaluationMapping = mapping.Kind == ComparisonMappingKind.Unmapped && suggestion?.IsAmbiguous == true
                ? mapping with { Kind = ComparisonMappingKind.Ambiguous } : mapping;
            var result = ComparisonEngine.Compare(input, output, pair, evaluationMapping);
            var pending = mapping.Kind == ComparisonMappingKind.Unmapped && suggestion?.Candidates.Count > 0;
            return new { outputNumber = ordinal + 1, field.Name, field.DisplayDefinition, field.Comment,
                mapping, result, pendingCandidate = pending,
                displayState = pending && result.Status == ComparisonResultStatus.FieldUnmapped ? "候補（未確定）" : StateText(result.Status),
                suggestion,
                candidatePreviews = (suggestion?.Candidates ?? []).Select(candidate => new
                {
                    inputNumber = candidate.InputFieldOrdinal + 1, candidate.InputName,
                    source = context.InspectedInputRecordIndex is int index
                        ? input.ReadSource(index, candidate.InputFieldOrdinal) : null
                }) };
        }).ToArray();
        return new
        {
            schemaVersion = 1, applicationVersion = typeof(MainForm).Assembly.GetName().Version?.ToString(3),
            exportedAt = DateTimeOffset.Now,
            notice = "一致・差異は設定した行対応・項目対応・式に対する結果です。業務上の合否を自動で判定したものではありません。",
            input = Source(input), output = Source(output),
            inputRecordNumber = pair.InputRecordIndex + 1, outputRecordNumber = pair.OutputRecordIndex + 1,
            inspectedInputRecordNumber = context.InspectedInputRecordIndex + 1,
            currentPairConfirmed = context.IsCurrentPairConfirmed, configuredPair = context.ConfiguredPair,
            evaluationPair = pair, numbering = "Index/Ordinalは0始まり、Numberは1始まりです。",
            settings, results
        };
    }

    public static void Write(string path, ComparisonDataSet input, ComparisonDataSet output,
        ComparisonRecordPair pair, IReadOnlyList<ComparisonFieldMapping> mappings, ComparisonEvidenceContext? context = null)
    {
        var fullPath = Path.GetFullPath(path);
        foreach (var sourcePath in new[] { input.DefinitionPath, input.DataPath, output.DefinitionPath, output.DataPath })
            if (!string.IsNullOrWhiteSpace(sourcePath) && File.Exists(sourcePath)
                && string.Equals(fullPath, Path.GetFullPath(sourcePath), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("入力・出力・定義ファイルとは別の保存先を選んでください。");
        var record = BuildRecord(input, output, pair, mappings, context);
        var temporaryPath = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                JsonSerializer.Serialize(stream, record, JsonOptions);
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
    }

    private static object Source(ComparisonDataSet data) => new
    {
        data.DefinitionPath, data.DataPath, data.DefinitionEncoding, data.DefinitionFingerprint, data.DefinitionFileFingerprint,
        data.SourceFingerprint, data.RecordFingerprint, data.HasSourceBytes, data.Profile,
        recordCount = data.Records.Count, fieldCount = data.Fields.Count,
        sourceDescription = data.HasSourceBytes ? "ファイルから読み込んだ値と元バイト" : "編集画面の値のコピー。元固定長バイトは保持していません。"
    };

    public static string StateText(ComparisonResultStatus status) => status switch
    {
        ComparisonResultStatus.DirectValueEqual => "元値一致", ComparisonResultStatus.DirectValueDifferent => "元値不一致",
        ComparisonResultStatus.CalculatedExpectedEqual => "期待値一致", ComparisonResultStatus.CalculatedExpectedDifferent => "期待値不一致",
        ComparisonResultStatus.FieldUnmapped => "項目未対応", ComparisonResultStatus.InputUnmapped => "入力・行未対応",
        ComparisonResultStatus.OutputUnmapped => "出力未対応", ComparisonResultStatus.Ambiguous => "曖昧・未確定",
        ComparisonResultStatus.Unsupported => "未対応の式", _ => "算出不可"
    };
}
