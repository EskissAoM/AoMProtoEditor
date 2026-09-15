using System.Xml.Linq;

namespace AoMDivineDataEditor.Classes;

/// <summary>
/// Authoritative save-boundary checks shared by the god-related editors. UI
/// autocomplete improves input, but it is never the final integrity boundary.
/// </summary>
internal static class GodModuleFieldValidation
{
    internal static void ValidateMajorGodSharedFields(
        XElement definition,
        IEnumerable<string> iconPaths,
        IEnumerable<string> minimapBorderValues,
        IEnumerable<string> backgroundScenarioValues)
    {
        ArgumentNullException.ThrowIfNull(definition);
        foreach (var (tag, label) in new[]
                 {
                     ("displaynameid", "Display Name"),
                     ("rollovernameid", "Rollover"),
                     ("titleid", "Title")
                 })
        {
            if (ReadDirectText(definition, tag).Length == 0)
                throw new InvalidDataException($"{label} string ID is required.");
        }

        foreach (var (tag, label) in new[]
                 {
                     ("icon", "Icon"),
                     ("portrait", "Portrait"),
                     ("breakoutportrait", "Breakout Portrait")
                 })
        {
            ValidateCatalogAsset(ReadDirectText(definition, tag), iconPaths, label);
        }

        ValidateCatalogText(
            ReadDirectText(definition, "minimapborderimage"),
            minimapBorderValues,
            "Minimap Border Image");
        ValidateCatalogText(
            ReadDirectText(definition, "backgroundscenario"),
            backgroundScenarioValues,
            "Background Scenario");

        if (FindDirect(definition, "partisans") is { } partisans && partisans.Value.Trim() != "1")
            throw new InvalidDataException("Has Partisans must contain 1 when present.");
        if (FindDirect(definition, "devotioncommands") is { } devotionCommands &&
            !string.IsNullOrWhiteSpace(devotionCommands.Value))
            throw new InvalidDataException("Has Devotion Commands must be empty when present.");
    }

    internal static void ValidateCultureDefinition(XElement definition, string unchangedName)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!definition.Name.LocalName.Equals(CultureCatalog.EntryName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("A Culture definition must use a <culture> element.");

        var name = CultureCatalog.ReadField(definition, "name");
        if (name.Length == 0)
            throw new InvalidDataException("Culture Name is required.");
        if (!InternalNamePolicy.IsValidOrUnchangedLegacy(name, unchangedName))
            throw new InvalidDataException($"Culture Name can contain only {InternalNamePolicy.AllowedCharactersDescription}.");
        if (CultureCatalog.ReadField(definition, "displaynameid").Length == 0)
            throw new InvalidDataException("Culture Display name string ID is required.");
    }

    internal static void ValidateMinorGodDefinition(
        XElement definition,
        IEnumerable<string> ageUpgradeTechnologies,
        IEnumerable<string> iconPaths)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var techName = definition.Name.LocalName.Trim();
        if (!InternalNamePolicy.IsValid(techName) || MinorGodCatalog.GetDisplayName(techName).Length == 0)
            throw new InvalidDataException("Minor god Tech must have a valid, non-empty minor-god name after its age prefix.");
        if (!ageUpgradeTechnologies.Contains(techName, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException($"Technology '{techName}' is not an existing AgeUpgrade technology.");

        ValidateCatalogAsset(
            MinorGodCatalog.ReadAttribute(definition, MinorGodCatalog.PortraitAttribute),
            iconPaths,
            "Portrait");
        ValidateCatalogAsset(
            MinorGodCatalog.ReadAttribute(definition, MinorGodCatalog.IconAttribute),
            iconPaths,
            "Icon");
    }

    internal static void ValidateCatalogAsset(string raw, IEnumerable<string> catalogPaths, string label)
    {
        var value = NormalizePath(raw);
        if (value.Length == 0)
            throw new InvalidDataException($"{label} is required.");

        var catalog = catalogPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => NormalizeAssetPath(path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (catalog.Count == 0)
            return;

        if (!catalog.Contains(NormalizeAssetPath(value)))
            throw new InvalidDataException($"{label} must reference a valid icon from the asset list.");
    }

    private static void ValidateCatalogText(string raw, IEnumerable<string> catalogValues, string label)
    {
        var value = raw.Trim();
        if (value.Length == 0)
            throw new InvalidDataException($"{label} is required.");
        var catalog = catalogValues
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Select(candidate => candidate.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (catalog.Count > 0 && !catalog.Contains(value))
            throw new InvalidDataException($"{label} must reference an existing major-god value.");
    }

    private static XElement? FindDirect(XElement owner, string name)
        => owner.Elements().FirstOrDefault(element =>
            element.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static string ReadDirectText(XElement owner, string name)
        => FindDirect(owner, name)?.Value.Trim() ?? "";

    private static string NormalizePath(string? value)
        => value?.Trim().Replace('/', '\\') ?? "";

    private static string NormalizeAssetPath(string? value)
    {
        var normalized = NormalizePath(value);
        return normalized.EndsWith(".dds", StringComparison.OrdinalIgnoreCase)
            ? normalized[..^4] + ".png"
            : normalized;
    }
}
