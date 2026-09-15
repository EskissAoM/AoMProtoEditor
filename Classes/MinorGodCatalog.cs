using System.Xml.Linq;
using AoMDivineDataEditor.GameData;

namespace AoMDivineDataEditor.Classes;

public sealed record MinorGodDefinition(
    string Name,
    string TechName,
    bool IsBuiltIn,
    XElement SourceElement);

/// <summary>Reads and edits the age-prefixed minor-god catalogs.</summary>
public static class MinorGodCatalog
{
    public const string BaseFileName = "minor_gods.xml.xmb";
    public const string AlternateBaseFileName = "minor_gods.xmb";
    public const string ModFileName = "minor_gods_mods.xml";
    public const string BaseRootName = "minorgods";
    public const string ModRootName = "minorgodsmods";
    public const string PortraitAttribute = "portraitfilename";
    public const string IconAttribute = "portraiticonfilename";

    private static readonly string[] AgePrefixes =
        ["ArchaicAge", "ClassicalAge", "HeroicAge", "MythicAge", "WonderAge"];

    public static IReadOnlyList<MinorGodDefinition> ExtractDefinitions(XContainer container, bool isBuiltIn)
        => container.Elements()
            .Select(element => CreateDefinition(element, isBuiltIn))
            .Where(definition => definition.TechName.Length > 0)
            .GroupBy(definition => definition.TechName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(definition => definition.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(definition => definition.TechName, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static IReadOnlyList<MinorGodDefinition> Canonicalize(
        IEnumerable<MinorGodDefinition> definitions,
        IEnumerable<string> technologyNames)
    {
        var technologies = technologyNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(name => name, name => name, StringComparer.OrdinalIgnoreCase);
        return definitions.Select(definition =>
        {
            var techName = technologies.GetValueOrDefault(definition.TechName) ?? definition.TechName;
            var element = new XElement(definition.SourceElement) { Name = techName };
            return definition with
            {
                Name = GetDisplayName(techName),
                TechName = techName,
                SourceElement = element
            };
        }).ToList();
    }

    public static IReadOnlyList<string> ExtractAgeUpgradeTechnologyNames(IEnumerable<XDocument> documents)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var technology in documents.SelectMany(document => document.Descendants()).Where(element =>
                     element.Name.LocalName.Equals("tech", StringComparison.OrdinalIgnoreCase)))
        {
            var isAgeUpgrade = technology.Elements().Any(element =>
                element.Name.LocalName.Equals("flag", StringComparison.OrdinalIgnoreCase) &&
                element.Value.Trim().Equals("AgeUpgrade", StringComparison.OrdinalIgnoreCase));
            var name = technology.Attributes().FirstOrDefault(attribute =>
                attribute.Name.LocalName.Equals("name", StringComparison.OrdinalIgnoreCase))?.Value.Trim();
            if (isAgeUpgrade && !string.IsNullOrWhiteSpace(name)) names.Add(name);
        }
        return names.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static int AddMissingDefinitionsForAgeUpgradeTechnologies(
        XDocument minorGodDocument,
        IEnumerable<XDocument> technologyDocuments)
    {
        var added = 0;
        var usedDisplayNames = ExtractDefinitions(RequireModRoot(minorGodDocument), isBuiltIn: false)
            .Select(definition => definition.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var techName in ExtractAgeUpgradeTechnologyNames(technologyDocuments).Where(name =>
                     name.StartsWith("ClassicalAge", StringComparison.OrdinalIgnoreCase) ||
                     name.StartsWith("HeroicAge", StringComparison.OrdinalIgnoreCase) ||
                     name.StartsWith("MythicAge", StringComparison.OrdinalIgnoreCase)))
        {
            var displayName = GetDisplayName(techName);
            if (displayName.Length == 0 ||
                usedDisplayNames.Contains(displayName) ||
                FindDefinition(RequireModRoot(minorGodDocument), techName) != null)
                continue;
            AddDefinition(minorGodDocument, techName);
            usedDisplayNames.Add(displayName);
            added++;
        }
        return added;
    }

    public static int ApplyTechnologyLifecycle(
        XDocument minorGodDocument,
        XDocument technologyDocument,
        IEnumerable<TechnologyRenameOperation> renames,
        IEnumerable<string> removedTechnologyNames)
    {
        ArgumentNullException.ThrowIfNull(minorGodDocument);
        ArgumentNullException.ThrowIfNull(technologyDocument);
        var changed = 0;

        // Removal is applied first because a technology may have been renamed and
        // then deleted (or had AgeUpgrade removed) before the deferred save.
        foreach (var techName in removedTechnologyNames
                     .Where(name => !string.IsNullOrWhiteSpace(name))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (DeleteDefinition(minorGodDocument, techName))
                changed++;
        }

        foreach (var rename in renames)
        {
            if (FindDefinition(RequireModRoot(minorGodDocument), rename.OldName) == null)
                continue;
            if (!RenameDefinition(minorGodDocument, rename.OldName, rename.NewName))
                continue;
            changed++;
        }

        changed += AddMissingDefinitionsForAgeUpgradeTechnologies(minorGodDocument, [technologyDocument]);
        EnsureUniqueDisplayNames(minorGodDocument);
        return changed;
    }

    public static void EnsureUniqueDisplayNames(XDocument document)
    {
        var duplicate = RequireModRoot(document).Elements()
            .Select(element => new { TechName = element.Name.LocalName, DisplayName = GetDisplayName(element.Name.LocalName) })
            .Where(item => item.DisplayName.Length > 0)
            .GroupBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate == null)
            return;
        throw new InvalidDataException(
            $"Minor God name '{duplicate.Key}' is derived by more than one AgeUpgrade technology: " +
            string.Join(", ", duplicate.Select(item => item.TechName)) + ".");
    }

    public static IReadOnlyList<MinorGodDefinition> ExtractBaseDefinitionsFromBar(BarArchive? barFile, string? barPath)
    {
        if (barFile?.Entries == null || string.IsNullOrWhiteSpace(barPath) || !File.Exists(barPath))
            return [];

        var definitions = new Dictionary<string, MinorGodDefinition>(StringComparer.OrdinalIgnoreCase);
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
                foreach (var definition in ExtractDefinitions(document.Root, isBuiltIn: true))
                    definitions.TryAdd(definition.TechName, definition);
            }
            catch
            {
                // Keep entries decoded from other matching BAR entries or loose fallbacks.
            }
        }

        return definitions.Values.ToList();
    }

    public static IReadOnlyList<MinorGodDefinition> ReadBaseDefinitions(string? path)
        => ReadDefinitions(path, BaseRootName, isBuiltIn: true);

    public static IReadOnlyList<MinorGodDefinition> ReadModDefinitions(string? path)
        => ReadDefinitions(path, ModRootName, isBuiltIn: false);

    public static IReadOnlyList<MinorGodDefinition> MergeDefinitions(
        IEnumerable<MinorGodDefinition> baseDefinitions,
        IEnumerable<MinorGodDefinition> modDefinitions)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return baseDefinitions.Concat(modDefinitions)
            .Where(definition => definition.TechName.Length > 0 && names.Add(definition.TechName))
            .Select(definition => definition with { SourceElement = new XElement(definition.SourceElement) })
            .OrderBy(definition => definition.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(definition => definition.TechName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static XDocument LoadOrCreateModDocument(string path)
        => ModXmlDocumentSafety.LoadOrCreate(path, ModXmlDocumentKind.MinorGods).Document;

    public static XElement AddDefinition(XDocument document, string techName, XElement? template = null)
    {
        var root = RequireModRoot(document);
        EnsureAvailableTechName(root, techName);
        var entry = template == null
            ? new XElement(techName.Trim(),
                new XAttribute(PortraitAttribute, ""),
                new XAttribute(IconAttribute, ""))
            : new XElement(template);
        entry.Name = techName.Trim();
        AppendDefinition(root, entry);
        return entry;
    }

    public static bool ReplaceDefinition(XDocument document, string originalTechName, XElement replacement)
    {
        var root = RequireModRoot(document);
        var current = FindDefinition(root, originalTechName);
        if (current == null) return false;
        EnsureAvailableTechName(root, replacement.Name.LocalName, current);
        current.ReplaceWith(new XElement(replacement));
        return true;
    }

    public static bool RenameDefinition(XDocument document, string oldTechName, string newTechName)
    {
        var root = RequireModRoot(document);
        var entry = FindDefinition(root, oldTechName);
        if (entry == null) return false;
        EnsureAvailableTechName(root, newTechName, entry);
        entry.Name = newTechName.Trim();
        return true;
    }

    public static bool DeleteDefinition(XDocument document, string techName)
    {
        var entry = FindDefinition(RequireModRoot(document), techName);
        if (entry == null) return false;
        RemoveElementAndAdjacentWhitespace(entry);
        return true;
    }

    public static XElement? FindDefinition(XContainer container, string techName)
        => container.Elements().FirstOrDefault(element =>
            element.Name.LocalName.Equals(techName.Trim(), StringComparison.OrdinalIgnoreCase));

    public static string ReadAttribute(XElement element, string attributeName)
        => element.Attributes().FirstOrDefault(attribute =>
            attribute.Name.LocalName.Equals(attributeName, StringComparison.OrdinalIgnoreCase))?.Value.Trim() ?? "";

    public static void SetAttribute(XElement element, string attributeName, string? value)
    {
        var existing = element.Attributes().FirstOrDefault(attribute =>
            attribute.Name.LocalName.Equals(attributeName, StringComparison.OrdinalIgnoreCase));
        if (existing != null) existing.Value = value?.Trim() ?? "";
        else element.Add(new XAttribute(attributeName, value?.Trim() ?? ""));
    }

    public static string GetDisplayName(string techName)
    {
        var trimmed = techName.Trim();
        var prefix = AgePrefixes.FirstOrDefault(candidate =>
            trimmed.StartsWith(candidate, StringComparison.OrdinalIgnoreCase));
        return prefix == null ? trimmed : trimmed[prefix.Length..];
    }

    public static string GetAgePrefix(string techName)
    {
        var trimmed = techName.Trim();
        return AgePrefixes.FirstOrDefault(candidate =>
            trimmed.StartsWith(candidate, StringComparison.OrdinalIgnoreCase)) ?? "ClassicalAge";
    }

    public static string BuildTechName(string name, string? sourceTechName = null)
        => GetAgePrefix(sourceTechName ?? "") + name.Trim();

    public static bool IsValidName(string? name) => InternalNamePolicy.IsValid(name);

    private static MinorGodDefinition CreateDefinition(XElement element, bool isBuiltIn)
    {
        var techName = element.Name.LocalName.Trim();
        return new MinorGodDefinition(GetDisplayName(techName), techName, isBuiltIn, new XElement(element));
    }

    private static IReadOnlyList<MinorGodDefinition> ReadDefinitions(string? path, string expectedRoot, bool isBuiltIn)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return [];
        var document = XDocument.Load(path, LoadOptions.PreserveWhitespace);
        if (document.Root == null || !document.Root.Name.LocalName.Equals(expectedRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Expected <{expectedRoot}> in '{path}'.");
        return ExtractDefinitions(document.Root, isBuiltIn);
    }

    private static XElement RequireModRoot(XDocument document)
    {
        if (document.Root == null || !document.Root.Name.LocalName.Equals(ModRootName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Expected <{ModRootName}>.");
        return document.Root;
    }

    private static void EnsureAvailableTechName(XElement root, string techName, XElement? ignored = null)
    {
        if (!IsValidName(techName))
            throw new ArgumentException("Minor god technology names can contain only letters, digits, '_' and '-'.", nameof(techName));
        if (root.Elements().Any(element => !ReferenceEquals(element, ignored) &&
            element.Name.LocalName.Equals(techName.Trim(), StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Minor god technology '{techName.Trim()}' already exists.");
    }

    private static void AppendDefinition(XElement root, XElement entry)
    {
        var trailingWhitespace = root.Nodes().LastOrDefault() as XText;
        if (trailingWhitespace != null && string.IsNullOrWhiteSpace(trailingWhitespace.Value))
            trailingWhitespace.AddBeforeSelf(new XText("\n\t"), entry);
        else
            root.Add(new XText("\n\t"), entry, new XText("\n"));
    }

    private static void RemoveElementAndAdjacentWhitespace(XElement element)
    {
        if (element.PreviousNode is XText whitespace && string.IsNullOrWhiteSpace(whitespace.Value))
            whitespace.Remove();
        element.Remove();
    }
}
