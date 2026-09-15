using System.Text;
using System.Xml.Linq;
using AoMDivineDataEditor.GameData;

namespace AoMDivineDataEditor.Classes;

public sealed record CultureDefinition(string Name, bool IsBuiltIn, XElement SourceElement);

/// <summary>Reads and edits the culture definition catalogs used by the game and active mod.</summary>
public static class CultureCatalog
{
    public const string BaseFileName = "cultures.xml.xmb";
    public const string AlternateBaseFileName = "cultures.xmb";
    public const string ModFileName = "culture_mods.xml";
    public const string BaseRootName = "cultures";
    public const string ModRootName = "culturesmods";
    public const string EntryName = "culture";

    private static readonly string[] OrderedFields =
        ["name", "displaynameid", "compendiumimage", "compendiumtopic", "replaypanelbackground"];

    public static IReadOnlyList<string> ExtractNames(XContainer container)
        => ExtractDefinitions(container, isBuiltIn: false).Select(definition => definition.Name).ToList();

    public static IReadOnlyList<CultureDefinition> ExtractDefinitions(XContainer container, bool isBuiltIn)
        => container.Descendants()
            .Where(element => element.Name.LocalName.Equals(EntryName, StringComparison.OrdinalIgnoreCase))
            .Select(element => new CultureDefinition(ReadName(element), isBuiltIn, new XElement(element)))
            .Where(definition => definition.Name.Length > 0)
            .GroupBy(definition => definition.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(definition => definition.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static IReadOnlyList<CultureDefinition> ExtractBaseDefinitionsFromBar(BarArchive? barFile, string? barPath)
    {
        if (barFile?.Entries == null || string.IsNullOrWhiteSpace(barPath) || !File.Exists(barPath))
            return [];

        var definitions = new Dictionary<string, CultureDefinition>(StringComparer.OrdinalIgnoreCase);
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
                if (read <= 0) continue;
                var xml = XmbReader.ToFormattedXml(bytes.AsSpan(0, read));
                if (string.IsNullOrWhiteSpace(xml)) continue;
                var document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
                if (document.Root == null ||
                    !document.Root.Name.LocalName.Equals(BaseRootName, StringComparison.OrdinalIgnoreCase))
                    continue;
                foreach (var definition in ExtractDefinitions(document, isBuiltIn: true))
                    definitions.TryAdd(definition.Name, definition);
            }
            catch
            {
                // Keep definitions decoded from other matching entries or loose fallbacks.
            }
        }

        return definitions.Values.OrderBy(definition => definition.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static IReadOnlyList<string> ExtractBaseNamesFromBar(BarArchive? barFile, string? barPath)
        => ExtractBaseDefinitionsFromBar(barFile, barPath).Select(definition => definition.Name).ToList();

    public static IReadOnlyList<CultureDefinition> ReadBaseDefinitions(string? path)
        => ReadDefinitions(path, BaseRootName, isBuiltIn: true);

    public static IReadOnlyList<CultureDefinition> ReadModDefinitions(string? path)
        => ReadDefinitions(path, ModRootName, isBuiltIn: false);

    public static IReadOnlyList<string> ReadBaseFile(string? path)
        => ReadBaseDefinitions(path).Select(definition => definition.Name).ToList();

    public static IReadOnlyList<string> ReadModFile(string? path)
        => ReadModDefinitions(path).Select(definition => definition.Name).ToList();

    public static IReadOnlyList<CultureDefinition> MergeDefinitions(
        IEnumerable<CultureDefinition> baseDefinitions,
        IEnumerable<CultureDefinition> modDefinitions)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return baseDefinitions.Concat(modDefinitions)
            .Where(definition => definition.Name.Length > 0 && names.Add(definition.Name))
            .Select(definition => definition with { SourceElement = new XElement(definition.SourceElement) })
            .OrderBy(definition => definition.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<string> Merge(params IEnumerable<string>[] sources)
        => sources.SelectMany(source => source)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static XDocument LoadOrCreateModDocument(string path)
        => ModXmlDocumentSafety.LoadOrCreate(path, ModXmlDocumentKind.Cultures).Document;

    public static XElement AddDefinition(XDocument document, string name, XElement? template = null)
    {
        var root = RequireModRoot(document);
        EnsureAvailableName(root, name);
        var entry = template == null
            ? new XElement(EntryName,
                new XElement("name", name.Trim()),
                new XElement("displaynameid", BuildDisplayNameId(name)),
                new XElement("compendiumimage", ""),
                new XElement("compendiumtopic", ""),
                new XElement("replaypanelbackground", ""))
            : new XElement(template);
        entry.Name = EntryName;
        SetField(entry, "name", name.Trim());
        NormalizeDefinitionIndentation(entry);
        AppendDefinition(root, entry);
        return entry;
    }

    public static bool RenameDefinition(XDocument document, string oldName, string newName)
    {
        var root = RequireModRoot(document);
        var entry = FindDefinition(root, oldName);
        if (entry == null) return false;
        EnsureAvailableName(root, newName, entry);
        SetField(entry, "name", newName.Trim());
        return true;
    }

    public static bool ReplaceDefinition(XDocument document, string originalName, XElement replacement)
    {
        var root = RequireModRoot(document);
        var current = FindDefinition(root, originalName);
        if (current == null) return false;
        EnsureAvailableName(root, ReadName(replacement), current);
        var formatted = new XElement(replacement);
        NormalizeDefinitionIndentation(formatted);
        current.ReplaceWith(formatted);
        return true;
    }

    public static bool DeleteDefinition(XDocument document, string name)
    {
        var entry = FindDefinition(RequireModRoot(document), name);
        if (entry == null) return false;
        RemoveElementAndAdjacentWhitespace(entry);
        return true;
    }

    public static XElement? FindDefinition(XContainer container, string name)
        => container.Elements().FirstOrDefault(element =>
            element.Name.LocalName.Equals(EntryName, StringComparison.OrdinalIgnoreCase) &&
            ReadName(element).Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));

    public static string ReadField(XElement culture, string field)
        => culture.Elements().FirstOrDefault(element =>
            element.Name.LocalName.Equals(field, StringComparison.OrdinalIgnoreCase))?.Value.Trim() ?? "";

    public static void SetField(XElement culture, string field, string? value)
    {
        var existing = culture.Elements().FirstOrDefault(element =>
            element.Name.LocalName.Equals(field, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            XmlTriviaPreserver.SetText(existing, value?.Trim());
            return;
        }

        var newElement = new XElement(field, value?.Trim() ?? "");
        var fieldIndex = Array.FindIndex(OrderedFields, candidate => candidate.Equals(field, StringComparison.OrdinalIgnoreCase));
        var following = culture.Elements().FirstOrDefault(element =>
        {
            var candidateIndex = Array.FindIndex(OrderedFields, candidate =>
                candidate.Equals(element.Name.LocalName, StringComparison.OrdinalIgnoreCase));
            return fieldIndex >= 0 && candidateIndex > fieldIndex;
        });
        if (following != null) following.AddBeforeSelf(newElement);
        else culture.Add(newElement);
    }

    public static string BuildDisplayNameId(string name)
    {
        var normalized = new StringBuilder();
        foreach (var character in name.Trim())
            normalized.Append(char.IsLetterOrDigit(character) ? char.ToUpperInvariant(character) : '_');
        return $"STR_CULTURE_{normalized}";
    }

    public static bool OwnsDisplayNameId(string cultureName, string? stringId)
        => !string.IsNullOrWhiteSpace(stringId) &&
           BuildDisplayNameId(cultureName).Equals(stringId.Trim(), StringComparison.OrdinalIgnoreCase);

    public static OwnedStringRename? RenameOwnedDisplayNameId(
        XElement culture,
        string oldCultureName,
        string newCultureName)
    {
        ArgumentNullException.ThrowIfNull(culture);
        var displayName = culture.Elements().FirstOrDefault(element =>
            element.Name.LocalName.Equals("displaynameid", StringComparison.OrdinalIgnoreCase));
        var oldId = BuildDisplayNameId(oldCultureName);
        if (displayName == null || !displayName.Value.Trim().Equals(oldId, StringComparison.OrdinalIgnoreCase))
            return null;
        var newId = BuildDisplayNameId(newCultureName);
        if (oldId.Equals(newId, StringComparison.OrdinalIgnoreCase))
            return null;
        XmlTriviaPreserver.SetText(displayName, newId);
        return new OwnedStringRename("displaynameid", oldId, newId);
    }

    public static CultureDefinition? FindGeneratedDisplayNameIdCollision(
        string cultureName,
        IEnumerable<CultureDefinition> definitions,
        string? excludedCultureName = null)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        var generatedId = BuildDisplayNameId(cultureName);
        return definitions.FirstOrDefault(definition =>
            !definition.Name.Equals(cultureName, StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrWhiteSpace(excludedCultureName) ||
             !definition.Name.Equals(excludedCultureName, StringComparison.OrdinalIgnoreCase)) &&
            (BuildDisplayNameId(definition.Name).Equals(generatedId, StringComparison.OrdinalIgnoreCase) ||
             ReadField(definition.SourceElement, "displaynameid")
                 .Equals(generatedId, StringComparison.OrdinalIgnoreCase)));
    }

    public static bool IsValidName(string? name) => InternalNamePolicy.IsValid(name);

    private static IReadOnlyList<CultureDefinition> ReadDefinitions(string? path, string expectedRoot, bool isBuiltIn)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return [];
        var document = XDocument.Load(path, LoadOptions.PreserveWhitespace);
        if (document.Root == null || !document.Root.Name.LocalName.Equals(expectedRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Expected <{expectedRoot}> in '{path}'.");
        return ExtractDefinitions(document, isBuiltIn);
    }

    private static XElement RequireModRoot(XDocument document)
    {
        if (document.Root == null || !document.Root.Name.LocalName.Equals(ModRootName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Expected <{ModRootName}>.");
        return document.Root;
    }

    private static string ReadName(XElement element) => ReadField(element, "name");

    private static void EnsureAvailableName(XElement root, string name, XElement? ignored = null)
    {
        if (!IsValidName(name))
            throw new ArgumentException("Culture names can contain only letters, digits, '_' and '-'.", nameof(name));
        if (root.Elements().Any(element => !ReferenceEquals(element, ignored) &&
            element.Name.LocalName.Equals(EntryName, StringComparison.OrdinalIgnoreCase) &&
            ReadName(element).Equals(name.Trim(), StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Culture '{name.Trim()}' already exists.");
    }

    private static void AppendDefinition(XElement root, XElement entry)
    {
        var trailingWhitespace = root.Nodes().LastOrDefault() as XText;
        if (trailingWhitespace != null && string.IsNullOrWhiteSpace(trailingWhitespace.Value))
            trailingWhitespace.AddBeforeSelf(new XText("\n\t"), entry);
        else
            root.Add(new XText("\n\t"), entry, new XText("\n"));
    }

    private static void NormalizeDefinitionIndentation(XElement entry)
    {
        var content = entry.Nodes().Where(node => node is not XText text || !string.IsNullOrWhiteSpace(text.Value)).ToList();
        if (content.Count == 0) return;
        entry.RemoveNodes();
        foreach (var node in content)
            entry.Add(new XText("\n\t\t"), node);
        entry.Add(new XText("\n\t"));
    }

    private static void RemoveElementAndAdjacentWhitespace(XElement element)
    {
        if (element.PreviousNode is XText whitespace && string.IsNullOrWhiteSpace(whitespace.Value))
            whitespace.Remove();
        element.Remove();
    }
}
