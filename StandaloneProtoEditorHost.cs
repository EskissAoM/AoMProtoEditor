using CryBar;
using CryBar.Bar;
using CryBar.Utilities;
using CryBarEditor.Classes;

namespace AoMProtoEditor;

/// <summary>
/// Gives the standalone editor the game-location context it needs without
/// starting, referencing, or depending on the CryBarEditor application.
/// </summary>
internal sealed class StandaloneProtoEditorHost : IProtoEditorHost
{
    Dictionary<string, string>? _baseGameStrings;

    public BarFile? CurrentBarFile => null;
    public string? CurrentBarPath => null;

    public string? RootDirectory
    {
        get
        {
            var dataBar = ProtoEditorSettings.LoadSettings().DataBarPath;
            if (string.IsNullOrWhiteSpace(dataBar) || !File.Exists(dataBar))
                return null;

            var dataDirectory = Path.GetDirectoryName(dataBar);
            return dataDirectory is null ? null : Directory.GetParent(dataDirectory)?.FullName;
        }
    }

    public async ValueTask<string?> LookupStringKeyAsync(string key)
    {
        _baseGameStrings ??= await Task.Run(LoadBaseGameStrings);
        return _baseGameStrings.TryGetValue(key, out var value) ? value : null;
    }

    private Dictionary<string, string> LoadBaseGameStrings()
    {
        var dataBar = ProtoEditorSettings.LoadSettings().DataBarPath;
        if (string.IsNullOrWhiteSpace(dataBar) || !File.Exists(dataBar))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Data.bar is the authoritative source. It contains one table per
        // language; select English by its archive path rather than accepting
        // the first string_table.txt entry (which can be Czech, for example).
        try
        {
            using var stream = File.OpenRead(dataBar);
            var archive = new BarFile(stream);
            if (!archive.Load(out _) || archive.Entries is null)
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var entry = archive.Entries.FirstOrDefault(candidate =>
                candidate.RelativePath.Replace('\\', '/')
                    .EndsWith("strings/english/string_table.txt", StringComparison.OrdinalIgnoreCase));
            if (entry is null)
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            using var raw = entry.ReadDataRawPooledAsync(stream).AsTask().GetAwaiter().GetResult();
            using var content = BarCompression.EnsureDecompressedPooled(raw, out _);
            return StringTableParser.Parse(ConversionHelper.GetTextContent(content.Span, "string_table.txt"));
        }
        catch
        {
            // A malformed or locked Data.bar should not prevent opening the editor.
        }

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }
}
