using System.Xml.Linq;

namespace AoMDivineDataEditor.Classes;

public sealed record PreparedModXmlDocument(
    string Path,
    ModXmlDocumentKind Kind,
    XDocument Document,
    FileIntegrityTransaction.FileState LoadedState);

/// <summary>
/// Shared lifecycle for editable auxiliary mod documents: stable load, structural
/// validation, expected-state verification, and exact-byte transaction rollback.
/// </summary>
public static class ModDocumentPersistence
{
    public static PreparedModXmlDocument Prepare(string path, ModXmlDocumentKind kind)
    {
        var fullPath = Path.GetFullPath(path);
        var loaded = ModXmlDocumentSafety.LoadOrCreate(fullPath, kind);
        return new PreparedModXmlDocument(fullPath, kind, loaded.Document, loaded.FileState);
    }

    public static void Execute(
        IEnumerable<PreparedModXmlDocument> documents,
        IEnumerable<string?>? additionalPaths,
        IReadOnlyDictionary<string, FileIntegrityTransaction.FileState>? additionalExpectedStates,
        Action commit)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(commit);
        var prepared = documents.ToList();
        foreach (var item in prepared)
        {
            var validation = ModXmlDocumentSafety.Validate(item.Document, item.Kind);
            if (!validation.IsSafeToEdit)
                throw new InvalidDataException(validation.ToDisplayMessage(item.Path));
        }

        var paths = prepared.Select(item => (string?)item.Path)
            .Concat(additionalPaths ?? [])
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToList();
        var expected = FileIntegrityTransaction.CaptureStates(paths)
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase);
        foreach (var item in prepared)
            expected[item.Path] = item.LoadedState;
        if (additionalExpectedStates != null)
        {
            foreach (var state in additionalExpectedStates)
                expected[Path.GetFullPath(state.Key)] = state.Value;
        }

        FileIntegrityTransaction.Execute(paths, expected, commit);
    }

    public static async Task ExecuteAsync(
        IEnumerable<PreparedModXmlDocument> documents,
        IEnumerable<string?>? additionalPaths,
        IReadOnlyDictionary<string, FileIntegrityTransaction.FileState>? additionalExpectedStates,
        Func<Task> commit)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(commit);
        var prepared = documents.ToList();
        foreach (var item in prepared)
        {
            var validation = ModXmlDocumentSafety.Validate(item.Document, item.Kind);
            if (!validation.IsSafeToEdit)
                throw new InvalidDataException(validation.ToDisplayMessage(item.Path));
        }

        var paths = prepared.Select(item => (string?)item.Path)
            .Concat(additionalPaths ?? [])
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToList();
        var expected = FileIntegrityTransaction.CaptureStates(paths)
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase);
        foreach (var item in prepared)
            expected[item.Path] = item.LoadedState;
        if (additionalExpectedStates != null)
        {
            foreach (var state in additionalExpectedStates)
                expected[Path.GetFullPath(state.Key)] = state.Value;
        }

        await FileIntegrityTransaction.ExecuteAsync(paths, expected, commit);
    }
}
