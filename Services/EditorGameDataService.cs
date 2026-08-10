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
    private Dictionary<string, string>? _baseGameStrings;
    private string? _loadedDataBarPath;

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

        if (_baseGameStrings is null ||
            !string.Equals(_loadedDataBarPath, dataBarPath, StringComparison.OrdinalIgnoreCase))
        {
            _baseGameStrings = await Task.Run(() => LoadBaseGameStrings(dataBarPath));
            _loadedDataBarPath = dataBarPath;
        }

        return _baseGameStrings.TryGetValue(key, out string? value) ? value : null;
    }

    private static string? GetConfiguredDataBarPath()
    {
        string? path = ProtoEditorSettings.LoadSettings().DataBarPath;
        return !string.IsNullOrWhiteSpace(path) && File.Exists(path) ? Path.GetFullPath(path) : null;
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
