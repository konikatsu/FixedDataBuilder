using System.Text.Json;

namespace FixedDataBuilder;

public sealed record DataLoadSettings(DataFileFormat Format, CsvTextEncoding CsvEncoding,
    RecordSeparatorMode SeparatorMode, NationalTextEncoding NationalEncoding, DataEncodingProfile? TextEncoding = null, bool CsvHasHeader = false);

public sealed class DataLoadSettingsStore(string path)
{
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FixedDataBuilder", "data-load-settings.json");

    private sealed record Entry(string DefinitionPath, string DataPath, string? LayoutId, DataLoadSettings Settings);

    public DataLoadSettings? Find(string definitionPath, string dataPath, string? layoutId = null)
    {
        if (string.IsNullOrWhiteSpace(definitionPath) || string.IsNullOrWhiteSpace(dataPath)) return null;
        var definition = NormalizePath(definitionPath);
        var data = NormalizePath(dataPath);
        return Read().FirstOrDefault(entry => Matches(entry, definition, data, layoutId))?.Settings;
    }

    public bool Remember(string definitionPath, string dataPath, DataLoadSettings settings, string? layoutId = null)
    {
        if (string.IsNullOrWhiteSpace(definitionPath) || string.IsNullOrWhiteSpace(dataPath)) return false;
        string? temporary = null;
        try
        {
            var definition = NormalizePath(definitionPath);
            var data = NormalizePath(dataPath);
            var entries = Read().Where(entry => !Matches(entry, definition, data, layoutId)).ToList();
            entries.Insert(0, new Entry(definition, data, layoutId, settings));
            var target = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(entries.Take(100)));
            File.Move(temporary, target, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
        finally
        {
            if (temporary is not null)
            {
                try { File.Delete(temporary); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
    }

    private List<Entry> Read()
    {
        try
        {
            if (!File.Exists(path)) return [];
            return (JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(path)) ?? [])
                .Where(entry => entry is not null && entry.DefinitionPath is not null && entry.DataPath is not null
                    && entry.Settings is not null && Enum.IsDefined(entry.Settings.Format)
                    && Enum.IsDefined(entry.Settings.CsvEncoding) && Enum.IsDefined(entry.Settings.SeparatorMode)
                    && Enum.IsDefined(entry.Settings.NationalEncoding)
                    && (entry.Settings.TextEncoding is null || Enum.IsDefined(entry.Settings.TextEncoding.Value))).Take(100).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    private static bool Matches(Entry entry, string definition, string data, string? layoutId) =>
        string.Equals(entry.DefinitionPath, definition, StringComparison.OrdinalIgnoreCase)
        && string.Equals(entry.DataPath, data, StringComparison.OrdinalIgnoreCase)
        && string.Equals(entry.LayoutId, layoutId, StringComparison.Ordinal);

    private static string NormalizePath(string value) => value.StartsWith("(Excel) ", StringComparison.Ordinal)
        ? "(Excel) " + Path.GetFullPath(value[8..]) : Path.GetFullPath(value);
}
