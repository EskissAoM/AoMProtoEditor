using AoMDivineDataEditor.Classes;

namespace AoMDivineDataEditor.Windows;

public partial class ProtoEditorWindow
{
    private EditorSaveFeedback _saveFeedback = null!;

    private void InitializeSaveFeedback()
    {
        _saveFeedback = new EditorSaveFeedback(_statusMessage);
        _saveFeedback.Register(this);
    }

    private void ReportSaveFeedback(EditorSaveOutcome outcome)
        => EditorSaveFeedback.Publish(this, outcome);

    private void ReportContextStatus(string text) => _saveFeedback.ShowContextMessage(text);

    private void ClearFeedbackAfterDiscard()
    {
        if (!HasPendingSaveEdits()) _saveFeedback.ClearAfterDiscard();
    }

    private bool HasPendingSaveEdits()
        => _isDirty || _technologyView?.IsDirty == true ||
           _majorGodView?.IsDirty == true || _powerView?.IsDirty == true;
}
