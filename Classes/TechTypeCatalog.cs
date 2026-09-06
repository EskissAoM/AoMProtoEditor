using System.Xml.Linq;
using AoMDivineDataEditor.GameData;

namespace AoMDivineDataEditor.Classes;

public sealed record TechTypeDefinition(string Name, bool IsBuiltIn, XElement SourceElement);

public readonly record struct TechTypeUsage(int PropertyUsageCount, int EffectUsageCount)
{
    public int TotalCount => PropertyUsageCount + EffectUsageCount;

    public static TechTypeUsage operator +(TechTypeUsage left, TechTypeUsage right)
        => new(
            left.PropertyUsageCount + right.PropertyUsageCount,
            left.EffectUsageCount + right.EffectUsageCount);
}

public static class TechTypeCatalog
{
    public const string BaseRootName = "techtypes";
    public const string ModRootName = "techtypesmods";
    public const string EntryName = "techtype";

    public static IReadOnlyList<TechTypeDefinition> Merge(
        IEnumerable<TechTypeDefinition> baseDefinitions,
        IEnumerable<TechTypeDefinition> modDefinitions)
    {
        var result = new List<TechTypeDefinition>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in baseDefinitions.Concat(modDefinitions))
        {
            if (string.IsNullOrWhiteSpace(definition.Name) || !names.Add(definition.Name.Trim()))
                continue;
            result.Add(definition with { Name = definition.Name.Trim(), SourceElement = new XElement(definition.SourceElement) });
        }

        return result.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static IReadOnlyList<TechTypeDefinition> ReadBaseFile(string? path)
        => ReadFile(path, BaseRootName, isBuiltIn: true);

    public static IReadOnlyList<TechTypeDefinition> ReadModFile(string? path)
        => ReadFile(path, ModRootName, isBuiltIn: false);

    public static IReadOnlyList<TechTypeDefinition> ExtractDefinitions(XContainer container, bool isBuiltIn)
        => container.Descendants()
            .Where(element => element.Name.LocalName.Equals(EntryName, StringComparison.OrdinalIgnoreCase))
            .Select(element => new TechTypeDefinition(element.Value.Trim(), isBuiltIn, new XElement(element)))
            .Where(item => item.Name.Length > 0)
            .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static IReadOnlyList<TechTypeDefinition> ExtractBaseDefinitionsFromBar(BarArchive? barFile, string? barPath)
    {
        if (barFile?.Entries == null || string.IsNullOrWhiteSpace(barPath) || !File.Exists(barPath))
            return [];

        var definitions = new Dictionary<string, TechTypeDefinition>(StringComparer.OrdinalIgnoreCase);
        var entries = barFile.Entries.Where(entry =>
        {
            var fileName = entry.Name.Replace('\\', '/').Split('/').LastOrDefault() ?? "";
            return fileName.Equals("tech_types.xml.xmb", StringComparison.OrdinalIgnoreCase) ||
                   fileName.Equals("tech_types.xmb", StringComparison.OrdinalIgnoreCase);
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
                if (document.Root == null || !document.Root.Name.LocalName.Equals(BaseRootName, StringComparison.OrdinalIgnoreCase))
                    continue;
                foreach (var definition in ExtractDefinitions(document, isBuiltIn: true))
                    definitions.TryAdd(definition.Name, definition);
            }
            catch
            {
                // Keep definitions loaded from the remaining matching entries.
            }
        }

        return definitions.Values.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static XDocument LoadOrCreateModDocument(string path)
    {
        if (!File.Exists(path)) return new XDocument(new XElement(ModRootName));
        var document = XDocument.Load(path, LoadOptions.PreserveWhitespace);
        if (document.Root == null || !document.Root.Name.LocalName.Equals(ModRootName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Expected <{ModRootName}> in '{path}'.");
        return document;
    }

    public static XElement AddDefinition(XDocument document, string name)
    {
        var root = RequireModRoot(document);
        EnsureAvailableName(root, name);
        var entry = new XElement(EntryName, name.Trim());
        root.Add(entry);
        return entry;
    }

    public static bool RenameDefinition(XDocument document, string oldName, string newName)
    {
        var root = RequireModRoot(document);
        var entry = FindDefinition(root, oldName);
        if (entry == null) return false;
        EnsureAvailableName(root, newName, entry);
        entry.Value = newName.Trim();
        return true;
    }

    public static bool DeleteDefinition(XDocument document, string name)
    {
        var entry = FindDefinition(RequireModRoot(document), name);
        if (entry == null) return false;
        entry.Remove();
        return true;
    }

    public static int RenameTechnologyAssignments(XDocument document, string oldName, string newName)
    {
        var assignments = GetTechnologyAssignments(document, oldName).ToList();
        foreach (var assignment in assignments) assignment.Value = newName.Trim();
        return assignments.Count;
    }

    public static int RenameTechnologyEffectReferences(XDocument document, string oldName, string newName)
    {
        var renamedEffects = 0;
        foreach (var effect in GetTechnologyEffects(document))
        {
            var changed = false;
            foreach (var attribute in effect.Attributes().Where(attribute =>
                         attribute.Name.LocalName.Equals("techtype", StringComparison.OrdinalIgnoreCase) &&
                         attribute.Value.Trim().Equals(oldName.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                attribute.Value = newName.Trim();
                changed = true;
            }

            foreach (var target in effect.Elements().Where(element =>
                         element.Name.LocalName.Equals("target", StringComparison.OrdinalIgnoreCase)))
            {
                var type = target.Attributes().FirstOrDefault(attribute =>
                    attribute.Name.LocalName.Equals("type", StringComparison.OrdinalIgnoreCase))?.Value.Trim();
                if (string.Equals(type, "TechType", StringComparison.OrdinalIgnoreCase) &&
                    target.Value.Trim().Equals(oldName.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    target.Value = newName.Trim();
                    changed = true;
                }

                var exclusions = target.Attributes().FirstOrDefault(attribute =>
                    attribute.Name.LocalName.Equals("excludetypes", StringComparison.OrdinalIgnoreCase));
                if (exclusions != null && ReplacePipeSeparatedValue(exclusions, oldName, newName))
                    changed = true;
            }

            foreach (var element in effect.Descendants().Where(element =>
                         element.Name.LocalName.Equals(EntryName, StringComparison.OrdinalIgnoreCase) &&
                         element.Value.Trim().Equals(oldName.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                element.Value = newName.Trim();
                changed = true;
            }

            if (changed)
                renamedEffects++;
        }
        return renamedEffects;
    }

    public static int RemoveTechnologyAssignments(XDocument document, string name)
    {
        var assignments = GetTechnologyAssignments(document, name).ToList();
        foreach (var assignment in assignments) assignment.Remove();
        return assignments.Count;
    }

    public static int CountTechnologyUsage(XContainer container, string name)
        => container.Descendants()
            .Where(element => element.Name.LocalName.Equals("tech", StringComparison.OrdinalIgnoreCase))
            .Count(technology => technology.Elements().Any(element =>
                element.Name.LocalName.Equals(EntryName, StringComparison.OrdinalIgnoreCase) &&
                element.Value.Trim().Equals(name.Trim(), StringComparison.OrdinalIgnoreCase)));

    public static TechTypeUsage GetTechnologyUsage(XContainer container, string name)
        => new(
            CountTechnologyUsage(container, name),
            GetTechnologyEffects(container).Count(effect => EffectReferencesTechType(effect, name)));

    public static bool IsValidName(string? name) => InternalNamePolicy.IsValid(name);

    private static IReadOnlyList<TechTypeDefinition> ReadFile(string? path, string expectedRoot, bool isBuiltIn)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return [];
        try
        {
            var document = XDocument.Load(path, LoadOptions.PreserveWhitespace);
            if (document.Root == null || !document.Root.Name.LocalName.Equals(expectedRoot, StringComparison.OrdinalIgnoreCase))
                return [];
            return ExtractDefinitions(document, isBuiltIn);
        }
        catch { return []; }
    }

    private static XElement RequireModRoot(XDocument document)
    {
        if (document.Root == null || !document.Root.Name.LocalName.Equals(ModRootName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Expected <{ModRootName}>.");
        return document.Root;
    }

    private static XElement? FindDefinition(XElement root, string name)
        => root.Elements().FirstOrDefault(element =>
            element.Name.LocalName.Equals(EntryName, StringComparison.OrdinalIgnoreCase) &&
            element.Value.Trim().Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<XElement> GetTechnologyAssignments(XContainer container, string name)
        => container.Descendants()
            .Where(element => element.Name.LocalName.Equals("tech", StringComparison.OrdinalIgnoreCase))
            .SelectMany(technology => technology.Elements().Where(element =>
                element.Name.LocalName.Equals(EntryName, StringComparison.OrdinalIgnoreCase) &&
                element.Value.Trim().Equals(name.Trim(), StringComparison.OrdinalIgnoreCase)));

    private static IEnumerable<XElement> GetTechnologyEffects(XContainer container)
        => container.Descendants()
            .Where(element => element.Name.LocalName.Equals("tech", StringComparison.OrdinalIgnoreCase))
            .SelectMany(technology => technology.Descendants().Where(element =>
                element.Name.LocalName.Equals("effect", StringComparison.OrdinalIgnoreCase)));

    private static bool EffectReferencesTechType(XElement effect, string name)
    {
        if (effect.Attributes().Any(attribute =>
                attribute.Name.LocalName.Equals("techtype", StringComparison.OrdinalIgnoreCase) &&
                attribute.Value.Trim().Equals(name.Trim(), StringComparison.OrdinalIgnoreCase)))
            return true;

        if (effect.Descendants().Any(element =>
                element.Name.LocalName.Equals(EntryName, StringComparison.OrdinalIgnoreCase) &&
                element.Value.Trim().Equals(name.Trim(), StringComparison.OrdinalIgnoreCase)))
            return true;

        foreach (var target in effect.Elements().Where(element =>
                     element.Name.LocalName.Equals("target", StringComparison.OrdinalIgnoreCase)))
        {
            var type = target.Attributes().FirstOrDefault(attribute =>
                attribute.Name.LocalName.Equals("type", StringComparison.OrdinalIgnoreCase))?.Value.Trim();
            if (string.Equals(type, "TechType", StringComparison.OrdinalIgnoreCase) &&
                target.Value.Trim().Equals(name.Trim(), StringComparison.OrdinalIgnoreCase))
                return true;

            var exclusions = target.Attributes().FirstOrDefault(attribute =>
                attribute.Name.LocalName.Equals("excludetypes", StringComparison.OrdinalIgnoreCase))?.Value;
            if (ContainsPipeSeparatedValue(exclusions, name))
                return true;
        }
        return false;
    }

    private static bool ContainsPipeSeparatedValue(string? values, string name)
        => (values ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(value => value.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));

    private static bool ReplacePipeSeparatedValue(XAttribute attribute, string oldName, string newName)
    {
        var values = attribute.Value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var changed = false;
        for (var index = 0; index < values.Length; index++)
        {
            if (!values[index].Equals(oldName.Trim(), StringComparison.OrdinalIgnoreCase))
                continue;
            values[index] = newName.Trim();
            changed = true;
        }
        if (changed)
            attribute.Value = string.Join('|', values);
        return changed;
    }

    private static void EnsureAvailableName(XElement root, string name, XElement? ignored = null)
    {
        if (!IsValidName(name))
            throw new ArgumentException("Tech Type names can contain only letters, digits, '_' and '-'.", nameof(name));
        if (root.Elements().Any(element => !ReferenceEquals(element, ignored) &&
            element.Name.LocalName.Equals(EntryName, StringComparison.OrdinalIgnoreCase) &&
            element.Value.Trim().Equals(name.Trim(), StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Tech Type '{name.Trim()}' already exists.");
    }
}
