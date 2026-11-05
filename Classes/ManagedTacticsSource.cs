using System.Xml.Linq;

namespace AoMDivineDataEditor.Classes;

/// <summary>Retains physical format and competing aliases for one loose tactics name.</summary>
internal sealed class ManagedTacticsSource
{
    private ManagedTacticsSource(string xmlPath)
    {
        XmlPath = xmlPath;
        ExpectedStates = FileIntegrityTransaction.CaptureStates(
            [xmlPath, xmlPath + ".xmb", xmlPath[..^".tactics".Length] + ".xmb"]);
        Paths = ExpectedStates.Where(pair => pair.Value.Exists).Select(pair => pair.Key).ToList();
    }

    public string XmlPath { get; }
    public IReadOnlyDictionary<string, FileIntegrityTransaction.FileState> ExpectedStates { get; }
    public IReadOnlyList<string> Paths { get; }
    public bool IsAmbiguous => Paths.Count > 1;
    public bool IsCompiled => Paths.Count == 1 && ModReferenceDocumentReader.IsCompiledTactics(Paths[0]);
    public bool CanEdit => Paths.Count == 1 && !IsCompiled;
    public string? ReadOnlyReason => IsAmbiguous
        ? "Multiple XML/compiled files share this tactics name. " +
          $"The read-only viewer displays '{Paths[0]}'. Resolve the competing files before editing, renaming, deleting, or duplicating:\n" +
          string.Join("\n", Paths)
        : IsCompiled
            ? "Compiled tactics are read-only. Duplicate as editable XML to make changes; the compiled source is preserved."
            : null;

    public static ManagedTacticsSource Resolve(string directory, string name)
    {
        var relative = name.Trim().Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        if (relative.EndsWith(".xmb", StringComparison.OrdinalIgnoreCase)) relative = relative[..^4];
        if (!relative.EndsWith(".tactics", StringComparison.OrdinalIgnoreCase)) relative += ".tactics";
        var root = Path.GetFullPath(directory);
        var path = Path.GetFullPath(Path.Combine(root, relative));
        var contained = Path.GetRelativePath(root, path);
        if (Path.IsPathRooted(relative) || Path.IsPathRooted(contained) || contained == ".." ||
            contained.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("The tactics name must stay inside the mod tactics folder. No files were written.");
        return new ManagedTacticsSource(path);
    }

    public string RequireEditable()
    {
        if (!CanEdit) throw new InvalidOperationException(ReadOnlyReason ?? $"Tactics file '{XmlPath}' no longer exists. No files were written.");
        return Paths[0];
    }

    public void RequireMissing()
    {
        if (Paths.Count > 0)
            throw new IOException($"A tactics file already exists for '{XmlPath}' (XML or compiled). No files were written.");
    }

    public XDocument Load(bool allowAmbiguousView = false)
    {
        if ((!allowAmbiguousView && IsAmbiguous) || Paths.Count == 0)
            throw new InvalidOperationException(ReadOnlyReason ?? $"Tactics file '{XmlPath}' no longer exists. No files were written.");
        var document = ModReferenceDocumentReader.Load(Paths[0]);
        foreach (var state in ExpectedStates)
            if (FileIntegrityTransaction.CaptureState(state.Key) != state.Value)
                throw new IOException($"'{state.Key}' changed while tactics were being loaded. No files were written.");
        var validation = ModXmlDocumentSafety.Validate(document, ModXmlDocumentKind.Tactics);
        if (!validation.IsSafeToEdit) throw new InvalidDataException(validation.ToDisplayMessage(Paths[0]));
        return document;
    }
}
