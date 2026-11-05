using System.Xml.Linq;

namespace AoMDivineDataEditor.Classes;

public enum AssetReferenceKind { Icon, Animation, SoundSet }

/// <summary>Prepares asset reference changes only; the caller owns the move transaction and editor adoption.</summary>
public sealed class AssetReferenceMigrationPlan
{
    private readonly Dictionary<string, XDocument> _documents = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FileIntegrityTransaction.FileState> _expectedStates = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<string> Paths => _documents.Keys.ToList();
    public IReadOnlyDictionary<string, FileIntegrityTransaction.FileState> ExpectedStates => _expectedStates;
    public int ReferenceCount { get; private set; }

    public static AssetReferenceMigrationPlan Prepare(
        IEnumerable<TechnologyReferenceFile> files, AssetReferenceKind kind, string oldValue, string newValue)
        => PrepareCore(files, kind, oldValue, newValue, allowDdsIconReferences: false);

    internal static AssetReferenceMigrationPlan PrepareIconMove(IEnumerable<TechnologyReferenceFile> files, IconMoveIdentity identity)
        => PrepareCore(files, AssetReferenceKind.Icon, identity.OldXmlValue, identity.Destination.XmlValue,
            allowDdsIconReferences: identity.Format == IconFileFormat.Dds);

    internal static AssetReferenceMigrationPlan PrepareCore(
        IEnumerable<TechnologyReferenceFile> files, AssetReferenceKind kind, string oldValue, string newValue, bool allowDdsIconReferences,
        ReferenceReadBudget? budget = null)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentException.ThrowIfNullOrWhiteSpace(oldValue);
        ArgumentException.ThrowIfNullOrWhiteSpace(newValue);
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        budget ??= new ReferenceReadBudget();
        var plan = new AssetReferenceMigrationPlan();
        var oldPath = Normalize(oldValue, kind);
        foreach (var file in files.GroupBy(file => Path.GetFullPath(file.Path), StringComparer.OrdinalIgnoreCase).Select(group => group.First()))
        {
            var path = Path.GetFullPath(file.Path);
            budget.AdmitFile(path);
            var before = FileIntegrityTransaction.CaptureState(path);
            plan._expectedStates[path] = before;
            if (!before.Exists) continue;
            var document = ModReferenceDocumentReader.Load(path, budget);
            if (before != FileIntegrityTransaction.CaptureState(path))
                throw new IOException($"'{path}' changed during asset reference inspection. No files were written.");
            var count = 0;
            foreach (var element in document.Descendants())
            {
                if (!element.HasElements) Inspect(element.Value, element, null);
                foreach (var attribute in element.Attributes().Where(attribute => !attribute.IsNamespaceDeclaration))
                    Inspect(attribute.Value, element, attribute);
            }
            if (count > 0)
            {
                budget.InspectExisting(document, path);
                plan._documents[path] = document;
            }
            plan.ReferenceCount += count;

            void Inspect(string value, XElement element, XAttribute? attribute)
            {
                var candidate = Normalize(value, kind);
                var explicitDds = allowDdsIconReferences && Path.GetExtension(candidate).Equals(".dds", StringComparison.OrdinalIgnoreCase) &&
                    Path.ChangeExtension(candidate, ".png").Equals(oldPath, StringComparison.OrdinalIgnoreCase);
                if (!explicitDds && !candidate.Equals(oldPath, StringComparison.OrdinalIgnoreCase))
                {
                    // PNG/DDS aliases can resolve to distinct physical files. Do not guess.
                    if (kind == AssetReferenceKind.Icon &&
                        Path.ChangeExtension(candidate, null).Equals(Path.ChangeExtension(oldPath, null), StringComparison.OrdinalIgnoreCase) &&
                        (Path.GetExtension(candidate).Equals(".dds", StringComparison.OrdinalIgnoreCase) ||
                         Path.GetExtension(oldPath).Equals(".dds", StringComparison.OrdinalIgnoreCase)))
                        throw new InvalidOperationException($"Ambiguous PNG/DDS reference in '{path}' ({element.Name}). Resolve the physical asset before moving it. No files were written.");
                    return;
                }
                if (!IsSupported(element, attribute, kind))
                    throw new InvalidOperationException($"Unsupported asset reference in '{path}': {element.Name}{(attribute == null ? "" : "/@" + attribute.Name)}. No files were written.");
                var replacement = explicitDds ? Path.ChangeExtension(newValue, ".dds") : newValue;
                if (attribute == null) element.Value = replacement;
                else attribute.Value = replacement;
                count++;
            }
        }
        return plan;
    }

    public XDocument? GetDocument(string path)
        => _documents.TryGetValue(Path.GetFullPath(path), out var document) ? new XDocument(document) : null;

    public void Save(Action<XDocument, string> writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        foreach (var (path, document) in _documents) writer(new XDocument(document), path);
    }

    private static string Normalize(string value, AssetReferenceKind kind)
    {
        var path = value.Trim().Replace('/', '\\');
        if (kind != AssetReferenceKind.Icon) return path;
        path = IconCatalog.ToResourceRelativePath(path);
        return path.Length > 0 && Path.GetExtension(path).Length == 0 ? path + ".png" : path;
    }

    private static bool IsSupported(XElement element, XAttribute? attribute, AssetReferenceKind kind)
    {
        if (element.Name.Namespace != XNamespace.None || (attribute != null && attribute.Name.Namespace != XNamespace.None)) return false;
        var root = element.Document!.Root!;
        var owner = element.AncestorsAndSelf().FirstOrDefault(candidate => candidate.Parent == root);
        if (owner == null) return false;
        bool RootIs(params string[] names) => names.Contains(root.Name.LocalName, StringComparer.OrdinalIgnoreCase) && root.Name.Namespace == XNamespace.None;
        bool OwnerIs(string name) => owner.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase) && owner.Name.Namespace == XNamespace.None;
        var tag = element.Name.LocalName.ToLowerInvariant();
        if (kind != AssetReferenceKind.Icon)
            return attribute == null && RootIs("proto", "protomods") && OwnerIs("unit") &&
                tag == (kind == AssetReferenceKind.Animation ? "animfile" : "soundsetfile");
        if (attribute != null)
            return element == owner && RootIs("minorgods", "minorgodsmods") &&
                new[] { "portraitfilename", "portraiticonfilename" }.Contains(attribute.Name.LocalName, StringComparer.OrdinalIgnoreCase);
        if (RootIs("proto", "protomods") && OwnerIs("unit")) return tag == "icon";
        if (element.Parent != owner) return false;
        if (RootIs("civs", "civsmods") && OwnerIs("civ")) return tag is "icon" or "portrait" or "breakoutportrait";
        return tag == "icon" &&
            ((RootIs("techtree", "techtreemods") && OwnerIs("tech")) ||
             (RootIs("powers", "powersmod") && OwnerIs("power")) ||
             (RootIs("abilities", "abilitiesmods") && OwnerIs("ability")));
    }
}
