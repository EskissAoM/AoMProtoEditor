using System.Xml.Linq;

namespace AoMDivineDataEditor.Windows;

public partial class ProtoEditorWindow
{
    // This describes this window's already loaded base data, not another cache.
    // Use the existing catalog loaders separately so their own source keys stay authoritative.
    private sealed record TacticsBaseSource(string Path, long Length, long WriteTicks, long CreationTicks);
    private TacticsBaseSource? _loadedTacticsBaseSource;

    private static TacticsBaseSource? CaptureTacticsBaseSource(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            var file = new FileInfo(path);
            return file.Exists
                ? new TacticsBaseSource(file.FullName, file.Length, file.LastWriteTimeUtc.Ticks, file.CreationTimeUtc.Ticks)
                : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null; // A failed freshness check selects the existing loader.
        }
    }

    private bool TryReuseTacticsBaseData(ProtoEditorWindow owner)
    {
        var source = owner._loadedTacticsBaseSource;
        if (ReferenceEquals(owner, this) || source == null || owner._barData == null ||
            owner._barXmlRoot == null || owner._protoDataBarFile?.IsLoaded != true ||
            !string.Equals(source.Path, owner._protoDataBarPath, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(source.Path, ResolveDataBarPath(), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(source.Path, owner.ResolveDataBarPath(), StringComparison.OrdinalIgnoreCase) ||
            source != CaptureTacticsBaseSource(source.Path)) return false;

        // XML and mutable collection data belong to each editor. Only the loaded,
        // read-only archive entry index is shared; its readers open their own streams.
        var data = owner._barData.Clone();
        var root = new XElement(owner._barXmlRoot);
        if (source != CaptureTacticsBaseSource(source.Path)) return false;
        _barData = data;
        _barXmlRoot = root;
        _protoDataBarFile = owner._protoDataBarFile;
        _protoDataBarPath = source.Path;
        _loadedTacticsBaseSource = source;
        InvalidateBarDerivedCaches();
        _barProtoActionAnimationNames = [.. owner._barProtoActionAnimationNames];
        _barProtoActionAttachmentPaths = [.. owner._barProtoActionAttachmentPaths];
        _barProtoActionModelAttachmentBones = [.. owner._barProtoActionModelAttachmentBones];
        _barProtoActionSuggestionDataLoaded = owner._barProtoActionSuggestionDataLoaded;
        RefreshProtoActionMetadata(new Dictionary<string, string>(owner._globalTacticsActionTypeMap, StringComparer.OrdinalIgnoreCase));
        return true;
    }

    private async Task<bool> LoadTacticsEditorGameDataAsync(ProtoEditorWindow? owner)
    {
        if (owner != null && TryReuseTacticsBaseData(owner))
        {
            var source = _loadedTacticsBaseSource!;
            // These APIs already cache by source metadata. Retain their normal
            // checks for changed UI/Sound archives and Art archive membership.
            await LoadBaseGameIconCatalogAsync(source.Path);
            await LoadBaseGameAnimFileCatalogAsync(source.Path);
            await LoadBaseGameSoundSetCatalogAsync(source.Path);
            if (source == CaptureTacticsBaseSource(source.Path) &&
                string.Equals(source.Path, ResolveDataBarPath(), StringComparison.OrdinalIgnoreCase)) return true;
        }
        await LoadProtoDataFromBar();
        return false;
    }
}
