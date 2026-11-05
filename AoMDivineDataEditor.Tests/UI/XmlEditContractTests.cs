using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Controls;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using AvaloniaEdit;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class XmlEditContractTests
{
    private const string Original = "<tech name='A'><future keep='yes'/></tech>";

    [Fact]
    public void InvalidRawTextIsDirtyImmediatelyAndSurvivesRejectedValidationAndPreviewRefresh()
    {
        var session = new XmlEditSession(Original, true);
        Assert.True(session.BeginEditing());
        Assert.False(session.IsDirty);
        Assert.True(session.Edit("<tech name='A'><unfinished>"));
        var save = session.CaptureForSave()!;
        Assert.Throws<System.Xml.XmlException>(() => System.Xml.Linq.XElement.Parse(save.Text));
        Assert.True(session.IsDirty);
        Assert.True(session.IsEditing);
        Assert.False(session.RefreshFromModel(Original));
        Assert.Equal("<tech name='A'><unfinished>", session.Text);
    }

    [Fact]
    public void ReturningTextToTheBaselineRemovesOnlyTheXmlDirtyState()
    {
        var session = new XmlEditSession(Original, true);
        session.BeginEditing(); session.Edit("broken"); session.Edit(Original);
        Assert.False(session.IsDirty);
        Assert.True(session.IsEditing);
        Assert.False(session.RefreshFromModel("<tech name='DelayedForm'/>"));
        Assert.Equal(Original, session.Text);
    }

    [Fact]
    public void ConfirmedSaveRelocksAndUsesTheModulesCommittedPreview()
    {
        var session = new XmlEditSession(Original, true);
        session.BeginEditing(); session.Edit("<tech name='B'><future keep='yes'/></tech>");
        var saved = session.CaptureForSave()!;
        Assert.True(session.IsDirty); // Capturing/validating is not a successful save.
        const string formatted = "<tech name=\"B\">\n  <future keep=\"yes\" />\n</tech>";
        Assert.Equal(XmlSaveAcceptance.Saved, session.AcceptSaved(saved, formatted));
        Assert.Equal(formatted, session.Text);
        Assert.False(session.IsDirty);
        Assert.False(session.IsEditing);
        Assert.Null(session.CaptureForSave());
    }

    [Fact]
    public void AFailedSaveWithoutAcknowledgementKeepsTheExactDraftEditable()
    {
        var session = new XmlEditSession(Original, true);
        session.BeginEditing(); session.Edit("<tech name='B'/>");
        var captured = session.CaptureForSave()!;
        // The owning module failed validation, conflict resolution or writing.
        Assert.Equal(captured.Text, session.Text);
        Assert.True(session.IsDirty);
        Assert.True(session.IsEditing);
    }

    [Fact]
    public void SaveCompletionCannotClearTextTypedWhileThatSaveWasAwaiting()
    {
        var session = new XmlEditSession(Original, true);
        session.BeginEditing(); session.Edit("<tech name='B'/>");
        var saving = session.CaptureForSave()!;
        session.Edit("<tech name='C'><unfinished>");
        Assert.Equal("<tech name='B'/>", saving.Text);
        Assert.Equal(XmlSaveAcceptance.NewerEdits, session.AcceptSaved(saving, "<tech name='B'/>"));
        Assert.Equal("<tech name='C'><unfinished>", session.Text);
        Assert.True(session.IsDirty);
        Assert.True(session.IsEditing);
    }

    [Theory]
    [InlineData("<tech name='Reloaded'/>")]
    [InlineData(Original)]
    public void DiscardOrReloadInvalidatesInFlightSaveBeforeShowingTheAuthoritativeState(string authoritative)
    {
        var session = new XmlEditSession(Original, true);
        session.BeginEditing(); session.Edit("<tech name='B'/>");
        var saving = session.CaptureForSave()!;
        session.Reset(authoritative);
        Assert.Equal(XmlSaveAcceptance.Rejected, session.AcceptSaved(saving, "<tech name='B'/>"));
        Assert.Equal(authoritative, session.Text);
        Assert.False(session.IsDirty);
        Assert.False(session.IsEditing);
    }

    [Fact]
    public void OlderOrRepeatedCompletionCannotRegressAnAlreadyAcknowledgedSave()
    {
        var session = new XmlEditSession(Original, true);
        session.BeginEditing(); session.Edit("<tech name='B'/>");
        var first = session.CaptureForSave()!;
        session.Edit("<tech name='C'/>"); var second = session.CaptureForSave()!;
        Assert.Equal(XmlSaveAcceptance.Saved, session.AcceptSaved(second, second.Text));
        Assert.Equal(XmlSaveAcceptance.Rejected, session.AcceptSaved(first, first.Text));
        Assert.Equal(XmlSaveAcceptance.Rejected, session.AcceptSaved(second, second.Text));
        Assert.Equal("<tech name='C'/>", session.Text);
    }

    [Fact]
    public void AnotherRecordsSaveCannotAcknowledgeThisRecordsBuffer()
    {
        var first = new XmlEditSession(Original, true);
        var second = new XmlEditSession("<tech name='B'/>", true);
        first.BeginEditing(); second.BeginEditing(); second.Edit("<broken>");
        Assert.Equal(XmlSaveAcceptance.Rejected, second.AcceptSaved(first.CaptureForSave()!, Original));
        Assert.Equal("<broken>", second.Text);
        Assert.True(second.IsDirty);
    }

    [Fact]
    public void OriginalOrUnavailableRecordCannotUnlockEditOrProduceASaveSnapshot()
    {
        var session = new XmlEditSession(Original, false);
        Assert.False(session.BeginEditing());
        Assert.False(session.Edit("<tech name='Changed'/>"));
        Assert.Null(session.CaptureForSave());
        Assert.Equal(Original, session.Text);
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void SwitchingRecordsPreservesEachBufferAndNeverLeaksEditModeOrAcknowledgement()
        => AvaloniaTestHost.Run(() =>
        {
            var editor = new TextEditor(); var button = new Button(); var form = new StackPanel();
            var edits = 0;
            using var controller = new XmlEditController(editor, button, form, () => edits++);
            var first = new XmlEditSession(Original, true);
            var second = new XmlEditSession("<tech name='B'/>", true);
            controller.Bind(first, true);
            Assert.True(editor.IsReadOnly); Assert.True(form.IsEnabled);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(editor.IsReadOnly); Assert.False(form.IsEnabled);
            var savingFirst = first.CaptureForSave()!;
            editor.Text = "<tech name='A'><invalid>";
            Assert.True(first.IsDirty); Assert.Equal(1, edits);
            controller.Bind(second, true);
            Dispatcher.UIThread.RunJobs();
            Assert.True(editor.IsReadOnly); Assert.True(form.IsEnabled);
            Assert.Equal("<tech name='B'/>", editor.Text);
            Assert.Equal(XmlSaveAcceptance.NewerEdits, first.AcceptSaved(savingFirst, Original));
            Assert.Equal("<tech name='B'/>", editor.Text);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); editor.Text = "<tech name='B'><unfinished>";
            controller.Bind(first, true);
            Assert.False(editor.IsReadOnly); Assert.False(form.IsEnabled);
            Assert.Equal("<tech name='A'><invalid>", editor.Text);
            controller.Bind(second, true);
            Assert.False(editor.IsReadOnly); Assert.False(form.IsEnabled);
            Assert.Equal("<tech name='B'><unfinished>", editor.Text);
            Assert.Equal(2, edits);
        });

    [Fact]
    public void ProgrammaticRefreshDoesNotMarkDirtyAndReadOnlyXmlStaysAvailableForCopying()
        => AvaloniaTestHost.Run(() =>
        {
            var editor = new TextEditor(); var button = new Button(); var form = new StackPanel();
            var edits = 0;
            using var controller = new XmlEditController(editor, button, form, () => edits++);
            var session = new XmlEditSession(Original, true);
            controller.Bind(session, true);
            session.RefreshFromModel("<tech name='Refreshed'/>");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, edits); Assert.False(session.IsDirty);
            var original = new XmlEditSession(Original, false);
            controller.Bind(original, false);
            Assert.True(editor.IsEnabled); Assert.True(editor.Focusable); Assert.True(editor.IsReadOnly);
            Assert.False(button.IsVisible); Assert.False(form.IsEnabled);
            editor.SelectAll(); Assert.Equal(Original, editor.SelectedText);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(original.IsEditing); Assert.True(editor.IsReadOnly);
            Assert.Equal(0, edits);
        });

    [Fact]
    public void SuccessfulSaveRestoresTheFormButLaterEditsAndFailedSavesKeepXmlUnlocked()
        => AvaloniaTestHost.Run(() =>
        {
            var editor = new TextEditor(); var button = new Button(); var form = new StackPanel();
            using var controller = new XmlEditController(editor, button, form, () => { });
            var session = new XmlEditSession(Original, true); controller.Bind(session, true);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); editor.Text = "<tech name='B'/>";
            var save = session.CaptureForSave()!;
            editor.Text = "<tech name='C'/>";
            Assert.Equal(XmlSaveAcceptance.NewerEdits, session.AcceptSaved(save, save.Text));
            Assert.False(editor.IsReadOnly); Assert.False(form.IsEnabled);
            var latest = session.CaptureForSave()!;
            Assert.Equal(XmlSaveAcceptance.Saved, session.AcceptSaved(latest, latest.Text));
            Assert.True(editor.IsReadOnly); Assert.True(form.IsEnabled); Assert.True(button.IsEnabled);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); editor.Text = "<broken>";
            Assert.False(form.IsEnabled); Assert.False(editor.IsReadOnly);
            session.Reset(Original);
            Assert.True(form.IsEnabled); Assert.True(editor.IsReadOnly); Assert.False(session.IsDirty);
        });

    [Fact]
    public void DisposedControllerCannotKeepAnOldWindowListeningToDraftChanges()
        => AvaloniaTestHost.Run(() =>
        {
            var editor = new TextEditor(); var button = new Button(); var form = new StackPanel();
            var session = new XmlEditSession(Original, true);
            var controller = new XmlEditController(editor, button, form, () => throw new InvalidOperationException("Disposed callback"));
            controller.Bind(session, true); controller.Dispose(); controller.Dispose();
            session.BeginEditing(); session.Edit("<tech name='B'/>");
            Assert.Equal(Original, editor.Text);
            Assert.Throws<ObjectDisposedException>(() => controller.Bind(session, true));
        });
}
