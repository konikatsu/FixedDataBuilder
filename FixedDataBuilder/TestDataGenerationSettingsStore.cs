using System.Text.Json;

namespace FixedDataBuilder;

/// <summary>Stores generation choices by definition fingerprint, never record values.</summary>
public sealed class TestDataGenerationSettingsStore(string path)
{
    private const int MaximumEntries = 100;
    private const int MaximumFileBytes = 4 * 1024 * 1024;
    private static readonly GenerationRequirement KnownRequirements = Enum.GetValues<GenerationRequirement>()
        .Aggregate(GenerationRequirement.None, (all, value) => all | value);
    private sealed record Entry(string DefinitionFingerprint, TestDataGenerationSettings Settings);

    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FixedDataBuilder", "test-data-generation-settings.json");

    public TestDataGenerationSettings? Find(string definitionFingerprint)
    {
        if (!ValidKey(definitionFingerprint)) return null;
        var found = Read().FirstOrDefault(entry => string.Equals(entry.DefinitionFingerprint, definitionFingerprint, StringComparison.Ordinal));
        return found is null ? null : Snapshot(found.Settings);
    }

    public bool Remember(string definitionFingerprint, TestDataGenerationSettings settings)
    {
        if (!ValidKey(definitionFingerprint) || !IsValid(settings)) return false;
        string? temporary = null;
        try
        {
            var entries = Read().Where(entry => !string.Equals(entry.DefinitionFingerprint, definitionFingerprint, StringComparison.Ordinal)).ToList();
            entries.Insert(0, new(definitionFingerprint, Snapshot(settings)));
            var bytes = JsonSerializer.SerializeToUtf8Bytes(entries.Take(MaximumEntries));
            if (bytes.Length > MaximumFileBytes) return false;
            var target = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, target, overwrite: true);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or JsonException)
        {
            return false;
        }
        finally
        {
            if (temporary is not null)
            {
                try { File.Delete(temporary); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            }
        }
    }

    internal static bool IsValid(TestDataGenerationSettings? settings) => settings is not null
        && settings.BaselineRecordIndex >= 0 && settings.NormalRecordCount is >= 1 and <= TestDataGenerator.MaximumRecordCount
        && settings.TargetFieldIndexes is { Count: > 0 and <= 10_000 } targets
        && targets.All(index => index is >= 0 and < 10_000) && targets.Distinct().Count() == targets.Count
        && settings.Requirements != GenerationRequirement.None && (settings.Requirements & ~KnownRequirements) == 0
        && settings.OutputOptions is { } options && Enum.IsDefined(options.Format) && Enum.IsDefined(options.CsvEncoding)
        && Enum.IsDefined(options.SeparatorMode) && Enum.IsDefined(options.NationalEncoding)
        && (options.TextEncoding is null || Enum.IsDefined(options.TextEncoding.Value));

    private static bool ValidKey(string? key) => !string.IsNullOrWhiteSpace(key) && key.Length <= 128;
    private static TestDataGenerationSettings Snapshot(TestDataGenerationSettings settings) => settings with
    {
        TargetFieldIndexes = Array.AsReadOnly(settings.TargetFieldIndexes.ToArray())
    };

    private List<Entry> Read()
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > MaximumFileBytes) return [];
            return (JsonSerializer.Deserialize<List<Entry?>>(File.ReadAllBytes(path)) ?? [])
                .Where(entry => entry is not null && ValidKey(entry.DefinitionFingerprint) && IsValid(entry.Settings))
                .Take(MaximumEntries).Select(entry => entry!).ToList();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or JsonException)
        {
            return [];
        }
    }
}
