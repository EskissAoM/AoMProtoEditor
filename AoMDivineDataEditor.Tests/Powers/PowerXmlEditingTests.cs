using System.Reflection;
using System.Collections;
using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.GameData;
using AoMDivineDataEditor.Windows;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using AvaloniaEdit;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class PowerXmlEditingTests : IDisposable
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string Initial = "<powersmod future='root'><power name='A' type='GodPower'><activetime>10</activetime><!--keep--><future keep='yes'/></power><power name='B' type='GodPower'><future sibling='yes'/></power></powersmod>";
    private static TextEditor Raw(PowerEditorView view) => view.FindControl<TextEditor>("_rawXmlEditor")!;
    private static T Field<T>(object view, string name) => (T)view.GetType().GetField(name, Private)!.GetValue(view)!;
    public void Dispose() => AvaloniaTestHost.CollectUiGarbage();

    private static void WithEditor(Func<PowerEditorView, string, Task> check)
    {
        using var workspace = new TestWorkspace(nameof(PowerXmlEditingTests));
        var path = workspace.WriteText("powers_mods.xml", Initial);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var view = new PowerEditorView([], path);
            try { view.SetModifiedMode(true); await view.SelectPowerAsync("A"); await check(view, path); }
            finally { view.ReleasePowerXmlEditing(); }
        });
    }

    [Fact]
    public void StartsLockedAndUnchangedUnlockSaveRelocksWithoutRewriting()
        => WithEditor(async (view, path) =>
        {
            var before = File.ReadAllBytes(path);
            Assert.True(Raw(view).IsReadOnly);
            Assert.True(Raw(view).IsEnabled);
            await PowerXmlTestHelpers.UnlockAsync(view);
            Assert.False(view.IsDirty);
            Assert.False(view.FindControl<Grid>("_powerStructuredForm")!.IsEnabled);
            Assert.True(await view.SaveAsync());
            Assert.True(Raw(view).IsReadOnly);
            Assert.True(view.FindControl<Grid>("_powerStructuredForm")!.IsEnabled);
            Assert.False(view.IsDirty);
            Assert.Equal(before, File.ReadAllBytes(path));
        });

    [Fact]
    public void OriginalXmlIsCopyableButCannotBeUnlocked()
    {
        AvaloniaTestHost.RunAsync(async () =>
        {
            var definitions = PowerCatalog.ExtractDefinitions([XDocument.Parse("<powers><power name='Original' type='GodPower'/></powers>")], true);
            var view = new PowerEditorView(definitions, null);
            try
            {
                await view.SelectPowerAsync("Original");
                Assert.True(Raw(view).IsReadOnly); Assert.True(Raw(view).IsEnabled); Assert.True(Raw(view).Focusable);
                Assert.False(view.FindControl<Button>("_editXmlButton")!.IsVisible);
                Assert.False(view.IsDirty);
            }
            finally { view.ReleasePowerXmlEditing(); }
        });
    }

    [Fact]
    public void SavePreservesUnknownXmlAndSiblingThenReopensStructuredFields()
        => WithEditor(async (view, path) =>
        {
            await PowerXmlTestHelpers.UnlockAsync(view);
            Raw(view).Text = "<power name='A' type='GodPower' extra='yes'><activetime>17</activetime><!--keep--><future keep='yes'><nested/></future></power>";
            Assert.True(view.IsDirty); Assert.True(view.IsPowerDirty("A"));
            Assert.True(await view.SaveAsync());
            await view.SelectPowerAsync("B"); await view.SelectPowerAsync("A");
            var document = XDocument.Load(path);
            var entry = PowerCatalog.Find(document, "A")!;
            Assert.Equal("17", entry.Element("activetime")!.Value);
            Assert.Equal("yes", (string?)entry.Attribute("extra"));
            Assert.NotNull(entry.Element("future")!.Element("nested"));
            Assert.Contains(entry.Nodes().OfType<XComment>(), comment => comment.Value == "keep");
            Assert.True(XNode.DeepEquals(PowerCatalog.Find(XDocument.Parse(Initial), "B"), PowerCatalog.Find(document, "B")));
            Assert.Equal("17", view.FindControl<TextBox>("_activeTimeBox")!.Text);
            Assert.True(Raw(view).IsReadOnly); Assert.False(view.IsDirty);
        });

    [Theory]
    [InlineData("<power>")]
    [InlineData("<tech name='A'/>")]
    [InlineData("<power name='B' type='GodPower'/>")]
    [InlineData("<power name='A'/>")]
    [InlineData("<power xmlns='future' name='A' type='GodPower'/>")]
    public void InvalidXmlKeepsExactDraftAndCanBeDiscarded(string text)
        => WithEditor(async (view, path) =>
        {
            var before = File.ReadAllBytes(path);
            await PowerXmlTestHelpers.UnlockAsync(view); Raw(view).Text = text;
            Assert.False(await view.SaveAsync());
            Assert.Equal(before, File.ReadAllBytes(path)); Assert.Equal(text, Raw(view).Text); Assert.False(Raw(view).IsReadOnly);
            Assert.DoesNotContain(view.GetLogicalDescendants().OfType<Control>(), field => field.Classes.Contains("invalid-field"));
            Assert.True(await view.CommitCurrentPowerAsync()); // Navigation must not demand a parse.
            view.DiscardPowerChanges("A", PowerCatalog.Find(XDocument.Parse(Initial), "A"));
            await view.SelectPowerAsync("B"); await view.SelectPowerAsync("A");
            Assert.True(Raw(view).IsReadOnly); Assert.False(view.IsDirty); Assert.False(view.IsPowerDirty("A"));
            Assert.Equal(before, File.ReadAllBytes(path));
        });

    [Fact]
    public void RetainedRecordsKeepIndependentBuffersAndSaveAllTogether()
        => WithEditor(async (view, path) =>
        {
            await PowerXmlTestHelpers.UnlockAsync(view);
            Raw(view).Text = "<power name='A' type='GodPower'><future value='first'/></power>";
            var first = Raw(view).Text;
            await view.SelectPowerAsync("B"); Assert.True(Raw(view).IsReadOnly);
            await PowerXmlTestHelpers.UnlockAsync(view);
            Raw(view).Text = "<power name='B' type='GodPower'><future value='second'/></power>";
            await view.SelectPowerAsync("A"); Assert.Equal(first, Raw(view).Text); Assert.False(Raw(view).IsReadOnly);
            Assert.True(await view.SaveAsync());
            Assert.Equal("first", (string?)PowerCatalog.Find(XDocument.Load(path), "A")!.Element("future")!.Attribute("value"));
            Assert.Equal("second", (string?)PowerCatalog.Find(XDocument.Load(path), "B")!.Element("future")!.Attribute("value"));
            await view.SelectPowerAsync("B"); Assert.True(Raw(view).IsReadOnly); Assert.False(view.IsDirty);
        });

    [Fact]
    public void InvalidInactiveBufferBlocksAllWrites()
        => WithEditor(async (view, path) =>
        {
            var before = File.ReadAllBytes(path);
            await PowerXmlTestHelpers.UnlockAsync(view); Raw(view).Text = "<power name='A'>";
            await view.SelectPowerAsync("B");
            view.FindControl<TextBox>("_activeTimeBox")!.Text = "22";
            Assert.False(await view.SaveAsync()); Assert.Equal(before, File.ReadAllBytes(path));
            await view.SelectPowerAsync("A"); Assert.Equal("<power name='A'>", Raw(view).Text);
        });

    [Fact]
    public void RawRenameUpdatesReferencesInTheSamePowerDocumentBeforeValidation()
    {
        using var workspace = new TestWorkspace(nameof(RawRenameUpdatesReferencesInTheSamePowerDocumentBeforeValidation));
        var path = workspace.WriteText("powers_mods.xml", "<powersmod><power name='A' type='GodPower'/><power name='Clone' type='PowerClone'><duplicatetargetreplacement><target>A</target></duplicatetargetreplacement></power></powersmod>");
        AvaloniaTestHost.RunAsync(async () =>
        {
            var view = new PowerEditorView([], path);
            try
            {
                view.SetModifiedMode(true); await view.SelectPowerAsync("A"); await PowerXmlTestHelpers.UnlockAsync(view);
                Raw(view).Text = "<power name='NewA' type='GodPower'/>";
                Assert.True(await view.SaveAsync());
                Assert.Equal("NewA", PowerCatalog.Find(XDocument.Load(path), "Clone")!.Element("duplicatetargetreplacement")!.Element("target")!.Value);
                Assert.True(Raw(view).IsReadOnly); Assert.False(view.IsDirty);
            }
            finally { view.ReleasePowerXmlEditing(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RawRenameStringTransactionRetainsFailedOrNewerDraftForRetry(bool failFirstWrite)
    {
        using var workspace = new TestWorkspace(nameof(RawRenameStringTransactionRetainsFailedOrNewerDraftForRetry));
        var oldId = PowerCatalog.BuildStringId("A", "displaynameid");
        var newId = PowerCatalog.BuildStringId("NewA", "displaynameid");
        var path = workspace.WriteText("TestMod/game/data/gameplay/powers_mods.xml", $"<powersmod><power name='A' type='GodPower'><displaynameid>{oldId}</displaynameid></power></powersmod>");
        var stringsPath = workspace.WriteText("TestMod/game/data/strings/English/stringmods.txt", $"ID = \"{oldId}\" ; Str = \"Display\"\r\n");
        var before = File.ReadAllBytes(path); var stringsBefore = File.ReadAllBytes(stringsPath);
        AvaloniaTestHost.RunAsync(async () =>
        {
            PowerEditorView? view = null;
            var first = true;
            view = new PowerEditorView([], path, modStringsPath: stringsPath,
                resolveStringAsync: id => Task.FromResult(StringTableDocument.Parse(File.ReadAllText(stringsPath)).ToDictionary().GetValueOrDefault(id)),
                saveStringsAsync: (updates, removals) =>
                {
                    File.WriteAllText(stringsPath, StringTableDocument.Parse(File.ReadAllText(stringsPath)).Apply(updates, removals, StringTableSection.Powers));
                    if (first)
                    {
                        first = false;
                        if (failFirstWrite) throw new IOException("simulated string write failure");
                        Raw(view!).Text = $"<power name='NewA' type='GodPower'><displaynameid>{oldId}</displaynameid><future version='later'/></power>";
                    }
                    return Task.CompletedTask;
                });
            try
            {
                view.SetModifiedMode(true); await view.SelectPowerAsync("A"); await PowerXmlTestHelpers.UnlockAsync(view);
                Raw(view).Text = $"<power name='NewA' type='GodPower'><displaynameid>{oldId}</displaynameid><future version='first'/></power>";
                Assert.Equal(!failFirstWrite, await view.SaveAsync());
                Assert.False(Raw(view).IsReadOnly); Assert.True(view.IsDirty);
                if (failFirstWrite)
                { Assert.Equal(before, File.ReadAllBytes(path)); Assert.Equal(stringsBefore, File.ReadAllBytes(stringsPath)); }
                else
                { Assert.Equal("first", (string?)PowerCatalog.Find(XDocument.Load(path), "NewA")!.Element("future")!.Attribute("version")); Assert.Contains("later", Raw(view).Text); }
                Assert.True(await view.SaveAsync());
                var saved = PowerCatalog.Find(XDocument.Load(path), "NewA")!;
                Assert.Equal(newId, saved.Element("displaynameid")!.Value);
                Assert.Equal(failFirstWrite ? "first" : "later", (string?)saved.Element("future")!.Attribute("version"));
                var strings = StringTableDocument.Parse(File.ReadAllText(stringsPath)).ToDictionary();
                Assert.Equal("Display", strings[newId]); Assert.DoesNotContain(oldId, strings.Keys);
                Assert.True(Raw(view).IsReadOnly); Assert.False(view.IsDirty);
            }
            finally { view.ReleasePowerXmlEditing(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MalformedXmlShowsBlockingPopupAndCanReachHostDiscard(bool pinned)
    {
        using var fixture = new BaselineSafetyFixture();
        var path = fixture.Workspace.WriteText("TestMod/game/data/gameplay/powers_mods.xml", Initial);
        var bytes = File.ReadAllBytes(path);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var host = fixture.CreateHost(false);
            try
            {
                host.Call("EnsurePowerView");
                var view = (PowerEditorView)typeof(ProtoEditorWindow).GetField("_powerView", Private)!.GetValue(host)!;
                var kind = Enum.Parse(typeof(ProtoEditorWindow).GetNestedType("EditorEntityKind", BindingFlags.NonPublic)!, "Powers");
                if (typeof(ProtoEditorWindow).GetField("_mainDocumentTab", Private)!.GetValue(host) == null) host.Call("InitializeDocumentTabs");
                host.Set("_activeEntityKind", kind); host.Set("_documentTabActivationInProgress", true); host.Set("_suppressUnitListSelectionChange", true);
                ((Avalonia.Controls.Primitives.TabStrip)typeof(ProtoEditorWindow).GetField("_unitTabs", Private)!.GetValue(host)!).SelectedIndex = 1;
                host.Call("SetMainDocumentTarget", kind, true, "A"); host.GodHost.Content = view;
                view.SetModifiedMode(true); await view.SelectPowerAsync("A"); host.Show(); Dispatcher.UIThread.RunJobs();
                host.Set("_documentTabActivationInProgress", false); host.Set("_suppressUnitListSelectionChange", false);
                object? tab = null;
                if (pinned)
                {
                    tab = host.Call("CreateDocumentTab", false, kind, true, "A")!;
                    tab.GetType().GetProperty("SavedElement")!.SetValue(tab, view.LoadSavedPowerElement("A"));
                    ((IList)typeof(ProtoEditorWindow).GetField("_openDocumentTabs", Private)!.GetValue(host)!).Add(tab);
                    ((IList)typeof(ProtoEditorWindow).GetField("_documentTabItems", Private)!.GetValue(host)!).Add(tab.GetType().GetProperty("TabItem")!.GetValue(tab));
                    host.Set("_activeDocumentTab", tab);
                }
                await PowerXmlTestHelpers.UnlockAsync(view); Raw(view).Text = "<power name='A'>";
                var saving = view.SaveAsync();
                var error = await Popup(host); Assert.True(error.PromptIsError); Assert.Contains("XML", error.PromptTitle); error.Close();
                Assert.False(await saving.WaitAsync(TimeSpan.FromSeconds(10)));
                Assert.Equal("<power name='A'>", Raw(view).Text); Assert.Equal(bytes, File.ReadAllBytes(path));
                var leaving = pinned ? (Task)host.Call("CloseDocumentTabAsync", tab!)! : (Task)host.Call("ConfirmMainDocumentReplacementAsync", kind, true, "B")!;
                var discard = await Popup(host); Assert.True(discard.PromptIsConfirm); Assert.Contains("Discard", discard.FindControl<Button>("_confirmButton")!.Content!.ToString());
                discard.FindControl<Button>("_confirmButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await leaving.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.False(view.IsDirty); Assert.Equal(bytes, File.ReadAllBytes(path));
                await view.SelectPowerAsync("B"); await view.SelectPowerAsync("A");
                Assert.True(Raw(view).IsReadOnly); Assert.False(view.IsDirty);
            }
            finally { foreach (var child in host.OwnedWindows.ToArray()) child.Close(); host.DisposeFixtureWindow(); }
        });
    }

    private static async Task<Prompt> Popup(Window owner)
    {
        for (var i = 0; i < 300; i++)
        { if (owner.OwnedWindows.OfType<Prompt>().FirstOrDefault() is { } popup) return popup; await Task.Delay(10); }
        throw new TimeoutException("Expected Power XML popup.");
    }

    [Fact]
    public void ExternalReloadRefreshesAnUnchangedUnlockedRecordWhileRetainingOtherEdits()
        => WithEditor(async (view, path) =>
        {
            var owner = new Window { Content = view }; owner.Show();
            try
            {
                await PowerXmlTestHelpers.UnlockAsync(view);
                await view.SelectPowerAsync("B"); await PowerXmlTestHelpers.UnlockAsync(view);
                Raw(view).Text = "<power name='B' type='GodPower'><activetime>23</activetime><future sibling='yes'/></power>";
                Assert.True(view.IsDirty);
                var external = XDocument.Parse(Initial);
                PowerCatalog.Find(external, "A")!.Element("activetime")!.Value = "99";
                external.Save(path);
                var saving = view.SaveAsync();
                var reload = await Popup(owner); Assert.True(reload.PromptIsConfirm);
                reload.FindControl<Button>("_confirmButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.False(await saving.WaitAsync(TimeSpan.FromSeconds(10)));
                await view.SelectPowerAsync("A"); Assert.True(Raw(view).IsReadOnly); Assert.Contains("99", Raw(view).Text);
                Assert.True(view.IsDirty); Assert.True(await view.SaveAsync());
                Assert.Equal("99", PowerCatalog.Find(XDocument.Load(path), "A")!.Element("activetime")!.Value);
                Assert.Equal("23", PowerCatalog.Find(XDocument.Load(path), "B")!.Element("activetime")!.Value);
            }
            finally { foreach (var popup in owner.OwnedWindows.ToArray()) popup.Close(); owner.Close(); }
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GlobalSaveUsesPowerRenamePipelineAndExcludesOwnerFromReferenceWrites(bool rename)
    {
        using var fixture = new BaselineSafetyFixture();
        File.WriteAllText(fixture.ProtoPath, "<protomods><unit name='Caster'><castpower>A</castpower></unit></protomods>");
        var path = fixture.Workspace.WriteText("TestMod/game/data/gameplay/powers_mods.xml", Initial);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var host = fixture.CreateHost(false);
            try
            {
                host.Call("EnsurePowerView");
                var view = (PowerEditorView)typeof(ProtoEditorWindow).GetField("_powerView", Private)!.GetValue(host)!;
                host.Show(); view.SetModifiedMode(true); await view.SelectPowerAsync("A");
                await PowerXmlTestHelpers.UnlockAsync(view);
                var name = rename ? "NewA" : "A";
                Raw(view).Text = $"<power name='{name}' type='GodPower'><activetime>18</activetime><future/></power>";
                host.Call("Save_Click", null!, new RoutedEventArgs());
                var errors = new List<string>();
                for (var i = 0; (bool)typeof(ProtoEditorWindow).GetField("_isUnitSaveInProgress", Private)!.GetValue(host)! && i < 1000; i++)
                {
                    foreach (var prompt in host.OwnedWindows.OfType<Prompt>().ToArray()) { errors.Add(prompt.PromptTitle + ": " + prompt.PromptText); prompt.Close(); }
                    await Task.Delay(10);
                }
                Assert.False((bool)typeof(ProtoEditorWindow).GetField("_isUnitSaveInProgress", Private)!.GetValue(host)!);
                Assert.Empty(errors);
                Assert.Equal("18", PowerCatalog.Find(XDocument.Load(path), name)!.Element("activetime")!.Value);
                Assert.Equal(name, XDocument.Load(fixture.ProtoPath).Root!.Element("unit")!.Element("castpower")!.Value);
                Assert.True(Raw(view).IsReadOnly); Assert.False(view.IsDirty);
            }
            finally { foreach (var prompt in host.OwnedWindows.ToArray()) prompt.Close(); host.DisposeFixtureWindow(); }
        });
    }
}
