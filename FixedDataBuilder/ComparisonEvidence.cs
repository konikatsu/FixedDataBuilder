using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FixedDataBuilder;

/// <summary>Records what was compared and how; equality is not a business test verdict.</summary>
public static class ComparisonEvidence
{
    public static string BuildJson(ComparisonDataSet input, ComparisonDataSet output,
        ComparisonRecordPair pair, IReadOnlyList<ComparisonFieldMapping> mappings)
    {
        var settings = ComparisonSettingsStore.Create(input, output, mappings, [pair]);
        var byOutput = mappings.ToDictionary(mapping => mapping.OutputFieldOrdinal);
        var results = output.Fields.Select((field, ordinal) =>
        {
            var mapping = byOutput.TryGetValue(ordinal, out var configured) ? configured
                : new ComparisonFieldMapping(ordinal, ComparisonMappingKind.Unmapped, []);
            return new { outputNumber = ordinal + 1, field.Name, field.DisplayDefinition, field.Comment,
                mapping, result = ComparisonEngine.Compare(input, output, pair, mapping) };
        }).ToArray();
        return JsonSerializer.Serialize(new
        {
            schemaVersion = 1, applicationVersion = typeof(MainForm).Assembly.GetName().Version?.ToString(3),
            exportedAt = DateTimeOffset.Now,
            notice = "一致・差異は設定した行対応・項目対応・式に対する結果です。業務上の合否を自動で判定したものではありません。",
            input = Source(input), output = Source(output),
            inputRecordNumber = pair.InputRecordIndex + 1, outputRecordNumber = pair.OutputRecordIndex + 1,
            settings, results
        }, new JsonSerializerOptions
        {
            WriteIndented = true, MaxDepth = 128, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            Converters = { new JsonStringEnumConverter() }
        });
    }

    public static void Write(string path, ComparisonDataSet input, ComparisonDataSet output,
        ComparisonRecordPair pair, IReadOnlyList<ComparisonFieldMapping> mappings)
    {
        var fullPath = Path.GetFullPath(path);
        foreach (var sourcePath in new[] { input.DefinitionPath, input.DataPath, output.DefinitionPath, output.DataPath })
            if (!string.IsNullOrWhiteSpace(sourcePath) && File.Exists(sourcePath)
                && string.Equals(fullPath, Path.GetFullPath(sourcePath), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("入力・出力・定義ファイルとは別の保存先を選んでください。");
        File.WriteAllText(fullPath, BuildJson(input, output, pair, mappings), new UTF8Encoding(false));
    }

    private static object Source(ComparisonDataSet data) => new
    {
        data.DefinitionPath, data.DataPath, data.DefinitionFingerprint, data.DefinitionFileFingerprint,
        data.SourceFingerprint, data.RecordFingerprint, data.HasSourceBytes, data.Profile,
        recordCount = data.Records.Count, fieldCount = data.Fields.Count,
        sourceDescription = data.HasSourceBytes ? "ファイルから読み込んだ値と元バイト" : "編集画面の値のコピー。元固定長バイトは保持していません。"
    };
}
