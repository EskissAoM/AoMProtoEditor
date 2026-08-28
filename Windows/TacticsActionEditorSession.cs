using System.Xml.Linq;

namespace AoMDivineDataEditor.Windows;

internal enum TacticsDocumentSaveOutcome
{
    Saved,
    Cancelled
}

internal enum TacticsEditorSaveResult
{
    Saved,
    Cancelled,
    Busy,
    Unavailable
}

/// <summary>
/// Owns the standalone tactics editor's committed document and window lifecycle.
/// The shared ProtoAction surface only supplies a serialized document; it does not
/// decide how tactics are saved or whether closing should prompt.
/// </summary>
internal sealed class TacticsActionEditorSession
{
    private readonly Func<XDocument, Task<TacticsDocumentSaveOutcome>>? _saveDocumentAsync;
    private XDocument _committedDocument;
    private int _saveInProgress;

    public TacticsActionEditorSession(
        string name,
        bool isReadOnly,
        XDocument sourceDocument,
        Func<XDocument, Task<TacticsDocumentSaveOutcome>>? saveDocumentAsync)
    {
        Name = name;
        IsReadOnly = isReadOnly;
        _committedDocument = new XDocument(sourceDocument);
        _saveDocumentAsync = saveDocumentAsync;
    }

    public string Name { get; }
    public bool IsReadOnly { get; }
    public bool CanSave => !IsReadOnly && _saveDocumentAsync != null;
    public bool IsSaveInProgress => Volatile.Read(ref _saveInProgress) != 0;
    public XDocument CommittedDocument => new(_committedDocument);
    public bool IsCloseAllowed { get; private set; }
    public bool IsClosePromptOpen { get; private set; }

    public async Task<TacticsEditorSaveResult> TrySaveAsync(XDocument document)
    {
        if (!CanSave || _saveDocumentAsync == null)
            return TacticsEditorSaveResult.Unavailable;

        if (Interlocked.CompareExchange(ref _saveInProgress, 1, 0) != 0)
            return TacticsEditorSaveResult.Busy;

        try
        {
            var saveSnapshot = new XDocument(document);
            if (await _saveDocumentAsync(saveSnapshot) != TacticsDocumentSaveOutcome.Saved)
                return TacticsEditorSaveResult.Cancelled;

            // Commit exactly the snapshot supplied to the successful callback.
            // Returning a clone from CommittedDocument prevents callers from
            // mutating the authoritative post-save comparison baseline.
            _committedDocument = new XDocument(saveSnapshot);
            return TacticsEditorSaveResult.Saved;
        }
        finally
        {
            Volatile.Write(ref _saveInProgress, 0);
        }
    }

    public bool HasUnsavedChanges(bool isDirty, Func<XDocument> buildCurrentDocument)
    {
        if (IsReadOnly || !isDirty)
            return false;

        try
        {
            return !XNode.DeepEquals(buildCurrentDocument(), _committedDocument);
        }
        catch
        {
            // A state that cannot be serialized must never be discarded silently.
            return true;
        }
    }

    public bool TryBeginClosePrompt()
    {
        if (IsClosePromptOpen)
            return false;

        IsClosePromptOpen = true;
        return true;
    }

    public void EndClosePrompt()
        => IsClosePromptOpen = false;

    public void AllowClose()
        => IsCloseAllowed = true;
}
