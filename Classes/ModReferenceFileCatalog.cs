namespace AoMDivineDataEditor.Classes;

/// <summary>
/// Discovers editable, XML-based reference sources across one mod. Unknown XML
/// and tactics files are deliberately included so a rename cannot silently
/// ignore a user-authored gameplay file merely because the editor does not own
/// its filename.
/// </summary>
public static class ModReferenceFileCatalog
{
    private const int MaximumReferenceFileCount = 20_000;

    public static IReadOnlyList<TechnologyReferenceFile> Discover(string? modRootDirectory)
    {
        if (string.IsNullOrWhiteSpace(modRootDirectory) || !Directory.Exists(modRootDirectory))
            return [];

        var files = new List<TechnologyReferenceFile>();
        foreach (var path in ContainedFileEnumeration.EnumerateFiles(modRootDirectory))
        {
            var extension = Path.GetExtension(path);
            if (!extension.Equals(".xml", StringComparison.OrdinalIgnoreCase) &&
                !extension.Equals(".tactics", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var fullPath = Path.GetFullPath(path);
            files.Add(new TechnologyReferenceFile(
                fullPath,
                InferDocumentKind(fullPath),
                Path.GetRelativePath(modRootDirectory, fullPath)));
            if (files.Count > MaximumReferenceFileCount)
            {
                throw new InvalidOperationException(
                    $"Reference discovery found more than {MaximumReferenceFileCount:N0} editable XML/tactics files. " +
                    "The operation was cancelled to keep memory and scan time bounded.");
            }
        }

        return files
            .OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static TechnologyReferenceDocumentKind InferDocumentKind(string path)
    {
        var fileName = Path.GetFileName(path);
        if (fileName.Equals("techtree_mods.xml", StringComparison.OrdinalIgnoreCase))
            return TechnologyReferenceDocumentKind.Techtree;
        if (fileName.Equals("proto_mods.xml", StringComparison.OrdinalIgnoreCase))
            return TechnologyReferenceDocumentKind.ProtoUnits;
        if (fileName.Equals("proto_unit_command_mods.xml", StringComparison.OrdinalIgnoreCase))
            return TechnologyReferenceDocumentKind.ProtoUnitCommands;
        if (fileName.Equals("abilities_mods.xml", StringComparison.OrdinalIgnoreCase))
            return TechnologyReferenceDocumentKind.Abilities;
        if (fileName.Equals("unit_transform_mods.xml", StringComparison.OrdinalIgnoreCase))
            return TechnologyReferenceDocumentKind.UnitTransforms;
        if (fileName.Equals("powers_mods.xml", StringComparison.OrdinalIgnoreCase))
            return TechnologyReferenceDocumentKind.Powers;
        if (fileName.Equals(MajorGodCatalog.ModFileName, StringComparison.OrdinalIgnoreCase))
            return TechnologyReferenceDocumentKind.MajorGods;
        if (fileName.Equals(MinorGodCatalog.ModFileName, StringComparison.OrdinalIgnoreCase))
            return TechnologyReferenceDocumentKind.MinorGods;
        if (Path.GetExtension(path).Equals(".tactics", StringComparison.OrdinalIgnoreCase))
            return TechnologyReferenceDocumentKind.Tactics;
        return TechnologyReferenceDocumentKind.Other;
    }
}
