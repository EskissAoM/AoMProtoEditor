using System.Xml.Linq;
using AoMDivineDataEditor.GameData;

namespace AoMDivineDataEditor.Classes;

public sealed record UnitTypeDefinition(string Name, bool IsBuiltIn, XElement SourceElement);

public static class UnitTypeCatalog
{
    public const string BaseRootName = "abstractunittypes";
    public const string ModRootName = "abstractunittypesmods";
    public const string EntryName = "abstractunittype";

    public static IReadOnlyList<UnitTypeDefinition> Merge(
        IEnumerable<UnitTypeDefinition> baseDefinitions,
        IEnumerable<UnitTypeDefinition> modDefinitions)
    {
        var result = new List<UnitTypeDefinition>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in baseDefinitions.Concat(modDefinitions))
        {
            if (string.IsNullOrWhiteSpace(definition.Name) || !names.Add(definition.Name.Trim()))
                continue;
            result.Add(definition with { Name = definition.Name.Trim(), SourceElement = new XElement(definition.SourceElement) });
        }

        return result.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static IReadOnlyList<UnitTypeDefinition> ReadBaseFile(string? path)
        => ReadFile(path, BaseRootName, isBuiltIn: true);

    public static IReadOnlyList<UnitTypeDefinition> ReadModFile(string? path)
        => ReadFile(path, ModRootName, isBuiltIn: false);

    public static IReadOnlyList<UnitTypeDefinition> ExtractDefinitions(XContainer container, bool isBuiltIn)
        => container.Descendants()
            .Where(element => element.Name.LocalName.Equals(EntryName, StringComparison.OrdinalIgnoreCase))
            .Select(element => new UnitTypeDefinition(element.Value.Trim(), isBuiltIn, new XElement(element)))
            .Where(item => item.Name.Length > 0)
            .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static IReadOnlyList<UnitTypeDefinition> ExtractUsedDefinitionsFromProto(XContainer container)
        => container.Descendants()
            .Where(element => element.Name.LocalName.Equals("unit", StringComparison.OrdinalIgnoreCase))
            .SelectMany(unit => unit.Elements().Where(element =>
                element.Name.LocalName.Equals("unittype", StringComparison.OrdinalIgnoreCase)))
            .Select(element => element.Value.Trim())
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(name => new UnitTypeDefinition(name, true, new XElement(EntryName, name)))
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static IReadOnlyList<UnitTypeDefinition> ExtractBaseDefinitionsFromBar(BarArchive? barFile, string? barPath)
    {
        if (barFile?.Entries == null || string.IsNullOrWhiteSpace(barPath) || !File.Exists(barPath))
            return [];

        var definitions = new Dictionary<string, UnitTypeDefinition>(StringComparer.OrdinalIgnoreCase);
        var entries = barFile.Entries.Where(entry =>
        {
            var normalized = entry.Name.Replace('\\', '/');
            var fileName = normalized.Split('/').LastOrDefault() ?? "";
            return fileName.Equals("abstract_unit_types.xml.xmb", StringComparison.OrdinalIgnoreCase) ||
                   fileName.Equals("abstract_unit_types.xmb", StringComparison.OrdinalIgnoreCase);
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
                if (document.Root == null || !document.Root.Name.LocalName.Equals(BaseRootName, StringComparison.OrdinalIgnoreCase))
                    continue;

                foreach (var definition in ExtractDefinitions(document, isBuiltIn: true))
                    definitions.TryAdd(definition.Name, definition);
            }
            catch
            {
                // A malformed matching BAR entry must not hide definitions loaded from another entry/fallback.
            }
        }

        return definitions.Values.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static XDocument LoadOrCreateModDocument(string path)
    {
        if (!File.Exists(path))
            return new XDocument(new XElement(ModRootName));

        var document = XDocument.Load(path, LoadOptions.PreserveWhitespace);
        if (document.Root == null || !document.Root.Name.LocalName.Equals(ModRootName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Expected <{ModRootName}> in '{path}'.");
        return document;
    }

    public static XElement AddDefinition(XDocument document, string name, XElement? template = null)
    {
        var root = RequireModRoot(document);
        EnsureAvailableName(root, name);
        var entry = template == null ? new XElement(EntryName) : new XElement(template);
        entry.Name = EntryName;
        entry.RemoveNodes();
        entry.Value = name.Trim();
        root.Add(entry);
        return entry;
    }

    public static bool RenameDefinition(XDocument document, string oldName, string newName)
    {
        var root = RequireModRoot(document);
        var entry = FindDefinition(root, oldName);
        if (entry == null)
            return false;
        EnsureAvailableName(root, newName, entry);
        entry.Value = newName.Trim();
        return true;
    }

    public static bool DeleteDefinition(XDocument document, string name)
    {
        var entry = FindDefinition(RequireModRoot(document), name);
        if (entry == null)
            return false;
        entry.Remove();
        return true;
    }

    public static int RemoveUnitAssignments(XDocument protoDocument, string name)
    {
        var assignments = GetUnitAssignments(protoDocument, name).ToList();
        foreach (var assignment in assignments)
            assignment.Remove();
        return assignments.Count;
    }

    public static int RenameUnitAssignments(XDocument protoDocument, string oldName, string newName)
    {
        var assignments = GetUnitAssignments(protoDocument, oldName).ToList();
        foreach (var assignment in assignments)
            assignment.Value = newName.Trim();
        return assignments.Count;
    }

    public static bool IsValidName(string? name)
        => InternalNamePolicy.IsValid(name);

    private static IReadOnlyList<UnitTypeDefinition> ReadFile(string? path, string expectedRoot, bool isBuiltIn)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return [];
        try
        {
            var document = XDocument.Load(path, LoadOptions.PreserveWhitespace);
            if (document.Root == null || !document.Root.Name.LocalName.Equals(expectedRoot, StringComparison.OrdinalIgnoreCase))
                return [];
            return ExtractDefinitions(document, isBuiltIn);
        }
        catch
        {
            return [];
        }
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

    private static IEnumerable<XElement> GetUnitAssignments(XDocument protoDocument, string name)
        => protoDocument.Root?.Elements()
               .Where(element => element.Name.LocalName.Equals("unit", StringComparison.OrdinalIgnoreCase))
               .SelectMany(unit => unit.Elements().Where(element =>
                   element.Name.LocalName.Equals("unittype", StringComparison.OrdinalIgnoreCase) &&
                   element.Value.Trim().Equals(name.Trim(), StringComparison.OrdinalIgnoreCase)))
           ?? [];

    private static void EnsureAvailableName(XElement root, string name, XElement? ignored = null)
    {
        if (!IsValidName(name))
            throw new ArgumentException("Unit Type names can contain only letters, digits, '_' and '-'.", nameof(name));
        if (root.Elements().Any(element => !ReferenceEquals(element, ignored) &&
            element.Name.LocalName.Equals(EntryName, StringComparison.OrdinalIgnoreCase) &&
            element.Value.Trim().Equals(name.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Unit Type '{name.Trim()}' already exists.");
        }
    }
}
