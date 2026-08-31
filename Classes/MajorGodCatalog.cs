using System.Xml.Linq;
using AoMDivineDataEditor.GameData;

namespace AoMDivineDataEditor.Classes;

public sealed record MajorGodDefinition(string Name, bool IsBuiltIn, XElement SourceElement);

public static class MajorGodCatalog
{
    public const string BaseFileName = "major_gods.xml.xmb";
    public const string AlternateBaseFileName = "major_gods.xmb";
    public const string ModFileName = "major_gods_mods.xml";
    public const string ModRootName = "civsmods";
    public const string EntryName = "civ";

    public static IReadOnlyList<MajorGodDefinition> ExtractDefinitions(XContainer container, bool isBuiltIn)
        => container.Descendants()
            .Where(element => element.Name.LocalName.Equals(EntryName, StringComparison.OrdinalIgnoreCase))
            .Select(element => new MajorGodDefinition(GetName(element), isBuiltIn, new XElement(element)))
            .Where(definition => definition.Name.Length > 0)
            .GroupBy(definition => definition.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(definition => definition.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static IReadOnlyList<MajorGodDefinition> ExtractBaseDefinitionsFromBar(BarArchive? barFile, string? barPath)
    {
        if (barFile?.Entries == null || string.IsNullOrWhiteSpace(barPath) || !File.Exists(barPath))
            return [];

        var definitions = new Dictionary<string, MajorGodDefinition>(StringComparer.OrdinalIgnoreCase);
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

                foreach (var definition in ExtractDefinitions(XDocument.Parse(xml, LoadOptions.PreserveWhitespace), true))
                    definitions.TryAdd(definition.Name, definition);
            }
            catch
            {
                // Keep entries already decoded if a matching BAR member is malformed.
            }
        }

        return definitions.Values.OrderBy(definition => definition.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static IReadOnlyList<MajorGodDefinition> ReadModFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return [];

        var document = XDocument.Load(path, LoadOptions.PreserveWhitespace);
        RequireModRoot(document);
        return ExtractDefinitions(document, false);
    }

    public static XDocument LoadOrCreateModDocument(string path)
    {
        if (!File.Exists(path))
            return new XDocument(new XElement(ModRootName));

        var document = XDocument.Load(path, LoadOptions.PreserveWhitespace);
        RequireModRoot(document);
        return document;
    }

    public static string GetName(XElement entry)
        => entry.Elements().FirstOrDefault(element =>
               element.Name.LocalName.Equals("name", StringComparison.OrdinalIgnoreCase))?.Value.Trim() ?? "";

    public static XElement? Find(XContainer container, string name)
        => container.Descendants().FirstOrDefault(element =>
            element.Name.LocalName.Equals(EntryName, StringComparison.OrdinalIgnoreCase) &&
            GetName(element).Equals(name, StringComparison.OrdinalIgnoreCase));

    public static XElement RequireModRoot(XDocument document)
    {
        if (document.Root == null || !document.Root.Name.LocalName.Equals(ModRootName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Expected <{ModRootName}> as the major-god mod root.");
        return document.Root;
    }
}
