using System.Xml.Linq;
using AoMDivineDataEditor.GameData;

namespace AoMDivineDataEditor.Classes;

/// <summary>
/// Discovers culture identifiers from the game and active mod instead of
/// maintaining a second, editor-owned list that becomes stale as content grows.
/// </summary>
public static class CultureCatalog
{
    public const string BaseFileName = "cultures.xml.xmb";
    public const string AlternateBaseFileName = "cultures.xmb";
    public const string ModFileName = "culture_mods.xml";
    public const string AlternateModFileName = "cultures_mods.xml";
    public const string BaseRootName = "cultures";
    public const string ModRootName = "culturesmods";
    public const string EntryName = "culture";

    public static IReadOnlyList<string> ExtractNames(XContainer container)
        => container.Descendants()
            .Where(element => element.Name.LocalName.Equals(EntryName, StringComparison.OrdinalIgnoreCase))
            .Select(element => element.Elements().FirstOrDefault(child =>
                child.Name.LocalName.Equals("name", StringComparison.OrdinalIgnoreCase))?.Value.Trim() ?? "")
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static IReadOnlyList<string> ExtractBaseNamesFromBar(BarArchive? barFile, string? barPath)
    {
        if (barFile?.Entries == null || string.IsNullOrWhiteSpace(barPath) || !File.Exists(barPath))
            return [];

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = barFile.Entries.Where(entry =>
        {
            var fileName = entry.Name.Replace('\\', '/').Split('/').LastOrDefault() ?? "";
            return fileName.Equals(BaseFileName, StringComparison.OrdinalIgnoreCase) ||
                   fileName.Equals(AlternateBaseFileName, StringComparison.OrdinalIgnoreCase);
        });

        using var stream = File.OpenRead(barPath);
        foreach (var entry in entries)
        {
            try
            {
                var size = entry.IsCompressed ? entry.SizeUncompressed : entry.SizeInArchive;
                var bytes = new byte[size];
                var read = entry.ReadDataDecompressed(stream, bytes);
                if (read <= 0)
                    continue;

                var xml = XmbReader.ToFormattedXml(bytes.AsSpan(0, read));
                if (string.IsNullOrWhiteSpace(xml))
                    continue;

                var document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
                if (document.Root == null ||
                    !document.Root.Name.LocalName.Equals(BaseRootName, StringComparison.OrdinalIgnoreCase))
                    continue;

                names.UnionWith(ExtractNames(document));
            }
            catch
            {
                // Keep cultures decoded from other matching entries or fallbacks.
            }
        }

        return names.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static IReadOnlyList<string> ReadBaseFile(string? path)
        => ReadFile(path, BaseRootName);

    public static IReadOnlyList<string> ReadModFile(string? path)
        => ReadFile(path, ModRootName);

    public static IReadOnlyList<string> Merge(params IEnumerable<string>[] sources)
        => sources.SelectMany(source => source)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static IReadOnlyList<string> ReadFile(string? path, string expectedRoot)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return [];

        var document = XDocument.Load(path, LoadOptions.PreserveWhitespace);
        if (document.Root == null ||
            !document.Root.Name.LocalName.Equals(expectedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Expected <{expectedRoot}> in '{path}'.");
        }

        return ExtractNames(document);
    }
}
