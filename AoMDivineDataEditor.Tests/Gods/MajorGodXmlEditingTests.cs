using System.Collections;
using System.Reflection;
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

public sealed class MajorGodXmlEditingTests : IDisposable
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string Initial = "<civsmods future='root'><civ future='a'><name>A</name><statsid>SP</statsid><key>A</key><startingresources><food>10</food></startingresources><!--keep--><future keep='yes'/></civ><civ><name>B</name><statsid>SP</statsid><future sibling='yes'/></civ></civsmods>";
    private static TextEditor Raw(MajorGodEditorView view) => view.FindControl<TextEditor>("_rawXmlEditor")!;
    private static T Field<T>(object view, string name) => (T)view.GetType().GetField(name, Private)!.GetValue(view)!;
    public void Dispose() => AvaloniaTestHost.CollectUiGarbage();

    private static void WithEditor(Func<MajorGodEditorView, string, Task> check)
    {
        using var workspace = new TestWorkspace(nameof(MajorGodXmlEditingTests));
        var path = workspace.WriteText("major_gods_mods.xml", Initial);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var view = new MajorGodEditorView([], path, cultureNames: ["Greek"], protoUnitNames: ["BaseUnit"], technologyNames: ["TechA"]);
            try
            {
                view.SetModifiedMode(true); view.SelectMajorGod("A");
                await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
                await check(view, path);
            }
            finally { view.ReleaseMajorGodXmlEditing(); }
        });
    }

    [Fact]
    public void CleanUnlockIsNotDirtyAndSaveRelocksWithoutRewriting()
        => WithEditor(async (view, path) =>
        {
            var before = File.ReadAllBytes(path);
            Assert.True(Raw(view).IsReadOnly); Assert.True(Raw(view).IsEnabled);
            await MajorGodXmlTestHelpers.UnlockAsync(view);
            Assert.False(view.IsDirty); Assert.False(view.FindControl<Grid>("_majorGodStructuredForm")!.IsEnabled);
            Assert.True(await view.SaveAsync()); Assert.False(view.IsDirty);
            Assert.True(Raw(view).IsReadOnly); Assert.True(view.FindControl<Grid>("_majorGodStructuredForm")!.IsEnabled);
            Assert.Equal(before, File.ReadAllBytes(path));
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OriginalAndSharedDataStayCopyableAndCannotUnlock(bool shared)
    {
        AvaloniaTestHost.Run(() =>
        {
            var view = new MajorGodEditorView([new MajorGodDefinition("Original", true, XElement.Parse("<civ><name>Original</name></civ>"))], null,
                baseSharedData: XElement.Parse("<civs><defaultpoplimit>100</defaultpoplimit></civs>"));
            try
            {
                view.SelectMajorGod(shared ? MajorGodCatalog.SharedDataDisplayName : "Original");
                Assert.True(Raw(view).IsReadOnly); Assert.True(Raw(view).IsEnabled); Assert.True(Raw(view).Focusable);
                Assert.False(view.FindControl<Button>("_editXmlButton")!.IsVisible); Assert.False(view.IsDirty);
            }
            finally { view.ReleaseMajorGodXmlEditing(); }
        });
    }

    [Fact]
    public void SaveReopensWithUnknownContentCommentsAndSiblingPreserved()
        => WithEditor(async (view, path) =>
        {
            await MajorGodXmlTestHelpers.UnlockAsync(view);
            Raw(view).Text = "<civ future='edited'><name>A</name><key>Z</key><startingresources><food>17</food></startingresources><!--keep--><future keep='yes'><nested/></future></civ>";
            Assert.True(view.IsDirty); Assert.True(view.IsMajorGodDirty("A"));
            Assert.True(await view.SaveAsync());
            view.SelectMajorGod("B"); view.SelectMajorGod("A");
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
            var document = XDocument.Load(path); var entry = MajorGodCatalog.Find(document, "A")!;
            Assert.Equal("17", entry.Element("startingresources")!.Element("food")!.Value);
            Assert.Equal("edited", (string?)entry.Attribute("future")); Assert.NotNull(entry.Element("future")!.Element("nested"));
            Assert.Contains(entry.Nodes().OfType<XComment>(), comment => comment.Value == "keep");
            Assert.True(XNode.DeepEquals(MajorGodCatalog.Find(XDocument.Parse(Initial), "B"), MajorGodCatalog.Find(document, "B")));
            Assert.Equal("Z", view.FindControl<TextBox>("_keyBox")!.Text);
            Assert.True(Raw(view).IsReadOnly); Assert.False(view.IsDirty);
        });

    [Theory]
    [InlineData("<civ>")]
    [InlineData("<power name='A'/>")]
    [InlineData("<civ xmlns='future'><name>A</name></civ>")]
    [InlineData("<civ><name>B</name></civ>")]
    [InlineData("<civ><name/></civ>")]
    [InlineData("<civ><name>A</name><key>AA</key></civ>")]
    [InlineData("<civ><name>A</name><culture>Unknown</culture></civ>")]
    [InlineData("<civ><name>A</name><agetech age='ArchaicAge'><tech>Missing</tech></agetech></civ>")]
    [InlineData("<civ><name>A</name><buildingefficiency>-1</buildingefficiency></civ>")]
    [InlineData("<civ><name>A</name><startingresources><food>-1</food></startingresources></civ>")]
    [InlineData("<civ><name>A</name><startingunits><unit count='1'>Unknown</unit></startingunits></civ>")]
    public void InvalidRawXmlKeepsExactDiskAndBufferAndCanBeDiscarded(string text)
        => WithEditor(async (view, path) =>
        {
            var before = File.ReadAllBytes(path);
            await MajorGodXmlTestHelpers.UnlockAsync(view); Raw(view).Text = text;
            Assert.False(await view.SaveAsync()); Assert.Equal(text, Raw(view).Text); Assert.False(Raw(view).IsReadOnly);
            Assert.DoesNotContain(view.GetLogicalDescendants().OfType<Control>(), field => field.Classes.Contains("invalid-field"));
            Assert.Equal(before, File.ReadAllBytes(path)); Assert.True(await view.CommitCurrentMajorGodAsync());
            view.DiscardMajorGodChanges("A", MajorGodCatalog.Find(XDocument.Parse(Initial), "A"));
            view.SelectMajorGod("B"); view.SelectMajorGod("A");
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
            Assert.True(Raw(view).IsReadOnly); Assert.False(view.IsDirty); Assert.False(view.IsMajorGodDirty("A"));
            Assert.Equal(before, File.ReadAllBytes(path));
        });

    [Fact]
    public void OnlyNameIsRequiredAndEmptyOptionalFieldsAreOmitted()
        => WithEditor(async (view, path) =>
        {
            await MajorGodXmlTestHelpers.UnlockAsync(view);
            Raw(view).Text = "<civ><name>A</name><key/><culture/><icon/><portrait/><titleid/></civ>";
            Assert.True(await view.SaveAsync());
            var entry = MajorGodCatalog.Find(XDocument.Load(path), "A")!;
            Assert.Equal(["name", "statsid"], entry.Elements().Select(element => element.Name.LocalName));
        });

    [Fact]
    public void IndependentRecordBuffersSaveTogetherAndInvalidInactiveXmlBlocksWrites()
        => WithEditor(async (view, path) =>
        {
            var before = File.ReadAllBytes(path);
            await MajorGodXmlTestHelpers.UnlockAsync(view); Raw(view).Text = "<civ><name>A</name>";
            view.SelectMajorGod("B"); Assert.True(Raw(view).IsReadOnly);
            await MajorGodXmlTestHelpers.UnlockAsync(view); Raw(view).Text = "<civ><name>B</name><key>C</key></civ>";
            Assert.False(await view.SaveAsync()); Assert.Equal(before, File.ReadAllBytes(path));
            view.SelectMajorGod("A"); Assert.Equal("<civ><name>A</name>", Raw(view).Text); Assert.False(Raw(view).IsReadOnly);
            Raw(view).Text = "<civ><name>A</name><key>D</key></civ>";
            Assert.True(await view.SaveAsync()); Assert.False(view.IsDirty);
            Assert.Equal("D", MajorGodCatalog.Find(XDocument.Load(path), "A")!.Element("key")!.Value);
            Assert.Equal("C", MajorGodCatalog.Find(XDocument.Load(path), "B")!.Element("key")!.Value);
            view.SelectMajorGod("B"); Assert.True(Raw(view).IsReadOnly);
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RawRenameStringTransactionRetainsFailedOrNewerDraftForRetry(bool failFirstWrite)
    {
        using var workspace = new TestWorkspace(nameof(RawRenameStringTransactionRetainsFailedOrNewerDraftForRetry));
        var oldId = MajorGodCatalog.BuildStringId("A", "displaynameid");
        var newId = MajorGodCatalog.BuildStringId("NewA", "displaynameid");
        var path = workspace.WriteText("TestMod/game/data/gameplay/major_gods_mods.xml", $"<civsmods><civ><name>A</name><displaynameid>{oldId}</displaynameid></civ></civsmods>");
        var stringsPath = workspace.WriteText("TestMod/game/data/strings/English/stringmods.txt", $"ID = \"{oldId}\" ; Str = \"Display\"\r\n");
        var before = File.ReadAllBytes(path); var stringsBefore = File.ReadAllBytes(stringsPath);
        AvaloniaTestHost.RunAsync(async () =>
        {
            MajorGodEditorView? view = null; var first = true;
            view = new MajorGodEditorView([], path, modStringsPath: stringsPath,
                resolveStringAsync: id => Task.FromResult(StringTableDocument.Parse(File.ReadAllText(stringsPath)).ToDictionary().GetValueOrDefault(id)),
                saveStringsAsync: (updates, removals) =>
                {
                    File.WriteAllText(stringsPath, StringTableDocument.Parse(File.ReadAllText(stringsPath)).Apply(updates, removals, StringTableSection.MajorGods));
                    if (first)
                    {
                        first = false;
                        if (failFirstWrite) throw new IOException("simulated string write failure");
                        Raw(view!).Text = $"<civ><name>NewA</name><displaynameid>{oldId}</displaynameid><future version='later'/></civ>";
                    }
                    return Task.CompletedTask;
                });
            try
            {
                view.SetModifiedMode(true); view.SelectMajorGod("A"); await MajorGodXmlTestHelpers.UnlockAsync(view);
                Raw(view).Text = $"<civ><name>NewA</name><displaynameid>{oldId}</displaynameid><future version='first'/></civ>";
                Assert.Equal(!failFirstWrite, await view.SaveAsync()); Assert.False(Raw(view).IsReadOnly); Assert.True(view.IsDirty);
                if (failFirstWrite)
                { Assert.Equal(before, File.ReadAllBytes(path)); Assert.Equal(stringsBefore, File.ReadAllBytes(stringsPath)); }
                else
                { Assert.Equal("first", (string?)MajorGodCatalog.Find(XDocument.Load(path), "NewA")!.Element("future")!.Attribute("version")); Assert.Contains("later", Raw(view).Text); }
                Assert.True(await view.SaveAsync());
                var saved = MajorGodCatalog.Find(XDocument.Load(path), "NewA")!;
                Assert.Equal(newId, saved.Element("displaynameid")!.Value);
                Assert.Equal(failFirstWrite ? "first" : "later", (string?)saved.Element("future")!.Attribute("version"));
                var strings = StringTableDocument.Parse(File.ReadAllText(stringsPath)).ToDictionary();
                Assert.Equal("Display", strings[newId]); Assert.DoesNotContain(oldId, strings.Keys);
                Assert.True(Raw(view).IsReadOnly); Assert.False(view.IsDirty);
            }
            finally { view.ReleaseMajorGodXmlEditing(); }
        });
    }

    [Fact]
    public void DiscardInvalidatesRawRenameAwaitingStringLookup()
    {
        using var workspace = new TestWorkspace(nameof(DiscardInvalidatesRawRenameAwaitingStringLookup));
        var id = MajorGodCatalog.BuildStringId("A", "displaynameid");
        var path = workspace.WriteText("major_gods_mods.xml", $"<civsmods><civ><name>A</name><displaynameid>{id}</displaynameid></civ><civ><name>B</name></civ></civsmods>");
        var stringsPath = workspace.WriteText("stringmods.txt", $"ID = \"{id}\" ; Str = \"Display\"\r\n");
        var before = File.ReadAllBytes(path); var delayed = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously); var delay = false;
        AvaloniaTestHost.RunAsync(async () =>
        {
            var view = new MajorGodEditorView([], path, modStringsPath: stringsPath,
                resolveStringAsync: _ => delay ? delayed.Task : Task.FromResult<string?>("Display"));
            try
            {
                view.SetModifiedMode(true); view.SelectMajorGod("A"); await MajorGodXmlTestHelpers.UnlockAsync(view);
                Raw(view).Text = $"<civ><name>NewA</name><displaynameid>{id}</displaynameid></civ>"; delay = true;
                var saving = view.SaveAsync(); Assert.False(saving.IsCompleted);
                delay = false; view.DiscardMajorGodChanges("A", view.LoadSavedMajorGodElement("A")); view.SelectMajorGod("B");
                delayed.SetResult("Display"); Assert.False(await saving.WaitAsync(TimeSpan.FromSeconds(5)));
                Assert.Equal("B", view.CurrentMajorGodName); Assert.False(view.IsDirty);
                Assert.Empty(Field<HashSet<string>>(view, "_dirtyNames")); Assert.Empty(Field<Dictionary<string, string>>(view, "_pendingStringUpdates"));
                Assert.Equal(before, File.ReadAllBytes(path)); Assert.DoesNotContain("NewA", view.GetMajorGodNames(true));
                Assert.True(await view.CommitCurrentMajorGodAsync()); Assert.False(view.IsDirty);
            }
            finally { delayed.TrySetResult("Display"); view.ReleaseMajorGodXmlEditing(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExternalReloadOrCancelPreservesTheCorrectRawDraft(bool reload)
        => WithEditor(async (view, path) =>
        {
            var owner = new Window { Content = view }; owner.Show();
            try
            {
                await MajorGodXmlTestHelpers.UnlockAsync(view); Raw(view).Text = "<civ><name>A</name><key>Z</key></civ>";
                var external = XDocument.Parse(Initial); MajorGodCatalog.Find(external, "A")!.Element("key")!.Value = "C"; external.Save(path);
                var externalBytes = File.ReadAllBytes(path);
                var saving = view.SaveAsync(); var prompt = await Popup(owner);
                Assert.True(prompt.PromptIsConfirm); Assert.Equal("Major Gods changed outside the editor", prompt.PromptTitle);
                if (reload) prompt.FindControl<Button>("_confirmButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); else prompt.Close();
                Assert.False(await saving.WaitAsync(TimeSpan.FromSeconds(10))); Assert.Equal(externalBytes, File.ReadAllBytes(path));
                Assert.Equal(reload, Raw(view).IsReadOnly); Assert.Equal(!reload, view.IsDirty);
                Assert.Contains(reload ? "<key>C</key>" : "<key>Z</key>", Raw(view).Text);
            }
            finally { foreach (var popup in owner.OwnedWindows.ToArray()) popup.Close(); owner.Close(); }
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MalformedXmlCanReachMainOrPinnedHostDiscard(bool pinned)
    {
        using var fixture = new BaselineSafetyFixture(); File.WriteAllText(fixture.GodPath, Initial);
        var before = File.ReadAllBytes(fixture.GodPath);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var host = fixture.CreateHost(false);
            try
            {
                var view = host.God!;
                var kind = Enum.Parse(typeof(ProtoEditorWindow).GetNestedType("EditorEntityKind", BindingFlags.NonPublic)!, "MajorGods");
                if (typeof(ProtoEditorWindow).GetField("_mainDocumentTab", Private)!.GetValue(host) == null) host.Call("InitializeDocumentTabs");
                host.Set("_activeEntityKind", kind); host.Set("_documentTabActivationInProgress", true); host.Set("_suppressUnitListSelectionChange", true);
                ((Avalonia.Controls.Primitives.TabStrip)typeof(ProtoEditorWindow).GetField("_unitTabs", Private)!.GetValue(host)!).SelectedIndex = 1;
                host.Call("SetMainDocumentTarget", kind, true, "A"); host.GodHost.Content = view;
                view.SetModifiedMode(true); view.SelectMajorGod("A"); host.Show(); Dispatcher.UIThread.RunJobs();
                host.Set("_documentTabActivationInProgress", false); host.Set("_suppressUnitListSelectionChange", false);
                object? tab = null;
                if (pinned)
                {
                    tab = host.Call("CreateDocumentTab", false, kind, true, "A")!;
                    tab.GetType().GetProperty("SavedElement")!.SetValue(tab, view.LoadSavedMajorGodElement("A"));
                    ((IList)typeof(ProtoEditorWindow).GetField("_openDocumentTabs", Private)!.GetValue(host)!).Add(tab);
                    ((IList)typeof(ProtoEditorWindow).GetField("_documentTabItems", Private)!.GetValue(host)!).Add(tab.GetType().GetProperty("TabItem")!.GetValue(tab));
                    host.Set("_activeDocumentTab", tab);
                }
                await MajorGodXmlTestHelpers.UnlockAsync(view); Raw(view).Text = "<civ>";
                var leaving = pinned ? (Task)host.Call("CloseDocumentTabAsync", tab!)! : (Task)host.Call("ConfirmMainDocumentReplacementAsync", kind, true, "B")!;
                var discard = await Popup(host); Assert.True(discard.PromptIsConfirm);
                Assert.Contains("Discard", discard.FindControl<Button>("_confirmButton")!.Content!.ToString());
                discard.FindControl<Button>("_confirmButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await leaving.WaitAsync(TimeSpan.FromSeconds(10)); Assert.False(view.IsDirty); Assert.Equal(before, File.ReadAllBytes(fixture.GodPath));
                view.SelectMajorGod("B"); view.SelectMajorGod("A");
                await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
                Assert.True(Raw(view).IsReadOnly); Assert.False(view.IsDirty); Assert.True(await view.CommitCurrentMajorGodAsync());
            }
            finally { foreach (var child in host.OwnedWindows.ToArray()) child.Close(); host.DisposeFixtureWindow(); }
        });
    }

    private static async Task<Prompt> Popup(Window owner)
    {
        for (var i = 0; i < 300; i++)
        { if (owner.OwnedWindows.OfType<Prompt>().FirstOrDefault() is { } popup) return popup; await Task.Delay(10); }
        throw new TimeoutException("Expected Major God XML popup.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GlobalSaveRenamesReferencesOrBlocksMalformedDraftBeforeWriting(bool invalid)
    {
        using var fixture = new BaselineSafetyFixture();
        File.WriteAllText(fixture.GodPath, Initial);
        var reference = fixture.Workspace.WriteText("TestMod/game/data/gameplay/techtree_mods.xml", "<techtreemods><tech name='Ref'><prereqs><civilization><civName>A</civName></civilization></prereqs></tech></techtreemods>");
        var before = new[] { fixture.GodPath, reference }.ToDictionary(path => path, File.ReadAllBytes);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var host = fixture.CreateHost(false);
            try
            {
                var view = host.God!; host.Show(); view.SelectMajorGod("A"); await MajorGodXmlTestHelpers.UnlockAsync(view);
                Raw(view).Text = invalid ? "<civ>" : "<civ><name>NewA</name><key>Z</key></civ>";
                host.Call("Save_Click", null!, new RoutedEventArgs());
                var errors = new List<string>();
                for (var i = 0; (bool)typeof(ProtoEditorWindow).GetField("_isUnitSaveInProgress", Private)!.GetValue(host)! && i < 1000; i++)
                {
                    foreach (var popup in host.OwnedWindows.OfType<Prompt>().ToArray()) { errors.Add(popup.PromptTitle + ": " + popup.PromptText); popup.Close(); }
                    await Task.Delay(10);
                }
                Assert.False((bool)typeof(ProtoEditorWindow).GetField("_isUnitSaveInProgress", Private)!.GetValue(host)!);
                if (invalid)
                {
                    Assert.NotEmpty(errors); Assert.False(Raw(view).IsReadOnly); Assert.Equal("<civ>", Raw(view).Text);
                    foreach (var pair in before) Assert.Equal(pair.Value, File.ReadAllBytes(pair.Key));
                }
                else
                {
                    Assert.Empty(errors); Assert.Null(MajorGodCatalog.Find(XDocument.Load(fixture.GodPath), "A"));
                    Assert.Equal("Z", MajorGodCatalog.Find(XDocument.Load(fixture.GodPath), "NewA")!.Element("key")!.Value);
                    Assert.Equal("NewA", XDocument.Load(reference).Descendants("civName").Single().Value);
                    Assert.True(Raw(view).IsReadOnly); Assert.False(view.IsDirty);
                }
            }
            finally { foreach (var child in host.OwnedWindows.ToArray()) child.Close(); host.DisposeFixtureWindow(); }
        });
    }
}
