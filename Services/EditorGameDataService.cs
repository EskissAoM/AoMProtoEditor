using System.Text;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.GameData;

namespace AoMDivineDataEditor.Services;

public interface IEditorGameDataService
{
    string? RootDirectory { get; }
    ValueTask<string?> LookupStringKeyAsync(string key);
}

/// <summary>Provides configured base-game paths and localized strings to the standalone editor.</summary>
public sealed class EditorGameDataService : IEditorGameDataService
{
    private readonly Func<ProtoEditorSettingsLoadResult> _settings;
    private readonly Func<ProtoEditorSettingsLoadResult> _refreshSettings;
    private readonly object _stringsGate = new();
    private Task<Dictionary<string, string>>? _stringLoad;
    private string? _loadedDataBarPath;

    public EditorGameDataService() : this(ProtoEditorSettings.LoadCachedSettingsResult, ProtoEditorSettings.LoadSettingsResult) { }

    internal EditorGameDataService(ProtoEditorSettingsStore store) : this(store.LoadCached, store.Load) { }

    private EditorGameDataService(Func<ProtoEditorSettingsLoadResult> settings, Func<ProtoEditorSettingsLoadResult> refreshSettings)
    {
        _settings = settings;
        _refreshSettings = refreshSettings;
    }

    /// <summary>Explicitly adopt external settings changes; ordinary warm lookups do not re-read settings.</summary>
    public ProtoEditorSettingsLoadResult RefreshSettings()
    {
        var result = _refreshSettings();
        lock (_stringsGate)
        {
            _stringLoad = null;
            _loadedDataBarPath = null;
        }
        return result;
    }

    public string? RootDirectory
    {
        get
        {
            string? dataBarPath = GetConfiguredDataBarPath();
            if (dataBarPath is null)
                return null;

            string? dataDirectory = Path.GetDirectoryName(dataBarPath);
            return dataDirectory is null ? null : Directory.GetParent(dataDirectory)?.FullName;
        }
    }

    public async ValueTask<string?> LookupStringKeyAsync(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        string? dataBarPath = GetConfiguredDataBarPath();
        if (dataBarPath is null)
            return null;

        Task<Dictionary<string, string>> load;
        lock (_stringsGate)
        {
            if (_stringLoad == null || !string.Equals(_loadedDataBarPath, dataBarPath, StringComparison.OrdinalIgnoreCase))
            {
                _stringLoad = Task.Run(() => LoadBaseGameStrings(dataBarPath));
                _loadedDataBarPath = dataBarPath;
            }
            load = _stringLoad;
        }
        var strings = await load;
        return strings.TryGetValue(key, out string? value) ? value : null;
    }

    private string? GetConfiguredDataBarPath()
    {
        string? path = _settings().Config?.DataBarPath;
        try { return !string.IsNullOrWhiteSpace(path) && File.Exists(path) ? Path.GetFullPath(path) : null; }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        { return null; }
    }

    private static Dictionary<string, string> LoadBaseGameStrings(string dataBarPath)
    {
        try
        {
            using var stream = File.OpenRead(dataBarPath);
            var archive = new BarArchive(stream);
            if (!archive.Load(out _) || archive.Entries is null)
                return EmptyStringTable();

            var entry = archive.Entries.FirstOrDefault(candidate =>
                candidate.RelativePath.Replace('\\', '/')
                    .EndsWith("strings/english/string_table.txt", StringComparison.OrdinalIgnoreCase));
            if (entry is null)
                return EmptyStringTable();

            byte[] content = entry.ReadDataDecompressed(stream);
            return StringTableParser.Parse(Encoding.UTF8.GetString(content));
        }
        catch
        {
            // Missing, malformed, or locked game data must not prevent the editor from opening.
            return EmptyStringTable();
        }
    }

    private static Dictionary<string, string> EmptyStringTable()
        => new(StringComparer.OrdinalIgnoreCase);
}
