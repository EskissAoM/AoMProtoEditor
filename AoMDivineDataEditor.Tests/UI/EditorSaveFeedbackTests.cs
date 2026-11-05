using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Controls;
using AoMDivineDataEditor.Windows;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using System.Reflection;
using System.Xml.Linq;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class EditorSaveFeedbackTests
{
    [Fact]
    public void NavigationClearsContextAndTooltipButKeepsAnActiveSaveAndPartialProgress()
        => AvaloniaTestHost.Run(() =>
        {
            var status = new TextBlock(); var feedback = new EditorSaveFeedback(status, (_, _) => { });
            feedback.ShowContextMessage("Original entry (read-only).");
            feedback.ClearContextMessage();
            Assert.Equal("", status.Text); Assert.Null(ToolTip.GetTip(status));
            using (feedback.BeginSave())
            {
                feedback.ClearContextMessage(); Assert.Equal("Saving…", status.Text);
                feedback.Report(EditorSaveOutcome.Saved); feedback.Report(EditorSaveOutcome.Failed);
            }
            feedback.ClearContextMessage();
            Assert.Equal("Some changes saved; save not completed.", status.Text);
        });

    [Theory]
    [InlineData((int)EditorSaveOutcome.Saved)] [InlineData((int)EditorSaveOutcome.NoChanges)]
    [InlineData((int)EditorSaveOutcome.Cancelled)] [InlineData((int)EditorSaveOutcome.Blocked)]
    [InlineData((int)EditorSaveOutcome.Failed)] [InlineData((int)EditorSaveOutcome.NewerEdits)]
    [InlineData((int)EditorSaveOutcome.ReviewRequired)]
    public void NavigationDoesNotEraseSaveOutcomes(int outcome)
        => AvaloniaTestHost.Run(() =>
        {
            var status = new TextBlock(); var feedback = new EditorSaveFeedback(status, (_, _) => { });
            feedback.Report((EditorSaveOutcome)outcome); var before = status.Text;
            feedback.ClearContextMessage(); Assert.Equal(before, status.Text);
        });

    [Fact]
    public void HostedFooterRoutesContextWithoutDuplicatingSaveMessages()
        => AvaloniaTestHost.Run(() =>
        {
            var hostText = new TextBlock(); var localText = new TextBlock();
            var host = new EditorSaveFeedback(hostText, (_, _) => { });
            var local = new EditorSaveFeedback(localText, (_, _) => { });
            local.UseHostFooter(host.ShowContextMessage);
            Assert.False(localText.IsVisible);
            localText.Text = "Original entry (read-only).";
            Assert.Equal(localText.Text, hostText.Text);
            localText.Text = "Modified";
            Assert.Equal("Original entry (read-only).", hostText.Text);
            host.Report(EditorSaveOutcome.Blocked);
            localText.Text = "Another context notice";
            Assert.Equal("Save blocked.", hostText.Text);
            local.Report(EditorSaveOutcome.Saved);
            Assert.Equal("Save blocked.", hostText.Text);
        });

    [Theory]
    [InlineData((int)EditorSaveOutcome.Blocked)]
    [InlineData((int)EditorSaveOutcome.Failed)]
    [InlineData((int)EditorSaveOutcome.NewerEdits)]
    [InlineData((int)EditorSaveOutcome.ReviewRequired)]
    public void ContextCannotReplaceAnImportantOutcomeAndExplicitDiscardCanClearIt(int outcome)
        => AvaloniaTestHost.Run(() =>
        {
            var status = new TextBlock(); var feedback = new EditorSaveFeedback(status, (_, _) => { });
            feedback.Report((EditorSaveOutcome)outcome); var warning = status.Text;
            feedback.ShowContextMessage("Original entry (read-only).");
            Assert.Equal(warning, status.Text);
            feedback.ClearAfterDiscard(); Assert.Equal("", status.Text);
            feedback.ShowContextMessage("First line\nLonger context details");
            Assert.Equal("First line", status.Text);
        });

    [Fact]
    public void ContextAndDiscardDoNotInterruptAnActiveSaveOrHidePartialProgress()
        => AvaloniaTestHost.Run(() =>
        {
            var status = new TextBlock(); var feedback = new EditorSaveFeedback(status, (_, _) => { });
            using (feedback.BeginSave())
            {
                feedback.ClearAfterDiscard(); feedback.ShowContextMessage("Modified");
                Assert.Equal("Saving…", status.Text);
                feedback.Report(EditorSaveOutcome.Saved); feedback.Report(EditorSaveOutcome.Failed);
            }
            feedback.ShowContextMessage("Original entry (read-only).");
            Assert.Equal("Some changes saved; save not completed.", status.Text);
            feedback.ClearAfterDiscard(); Assert.Equal("", status.Text);
        });

    [Fact]
    public void ActualGlobalUnitSaveConfirmsTheWriteInTheSharedFooter()
    {
        using var fixture = new BaselineSafetyFixture();
        File.WriteAllText(fixture.ProtoPath,
            "<protomods><unit name='FeedbackUnit'><maxhitpoints>10</maxhitpoints><unittype>Unit</unittype><future keep='yes'/></unit></protomods>");
        AvaloniaTestHost.RunAsync(async () =>
        {
            var host = fixture.CreateHost(false);
            try
            {
                host.Call("InitializeEditorTabs");
                host.Call("BuildEditorPanel", "FeedbackUnit", true);
                host.Show();
                Dispatcher.UIThread.RunJobs();
                host.Set("_suppressEditorChangeTracking", false);
                var fields = (IDictionary<string, Control>)typeof(ProtoEditorWindow)
                    .GetField("_fieldControls", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
                ((TextBox)fields["maxhitpoints"]).Text = "11";
                host.Call("MarkDirty");
                var status = host.FindControl<TextBlock>("_statusMessage")!;
                var completed = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
                status.PropertyChanged += (_, args) =>
                {
                    if (args.Property == TextBlock.TextProperty && status.Text is "Changes saved." or "Save blocked." or "Save failed.")
                        completed.TrySetResult(status.Text);
                };
                host.Call("Save_Click", null!, new RoutedEventArgs());
                Assert.Equal("Changes saved.", await completed.Task.WaitAsync(TimeSpan.FromSeconds(15)));
                var unit = XDocument.Load(fixture.ProtoPath).Root!.Element("unit")!;
                Assert.Equal("11", unit.Element("maxhitpoints")!.Value);
                Assert.Equal("yes", (string?)unit.Element("future")!.Attribute("keep"));
                Assert.True(status.IsEffectivelyVisible);
                // Overlaying an embedded editor must not cover the host footer.
                host.FindControl<ContentControl>("_technologyHost")!.IsVisible = true;
                Assert.True(status.IsEffectivelyVisible);
            }
            finally
            {
                foreach (var child in host.OwnedWindows.ToArray()) child.Close();
                host.DisposeFixtureWindow();
            }
        });
    }

    [Fact]
    public void SuccessExpiresAfterThreeSecondsWithoutErasingLaterWarnings()
        => AvaloniaTestHost.Run(() =>
        {
            var status = new TextBlock();
            var expirations = new List<Action>();
            var feedback = new EditorSaveFeedback(status, (delay, callback) =>
            {
                Assert.Equal(TimeSpan.FromSeconds(3), delay);
                expirations.Add(callback);
            });
            feedback.Report(EditorSaveOutcome.Saved);
            Assert.Equal("Changes saved.", status.Text);
            status.Text = "Catalog unavailable. Retry loading.";
            expirations[0]();
            Assert.Equal("Catalog unavailable. Retry loading.", status.Text);
            feedback.Report(EditorSaveOutcome.Saved);
            expirations[1]();
            Assert.Equal("", status.Text);
        });

    [Fact]
    public void AnOlderIdenticalSuccessCannotExpireTheNewerSuccess()
        => AvaloniaTestHost.Run(() =>
        {
            var status = new TextBlock();
            var expirations = new List<Action>();
            var feedback = new EditorSaveFeedback(status, (_, callback) => expirations.Add(callback));
            feedback.Report(EditorSaveOutcome.Saved);
            feedback.Report(EditorSaveOutcome.Saved);
            expirations[0]();
            Assert.Equal("Changes saved.", status.Text);
            expirations[1]();
            Assert.Equal("", status.Text);
        });

    [Theory]
    [InlineData((int)EditorSaveOutcome.Blocked, "Save blocked.")]
    [InlineData((int)EditorSaveOutcome.Failed, "Save failed.")]
    [InlineData((int)EditorSaveOutcome.NewerEdits, "Saved; newer edits remain unsaved.")]
    [InlineData((int)EditorSaveOutcome.ReviewRequired, "Reloaded; review before saving.")]
    public void ImportantOutcomesRemainVisible(int outcome, string message)
        => AvaloniaTestHost.Run(() =>
        {
            var status = new TextBlock();
            var expirations = new List<Action>();
            var feedback = new EditorSaveFeedback(status, (_, callback) => expirations.Add(callback));
            feedback.Report(EditorSaveOutcome.Saved);
            feedback.Report((EditorSaveOutcome)outcome);
            expirations[0]();
            feedback.ClearSuccessOnEdit();
            Assert.Equal(message, status.Text);
            Assert.Single(expirations);
        });

    [Fact]
    public void BeginningAnotherEditRemovesTheOldSuccess()
        => AvaloniaTestHost.Run(() =>
        {
            var status = new TextBlock();
            var feedback = new EditorSaveFeedback(status, (_, _) => { });
            feedback.Report(EditorSaveOutcome.Saved);
            feedback.ClearSuccessOnEdit();
            Assert.Equal("", status.Text);
        });

    [Fact]
    public void GlobalSuccessIsNotPublishedUntilEveryStepCompletes()
        => AvaloniaTestHost.Run(() =>
        {
            var status = new TextBlock();
            var feedback = new EditorSaveFeedback(status, (_, _) => { });
            using var attempt = feedback.BeginSave();
            feedback.Report(EditorSaveOutcome.Saved);
            Assert.Equal("Saving…", status.Text);
            feedback.Report(EditorSaveOutcome.Saved);
            attempt.Complete();
            Assert.Equal("Changes saved.", status.Text);
        });

    [Fact]
    public void AStoppedSaveKeepsItsFailureClassificationAndPartialProgress()
        => AvaloniaTestHost.Run(() =>
        {
            var status = new TextBlock();
            var feedback = new EditorSaveFeedback(status, (_, _) => { });
            using (feedback.BeginSave())
            {
                feedback.Report(EditorSaveOutcome.Failed);
                feedback.Report(EditorSaveOutcome.Blocked);
            }
            Assert.Equal("Save failed.", status.Text);
            using (feedback.BeginSave())
            {
                feedback.Report(EditorSaveOutcome.Saved);
                feedback.Report(EditorSaveOutcome.Failed);
            }
            Assert.Equal("Some changes saved; save not completed.", status.Text);
        });

    [Fact]
    public void CompletedSaveDoesNotClaimNewerEditsWerePersisted()
        => AvaloniaTestHost.Run(() =>
        {
            var status = new TextBlock();
            var feedback = new EditorSaveFeedback(status, (_, _) => { });
            using var attempt = feedback.BeginSave();
            feedback.Report(EditorSaveOutcome.Saved);
            attempt.Complete(newerEdits: true);
            Assert.Equal("Saved; newer edits remain unsaved.", status.Text);
        });

    [Fact]
    public void DetailedErrorPopupRemainsOpenAndExplainsEarlierSuccessfulWrites()
        => AvaloniaTestHost.Run(() =>
        {
            var status = new TextBlock();
            var owner = new Window { Content = status };
            var feedback = new EditorSaveFeedback(status, (_, _) => { });
            feedback.Register(owner);
            owner.Show();
            using var attempt = feedback.BeginSave();
            feedback.Report(EditorSaveOutcome.Saved);
            var popup = new Prompt(PromptType.Error, "Power save blocked", "Choose a Power Type for TestPower.");
            try
            {
                _ = popup.ShowDialog(owner);
                Assert.True(popup.IsVisible);
                Assert.Contains("Choose a Power Type for TestPower.", popup.PromptText);
                Assert.Contains("saved earlier", popup.PromptText);
                Assert.Equal("Some changes saved; save not completed.", status.Text);
            }
            finally { popup.Close(); owner.Close(); }
        });

    [Fact]
    public void ManagerFeedbackGoesToTheVisibleManagerRatherThanBehindIt()
        => AvaloniaTestHost.Run(() =>
        {
            var status = new TextBlock();
            var owner = new Window { Content = status };
            var feedback = new EditorSaveFeedback(status, (_, _) => { });
            feedback.Register(owner);
            var shell = new ManagerListShell("Search", ["All"], "Items");
            var manager = new Window { Content = shell };
            owner.Show();
            manager.Show(owner);
            try
            {
                EditorSaveFeedback.Publish(owner, EditorSaveOutcome.Saved);
                Assert.NotEqual("Changes saved.", status.Text);
                Assert.Contains(shell.GetLogicalDescendants().OfType<TextBlock>(), text => text.Text == "Changes saved.");
                manager.Close();
                EditorSaveFeedback.Publish(owner, EditorSaveOutcome.Saved);
                Assert.Equal("Changes saved.", status.Text);
            }
            finally { manager.Close(); owner.Close(); }
        });
}
