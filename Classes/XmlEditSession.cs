namespace AoMDivineDataEditor.Classes;

internal enum XmlSaveAcceptance { Rejected, Saved, NewerEdits }

/// <summary>
/// One record's raw buffer, independent of selection and persistence. The owning
/// module supplies the initial preview, keeps this session with that record's
/// draft, validates captured XML, and acknowledges only a successful save.
/// </summary>
internal sealed class XmlEditSession(string text, bool canEdit)
{
    private string _baseline = text;
    private long _epoch;
    private long _revision;
    private long _lastSavedRevision = -1;

    internal string Text { get; private set; } = text;
    internal bool CanEdit { get; } = canEdit;
    internal bool IsEditing { get; private set; }
    internal bool IsDirty => !string.Equals(Text, _baseline, StringComparison.Ordinal);
    internal event Action? Changed;

    internal bool BeginEditing()
    {
        if (!CanEdit || IsEditing) return false;
        ++_revision;
        IsEditing = true;
        Changed?.Invoke();
        return true;
    }

    internal bool Edit(string text)
    {
        if (!CanEdit || !IsEditing || string.Equals(Text, text, StringComparison.Ordinal)) return false;
        Text = text;
        ++_revision;
        Changed?.Invoke();
        return true;
    }

    // Delayed form/preview callbacks must never replace an unlocked raw buffer,
    // even before the first keystroke. No parse/normalization occurs while typing.
    internal bool RefreshFromModel(string text)
    {
        if (IsEditing || IsDirty) return false;
        if (string.Equals(Text, text, StringComparison.Ordinal)) return true;
        Reset(text);
        return true;
    }

    internal Snapshot? CaptureForSave()
        => CanEdit && IsEditing ? new Snapshot(this, _epoch, _revision, Text) : null;

    /// <summary>
    /// Call only after the module's ordinary validation/reference/conflict checks
    /// and confirmed persistence. An accepted candidate is not itself a save.
    /// </summary>
    internal XmlSaveAcceptance AcceptSaved(Snapshot snapshot, string savedPreview)
    {
        if (!ReferenceEquals(snapshot.Owner, this) || snapshot.Epoch != _epoch ||
            snapshot.Revision <= _lastSavedRevision)
            return XmlSaveAcceptance.Rejected;

        _lastSavedRevision = snapshot.Revision;
        _baseline = savedPreview;
        if (_revision != snapshot.Revision)
        {
            Changed?.Invoke();
            return XmlSaveAcceptance.NewerEdits;
        }

        Text = savedPreview;
        IsEditing = false;
        Changed?.Invoke();
        return XmlSaveAcceptance.Saved;
    }

    // The module resolves Discard/Reload against its authoritative draft or disk
    // baseline, including form/string changes, then supplies the resulting XML.
    // Invalidate every in-flight save snapshot before displaying that state.
    internal void Reset(string authoritativePreview)
    {
        ++_epoch;
        ++_revision;
        _lastSavedRevision = -1;
        _baseline = Text = authoritativePreview;
        IsEditing = false;
        Changed?.Invoke();
    }

    internal sealed class Snapshot
    {
        internal XmlEditSession Owner { get; }
        internal long Epoch { get; }
        internal long Revision { get; }
        internal string Text { get; }

        internal Snapshot(XmlEditSession owner, long epoch, long revision, string text)
            => (Owner, Epoch, Revision, Text) = (owner, epoch, revision, text);
    }
}
