using System.Xml.Linq;

namespace AoMDivineDataEditor.Classes;

/// <summary>One reviewed physical move and its reference writes, using the shared file transaction.</summary>
public sealed class AssetMovePlan
{
    private readonly string _source;
    private readonly string _destination;
    private readonly HashSet<string> _referenceFiles;
    private readonly IReadOnlyDictionary<string, FileIntegrityTransaction.FileState> _expectedStates;
    private readonly IconMoveIdentity? _iconIdentity;
    public AssetReferenceMigrationPlan References { get; }

    public AssetMovePlan(string source, string destination, IEnumerable<TechnologyReferenceFile> files,
        AssetReferenceKind kind, string oldValue, string newValue)
        : this(source, destination, files, kind, oldValue, newValue, null) { }

    internal AssetMovePlan(IconMoveIdentity identity, IEnumerable<TechnologyReferenceFile> files)
        : this(identity.SourcePath, identity.Destination.AbsolutePath, files, AssetReferenceKind.Icon,
            identity.OldXmlValue, identity.Destination.XmlValue, identity) { }

    private AssetMovePlan(string source, string destination, IEnumerable<TechnologyReferenceFile> files,
        AssetReferenceKind kind, string oldValue, string newValue, IconMoveIdentity? iconIdentity)
    {
        _iconIdentity = iconIdentity;
        _source = Path.GetFullPath(source);
        _destination = Path.GetFullPath(destination);
        var states = FileIntegrityTransaction.CaptureStates(new[] { _source, _destination })
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase);
        if (!states[_source].Exists) throw new IOException("The source asset no longer exists.");
        if (states[_destination].Exists) throw new IOException("A file already exists at the selected destination.");
        var sources = files.ToList();
        _referenceFiles = sources.Select(file => Path.GetFullPath(file.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        References = iconIdentity == null
            ? AssetReferenceMigrationPlan.Prepare(sources, kind, oldValue, newValue)
            : AssetReferenceMigrationPlan.PrepareIconMove(sources, iconIdentity);
        if (References.Paths.Contains(_source, StringComparer.OrdinalIgnoreCase) ||
            References.Paths.Contains(_destination, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("The asset itself contains references requiring migration. This move needs manual review.");
        foreach (var entry in References.ExpectedStates)
        {
            if (states.TryGetValue(entry.Key, out var captured) && captured != entry.Value)
                throw new IOException("The asset changed during reference inspection. Retry the move.");
            states[entry.Key] = entry.Value;
        }
        if (iconIdentity != null)
        {
            foreach (var entry in iconIdentity.ExpectedStates)
            {
                if (states.TryGetValue(entry.Key, out var captured) && captured != entry.Value)
                    throw new IOException("The icon changed during move preparation. Retry after reopening the manager.");
                states[entry.Key] = entry.Value;
            }
        }
        _expectedStates = states;
    }

    public void Commit(IEnumerable<TechnologyReferenceFile> currentFiles, Action<XDocument, string> writer)
    {
        if (!_referenceFiles.SetEquals(currentFiles.Select(file => Path.GetFullPath(file.Path))))
            throw new IOException("The mod's XML/tactics file list changed after inspection. Retry the move to review its references.");
        FileIntegrityTransaction.Execute(References.Paths.Concat(new[] { _source, _destination }), _expectedStates, () =>
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_destination)!);
            _iconIdentity?.ValidateCounterparts();
            File.Move(_source, _destination, overwrite: false);
            References.Save(writer);
            _iconIdentity?.ValidateCounterparts();
        });
    }
}
