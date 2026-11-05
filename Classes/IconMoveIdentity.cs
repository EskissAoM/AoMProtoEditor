namespace AoMDivineDataEditor.Classes;

/// <summary>Validated physical identity and read dependencies for one logical icon move.</summary>
internal sealed class IconMoveIdentity
{
    public string SourcePath { get; }
    public string OldXmlValue { get; }
    public AssetDestination Destination { get; }
    public IconFileFormat Format { get; }
    public IReadOnlyDictionary<string, FileIntegrityTransaction.FileState> ExpectedStates { get; }
    private readonly IReadOnlyDictionary<string, FileIntegrityTransaction.FileState> _counterparts;

    private IconMoveIdentity(string source, string oldXmlValue, AssetDestination destination, IconFileFormat format,
        IReadOnlyDictionary<string, FileIntegrityTransaction.FileState> states, IEnumerable<string> counterparts)
    {
        SourcePath = source;
        OldXmlValue = oldXmlValue;
        Destination = destination;
        Format = format;
        ExpectedStates = states;
        _counterparts = counterparts.Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(path => path, path => states[path], StringComparer.OrdinalIgnoreCase);
    }

    public static IconMoveIdentity Prepare(string resourcesDirectory, IconAssetSource retainedSource, AssetDestination logicalDestination)
    {
        var retained = retainedSource.PhysicalSources.Where(source => source.IsCustom).ToList();
        if (retained.Count == 0)
            throw new InvalidOperationException("Original archive icons cannot be moved. Select a custom icon.");
        if (retained.Count != 1)
            throw new InvalidOperationException("Both PNG and DDS files supply this custom icon. Rename/move is blocked until the competing files are resolved.");
        var source = retained[0];
        if (source.Format is not (IconFileFormat.Png or IconFileFormat.Dds))
            throw new InvalidOperationException("Only physical PNG or DDS icon files can be moved.");
        var current = IconSourceResolver.ResolveCustom(resourcesDirectory, retainedSource.LogicalPath);
        if (current == null || current.PhysicalSources.Count != 1 ||
            !current.PreferredSource!.Path.Equals(source.Path, StringComparison.OrdinalIgnoreCase) ||
            current.PreferredSource.Format != source.Format)
            throw new IOException("The icon's physical source changed after the manager loaded. Reopen the manager before moving it.");

        var relative = IconCatalog.ToResourceRelativePath(logicalDestination.XmlValue);
        if (!Path.GetExtension(relative).Equals(".png", StringComparison.OrdinalIgnoreCase) ||
            !AssetDestinationPolicy.TryResolve(resourcesDirectory, Path.GetDirectoryName(relative) ?? "",
                Path.GetFileNameWithoutExtension(relative), ".png", "resources", out var resolved, out _) ||
            resolved == null || !resolved.AbsolutePath.Equals(Path.GetFullPath(logicalDestination.AbsolutePath), StringComparison.OrdinalIgnoreCase))
            throw new IOException("The logical icon destination must be a PNG path inside the mod resources folder.");

        var extension = source.Format == IconFileFormat.Dds ? ".dds" : ".png";
        var destination = new AssetDestination(Path.ChangeExtension(resolved.AbsolutePath, extension),
            Path.ChangeExtension(resolved.RelativePath, extension), resolved.XmlValue);
        var sourceCounterpart = Path.ChangeExtension(source.Path, source.Format == IconFileFormat.Dds ? ".png" : ".dds");
        var destinationCounterpart = Path.ChangeExtension(destination.AbsolutePath, source.Format == IconFileFormat.Dds ? ".png" : ".dds");
        var states = FileIntegrityTransaction.CaptureStates([source.Path, destination.AbsolutePath, sourceCounterpart, destinationCounterpart]);
        if (!states[source.Path].Exists)
            throw new IOException("The source icon no longer exists.");
        if (states[sourceCounterpart].Exists)
            throw new IOException("A PNG/DDS counterpart appeared for the source icon. Reopen the manager before moving it.");
        if (states[destination.AbsolutePath].Exists || states[destinationCounterpart].Exists)
            throw new IOException("The destination already has a PNG or DDS icon. No files were overwritten.");
        return new IconMoveIdentity(source.Path, retainedSource.LogicalPath, destination, source.Format, states,
            [sourceCounterpart, destinationCounterpart]);
    }

    // Counterparts are read dependencies, never rollback/write targets. Check again
    // after reference writes so a late contender causes rollback without deleting it.
    internal void ValidateCounterparts()
        => FileIntegrityTransaction.Execute([], _counterparts, () => { });
}
