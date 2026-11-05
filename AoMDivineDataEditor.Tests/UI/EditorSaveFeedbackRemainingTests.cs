using System.Reflection;
using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Controls;
using AoMDivineDataEditor.Windows;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using AvaloniaEdit;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class EditorSaveFeedbackRemainingTests
{
    private static T Field<T>(object owner, string name)
    {
        for (var type = owner.GetType(); type != null; type = type.BaseType)
            if (type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic) is { } field)
                return (T)field.GetValue(owner)!;
        throw new MissingFieldException(name);
    }

    private static Task Save(object editor) => (Task)editor.GetType()
        .GetMethod("SaveAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor, null)!;

    private static async Task<Prompt> Popup(Window owner)
    {
        for (var i = 0; i < 200; i++)
        {
            Dispatcher.UIThread.RunJobs();
            if (owner.OwnedWindows.OfType<Prompt>().FirstOrDefault() is { } popup) return popup;
            await Task.Delay(10);
        }
        throw new Xunit.Sdk.XunitException("Expected a detailed save popup within two seconds.");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void PowerExternalChoicePreservesDiskAndReportsCancelOrReview(bool reload, bool hosted)
    {
        using var workspace = new TestWorkspace(nameof(PowerExternalChoicePreservesDiskAndReportsCancelOrReview));
        var path = workspace.WriteText("powers_mods.xml", "<powersmod><power name='TestPower' type='Bolt'><radius>10</radius></power></powersmod>");
        AvaloniaTestHost.RunAsync(async () =>
        {
            var editor = new PowerEditorView([], path);
            var status = new TextBlock();
            var root = new StackPanel { Children = { editor, status } };
            var owner = new Window { Content = root };
            var relay = new EditorSaveFeedback(status);
            if (hosted) { relay.Register(owner); editor.SaveFeedbackReported = relay.Report; }
            owner.Show();
            try
            {
                editor.SetModifiedMode(true);
                editor.SelectPower("TestPower");
                Dispatcher.UIThread.RunJobs();
                await PowerXmlTestHelpers.UnlockAsync(editor);
                Field<TextEditor>(editor, "_rawXmlEditor").Text = "<power name='TestPower' type='Bolt'><radius>11</radius></power>";
                const string external = "<powersmod><power name='TestPower' type='Bolt'><radius>12</radius><external keep='yes'/></power></powersmod>";
                File.WriteAllText(path, external);
                using var attempt = hosted ? relay.BeginSave() : null;
                var saving = editor.SaveAsync();
                var popup = await Popup(owner);
                Assert.Equal("Powers changed outside the editor", popup.PromptTitle);
                Assert.Contains("Saving is paused for review", popup.PromptText);
                if (reload) popup.FindControl<Button>("_confirmButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                else popup.Close();
                Assert.False(await saving.WaitAsync(TimeSpan.FromSeconds(10)));
                attempt?.Dispose();
                Assert.Equal(reload ? "Reloaded; review before saving." : "Save cancelled.", hosted ? status.Text : editor.StatusMessage);
                Assert.Equal(external, File.ReadAllText(path));
                Assert.Equal(reload, Field<TextEditor>(editor, "_rawXmlEditor").IsReadOnly);
                Assert.Contains(reload ? "12" : "11", Field<TextEditor>(editor, "_rawXmlEditor").Text);
                var draft = editor.CreateModifiedDocumentSnapshot()!.Root!.Element("power")!;
                Assert.Equal(reload ? "12" : "11", draft.Element("radius")!.Value);
                if (reload) Assert.Equal("yes", (string?)draft.Element("external")!.Attribute("keep"));
                else Assert.True(editor.IsDirty);
            }
            finally { foreach (var popup in owner.OwnedWindows.ToArray()) popup.Close(); owner.Close(); }
        });
    }

    [Fact]
    public void RealGlobalSaveRetainsTheUnitWriteWhenTheLaterPowerStepStops()
    {
        using var fixture = new BaselineSafetyFixture();
        File.WriteAllText(fixture.ProtoPath, "<protomods><unit name='FeedbackUnit'><maxhitpoints>10</maxhitpoints><unittype>Unit</unittype><future keep='yes'/></unit></protomods>");
        var powerPath = fixture.Workspace.WriteText("TestMod/game/data/gameplay/powers_mods.xml", "<powersmod><power name='TestPower' type='Bolt'><radius>10</radius></power></powersmod>");
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
                ((TextBox)Field<IDictionary<string, Control>>(host, "_fieldControls")["maxhitpoints"]).Text = "11";
                host.Call("MarkDirty");
                var power = new PowerEditorView([], powerPath);
                power.SaveFeedbackReported = outcome => host.Call("ReportSaveFeedback", outcome);
                host.Set("_powerView", power);
                host.FindControl<ContentControl>("_technologyHost")!.Content = power;
                power.SetModifiedMode(true);
                power.SelectPower("TestPower");
                Dispatcher.UIThread.RunJobs();
                await PowerXmlTestHelpers.UnlockAsync(power);
                Field<TextEditor>(power, "_rawXmlEditor").Text = "<power name='TestPower' type='Bolt'><radius>11</radius></power>";
                const string external = "<powersmod><power name='TestPower' type='Bolt'><radius>12</radius><external/></power></powersmod>";
                File.WriteAllText(powerPath, external);
                host.Call("Save_Click", null!, new RoutedEventArgs());
                var popup = await Popup(host);
                Assert.Contains("No Power changes were saved", popup.PromptText);
                Assert.Contains("saved earlier", popup.PromptText);
                Assert.Equal("Some changes saved; save not completed.", host.FindControl<TextBlock>("_statusMessage")!.Text);
                Assert.Equal("11", XDocument.Load(fixture.ProtoPath).Root!.Element("unit")!.Element("maxhitpoints")!.Value);
                Assert.Equal(external, File.ReadAllText(powerPath));
                Assert.Equal("11", power.CreateModifiedDocumentSnapshot()!.Root!.Element("power")!.Element("radius")!.Value);
                popup.Close();
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                Assert.True(host.FindControl<Button>("_saveButton")!.IsEnabled);
                Assert.True(power.IsDirty);
            }
            finally { foreach (var popup in host.OwnedWindows.ToArray()) popup.Close(); host.DisposeFixtureWindow(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompactEditorsConfirmInTheirParentOnlyAfterTheSaveSucceeds(bool minorGod)
    {
        using var workspace = new TestWorkspace(nameof(CompactEditorsConfirmInTheirParentOnlyAfterTheSaveSucceeds));
        AvaloniaTestHost.RunAsync(async () =>
        {
            var shell = new ManagerListShell("Search", ["All"], "Items");
            var manager = new Window { Content = shell };
            manager.Show();
            var path = workspace.GetPath("saved.xml");
            var calls = 0;
            Task<bool> Commit(XElement element) { calls++; element.Save(path); return Task.FromResult(true); }
            Window editor = minorGod
                ? new MinorGodEditorWindow(new MinorGodDefinition("Test", "ClassicalAgeTest", false, new XElement("ClassicalAgeTest", new XAttribute("future", "keep"))),
                    ["ClassicalAgeTest"], [], new IconPreviewService(() => null, () => null), (_, element) => Commit(element))
                : new CultureEditorWindow(new CultureDefinition("TestCulture", false, XElement.Parse("<culture><name>TestCulture</name><displaynameid>STR_SHARED</displaynameid><future keep='yes'/></culture>")),
                    [], _ => Task.FromResult<string?>("Display name"), (_, element, _) => Commit(element));
            try
            {
                _ = editor.ShowDialog(manager);
                Assert.DoesNotContain(shell.GetLogicalDescendants().OfType<TextBlock>(), text => text.Text == "Changes saved.");
                await Save(editor).WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Equal(1, calls);
                Assert.False(editor.IsVisible);
                Assert.True(File.Exists(path));
                Assert.Contains(shell.GetLogicalDescendants().OfType<TextBlock>(), text => text.Text == "Changes saved.");
            }
            finally { editor.Close(); manager.Close(); }
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void StandaloneCommandConfirmsARealWriteOrShowsTheBlockingPopup(bool invalidName, bool newerEdit)
    {
        using var workspace = new TestWorkspace(nameof(StandaloneCommandConfirmsARealWriteOrShowsTheBlockingPopup));
        var path = workspace.WriteText("command.xml", "<protounitcommand><name>FeedbackCommand</name><future keep='yes'/></protounitcommand>");
        var before = File.ReadAllBytes(path);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var calls = 0;
            var publication = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var editor = new ProtoUnitCommandEditorWindow(ProtoUnitCommandDefinition.FromElement(XElement.Load(path)), false,
                new ProtoUnitCommandEditorContext(), null, async (command, _, _) =>
                {
                    calls++;
                    if (newerEdit) await publication.Task;
                    command.ToElement().Save(path);
                    return true;
                });
            editor.Show();
            try
            {
                if (invalidName) Field<TextBox>(editor, "_nameBox").Text = "";
                var saving = Save(editor);
                if (newerEdit)
                {
                    Assert.False(saving.IsCompleted);
                    Field<TextBox>(editor, "_nameBox").Text = "LaterCommand";
                    publication.SetResult();
                }
                if (invalidName)
                {
                    var popup = await Popup(editor);
                    Assert.Contains("internal name", popup.PromptText);
                    Assert.True(popup.IsVisible);
                    popup.Close();
                }
                await saving.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Equal(invalidName ? 0 : 1, calls);
                Assert.Equal(invalidName ? "Save blocked." : newerEdit ? "Saved; newer edits remain unsaved." : "Changes saved.", Field<TextBlock>(editor, "_statusText").Text);
                if (invalidName) Assert.Equal(before, File.ReadAllBytes(path));
                else
                {
                    Assert.Equal("yes", (string?)XElement.Load(path).Element("future")!.Attribute("keep"));
                    Assert.Equal("FeedbackCommand", XElement.Load(path).Element("name")!.Value);
                    if (newerEdit) Assert.Equal("LaterCommand", Field<TextBox>(editor, "_nameBox").Text);
                }
            }
            finally
            {
                typeof(ProtoUnitCommandEditorWindow).GetField("_allowClose", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(editor, true);
                foreach (var popup in editor.OwnedWindows.ToArray()) popup.Close();
                editor.Close();
            }
        });
    }

    [Fact]
    public void VisibleSettingsSaveRefusalKeepsTheTypedValueAndDisplaysDetails()
    {
        using var fixture = new SettingsWorkflowFixture();
        var before = File.ReadAllBytes(fixture.Path);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var editor = new ProtoSettingsWindow(fixture.Store);
            editor.Show();
            try
            {
                editor.FindControl<TextBox>("DataBarPathBox")!.Text = "typed.bar";
                using var gate = new ProtoEditorSettingsFileSystem().TryAcquireLock(fixture.Path + ".lock");
                Assert.NotNull(gate);
                editor.FindControl<Button>("SaveSettingsButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var popup = await Popup(editor);
                Assert.Equal("Settings not saved", popup.PromptTitle);
                Assert.Contains("Busy", popup.PromptText);
                Assert.False(editor.IsSaved);
                Assert.Equal("typed.bar", editor.FindControl<TextBox>("DataBarPathBox")!.Text);
                Assert.Equal(before, File.ReadAllBytes(fixture.Path));
                popup.Close();
            }
            finally { foreach (var popup in editor.OwnedWindows.ToArray()) popup.Close(); editor.Close(); }
        });
    }
}
