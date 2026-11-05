namespace AoMDivineDataEditor.Classes;

/// <summary>
/// Discovers XML and read-only compiled tactics reference sources across one mod. Unknown XML
/// and tactics files are deliberately included so a rename cannot silently
/// ignore a user-authored gameplay file merely because the editor does not own
/// its filename.
/// </summary>
public static class ModReferenceFileCatalog
{
    private const int MaximumReferenceFileCount = 20_000;

    /// <summary>
    /// Discovers once and retains that exact membership for later validation.
    /// Unlike optional catalog browsing, guarded operations require an accessible mod root.
    /// </summary>
    public static IReadOnlyList<TechnologyReferenceFile> Discover(
        string modRootDirectory, out FileDiscoverySnapshot snapshot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modRootDirectory);
        var root = Path.GetFullPath(modRootDirectory);
        var files = DiscoverRequiredRoot(root);
        snapshot = FileDiscoverySnapshot.Capture(
            files.Select(file => file.Path),
            () => DiscoverRequiredRoot(root).Select(file => file.Path));
        return files.AsReadOnly();
    }

    private static List<TechnologyReferenceFile> DiscoverRequiredRoot(string root)
    {
        RequireDirectory(root);
        var files = Discover(root).ToList();
        // The existing optional discovery API returns an empty set for a missing root.
        // Reject a root that disappeared while this guarded discovery was running.
        RequireDirectory(root);
        return files;
    }

    private static void RequireDirectory(string root)
    {
        if ((File.GetAttributes(root) & FileAttributes.Directory) == 0)
            throw new IOException($"The mod reference root '{root}' is not a directory. No files were written.");
    }

    public static IReadOnlyList<TechnologyReferenceFile> Discover(string? modRootDirectory)
    {
        if (string.IsNullOrWhiteSpace(modRootDirectory) || !Directory.Exists(modRootDirectory))
            return [];

        var files = new List<TechnologyReferenceFile>();
        foreach (var path in ContainedFileEnumeration.EnumerateFiles(modRootDirectory))
        {
            var extension = Path.GetExtension(path);
            if (!extension.Equals(".xml", StringComparison.OrdinalIgnoreCase) &&
                !extension.Equals(".tactics", StringComparison.OrdinalIgnoreCase) &&
                !ModReferenceDocumentReader.IsCompiledTactics(path))
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
                    $"Reference discovery found more than {MaximumReferenceFileCount:N0} XML/tactics reference files. " +
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
        if (Path.GetExtension(path).Equals(".tactics", StringComparison.OrdinalIgnoreCase) ||
            ModReferenceDocumentReader.IsCompiledTactics(path))
            return TechnologyReferenceDocumentKind.Tactics;
        return TechnologyReferenceDocumentKind.Other;
    }
}
