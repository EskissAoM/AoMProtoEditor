using System.Xml.Linq;
using System.Collections;
using System.Reflection;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.GameData;
using AoMDivineDataEditor.Windows;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using AvaloniaEdit;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed partial class TechnologyEditorWorkflowTests
{
    private const string XmlInitial = "<techtreemods future='root'><tech name='A'><researchpoints>1</researchpoints><!--keep--><future keep='yes'/></tech><tech name='B'><future sibling='yes'/></tech></techtreemods>";
    private static TextEditor Raw(TechnologyEditorView editor) => GetPrivateField<TextEditor>(editor, "_xmlPreview");
    private static Task AwaitTechnologyBuild(TechnologyEditorView editor)
        => (Task)typeof(TechnologyEditorView).GetMethod("AwaitActiveEditorBuildCompletionAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(editor, null)!;
    private static async Task UnlockTechnologyXml(TechnologyEditorView editor, string name = "A")
    {
        editor.SetModifiedMode(true); editor.SelectTechnology(name);
        await AwaitTechnologyBuild(editor);
        GetPrivateField<Button>(editor, "_editXmlButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        for (var i = 0; Raw(editor).IsReadOnly && i < 200; i++) await Task.Delay(10);
        Assert.False(Raw(editor).IsReadOnly);
    }
    private static async Task InXmlWindow(TechnologyEditorView editor, Func<Window, Task> check)
    {
        var owner = new Window { Content = editor, Width = 1100, Height = 700 }; owner.Show();
        try { await check(owner); }
        finally { foreach (var child in owner.OwnedWindows.ToArray()) child.Close(); owner.Close(); }
    }
    private static async Task<Prompt> XmlPopup(Window owner)
    {
        for (var i = 0; i < 200; i++)
        {
            if (owner.OwnedWindows.OfType<Prompt>().FirstOrDefault() is { } popup) return popup;
            await Task.Delay(10);
        }
        throw new TimeoutException("Expected Technology XML popup.");
    }
    private static async Task<bool> SaveXmlChecked(TechnologyEditorView editor)
    {
        var saving = editor.SaveAsync();
        string? error = null;
        for (var i = 0; !saving.IsCompleted && i < 1000; i++)
        {
            if ((TopLevel.GetTopLevel(editor) as Window)?.OwnedWindows.OfType<Prompt>().FirstOrDefault() is { } popup)
            { error = popup.PromptTitle + ": " + popup.PromptText; popup.Close(); }
            if (!saving.IsCompleted) await Task.Delay(10);
        }
        var saved = await saving.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(saved, error);
        return saved;
    }

    [Fact]
    public void XmlEditSaveReopensAndPreservesUnknownContentAndSibling()
    {
        using var fixture = new TechnologyEditorFixture(XmlInitial);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var editor = fixture.CreateEditor();
            await InXmlWindow(editor, async _ =>
            {
                editor.SetModifiedMode(true); editor.SelectTechnology("A"); await AwaitTechnologyBuild(editor);
                Assert.True(Raw(editor).IsReadOnly); Assert.False(editor.IsDirty);
                await UnlockTechnologyXml(editor);
                Assert.False(editor.IsDirty);
                Assert.False(GetPrivateField<Grid>(editor, "_technologyForm").IsEnabled);
                Raw(editor).Text = "<tech name='A'><researchpoints>7</researchpoints><!--keep--><future keep='yes'><new-data/></future></tech>";
                Assert.True(editor.IsDirty); Assert.True(editor.IsTechnologyDirty("A"));
                Assert.True(await SaveXmlChecked(editor));
                Assert.True(Raw(editor).IsReadOnly); Assert.False(editor.IsDirty);
                Assert.True(GetPrivateField<Grid>(editor, "_technologyForm").IsEnabled);
                var disk = XDocument.Load(fixture.TechtreePath);
                Assert.Equal("7", FindTechnology(disk, "A").Element("researchpoints")!.Value);
                Assert.NotNull(FindTechnology(disk, "A").Element("future")!.Element("new-data"));
                Assert.Contains(FindTechnology(disk, "A").Nodes().OfType<XComment>(), comment => comment.Value == "keep");
                Assert.True(XNode.DeepEquals(FindTechnology(XDocument.Parse(XmlInitial), "B"), FindTechnology(disk, "B")));
                Assert.Equal("root", (string?)disk.Root!.Attribute("future"));
                var reopened = fixture.CreateEditor(); reopened.SetModifiedMode(true); reopened.SelectTechnology("A"); await AwaitTechnologyBuild(reopened);
                Assert.Contains("<researchpoints>7</researchpoints>", Raw(reopened).Text);
                Assert.True(Raw(reopened).IsReadOnly);
            });
        });
    }

    [Theory]
    [InlineData("<tech>")]
    [InlineData("<unit name='A'/>")]
    [InlineData("<tech name='B'/>")]
    [InlineData("<tech name='A'><researchpoints>-1</researchpoints></tech>")]
    [InlineData("<tech name='bad name'/>")]
    [InlineData("<tech name='A'/><tech name='Other'/>")]
    public void XmlInvalidSavePreservesExactDiskAndDraftAndAllowsDiscard(string xml)
    {
        using var fixture = new TechnologyEditorFixture(XmlInitial);
        var before = File.ReadAllBytes(fixture.TechtreePath);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var editor = fixture.CreateEditor();
            await InXmlWindow(editor, async owner =>
            {
                await UnlockTechnologyXml(editor); Raw(editor).Text = xml;
                var saving = editor.SaveAsync(); var popup = await XmlPopup(owner);
                Assert.True(popup.PromptIsError); Assert.NotEmpty(popup.PromptText); popup.Close();
                Assert.False(await saving.WaitAsync(TimeSpan.FromSeconds(10)));
                Assert.Equal(before, File.ReadAllBytes(fixture.TechtreePath));
                Assert.Equal(xml, Raw(editor).Text); Assert.False(Raw(editor).IsReadOnly); Assert.True(editor.IsDirty);
                editor.DiscardTechnologyChanges("A", editor.LoadSavedTechnologyElement("A"));
                editor.SelectTechnology(null); editor.SelectTechnology("A"); await AwaitTechnologyBuild(editor);
                Assert.True(Raw(editor).IsReadOnly); Assert.False(editor.IsDirty);
                Assert.Equal(before, File.ReadAllBytes(fixture.TechtreePath));
            });
        });
    }

    [Fact]
    public void XmlUnselectedDraftsAreSavedAndInvalidBuffersSurviveNavigation()
    {
        using var fixture = new TechnologyEditorFixture(XmlInitial);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var editor = fixture.CreateEditor();
            await InXmlWindow(editor, async _ =>
            {
                await UnlockTechnologyXml(editor); Raw(editor).Text = "<tech name='A'><future changed='yes'/></tech>";
                var snapshot = editor.CreateModifiedDocumentSnapshot()!;
                Assert.Equal("yes", (string?)FindTechnology(snapshot, "A").Element("future")!.Attribute("changed"));
                Raw(editor).Text = "<tech name='A'><broken>";
                Assert.True(await editor.CommitCurrentTechnologyAsync());
                editor.SelectTechnology("B"); await AwaitTechnologyBuild(editor); Assert.True(Raw(editor).IsReadOnly);
                editor.SelectTechnology("A"); await AwaitTechnologyBuild(editor);
                Assert.Equal("<tech name='A'><broken>", Raw(editor).Text); Assert.False(Raw(editor).IsReadOnly);
                Assert.Throws<InvalidDataException>(() => editor.CreateModifiedDocumentSnapshot());
                Raw(editor).Text = "<tech name='A'><researchpoints>8</researchpoints><future keep='yes'/></tech>";
                editor.SelectTechnology("B"); await AwaitTechnologyBuild(editor);
                Assert.True(await SaveXmlChecked(editor));
                Assert.Equal("B", editor.CurrentTechnologyName);
                Assert.Equal("8", FindTechnology(XDocument.Load(fixture.TechtreePath), "A").Element("researchpoints")!.Value);
                Assert.False(editor.IsDirty);
            });
        });
    }

    [Fact]
    public void XmlRawRenameUsesLocalExternalReferenceAndOwnedStringLifecycle()
    {
        using var fixture = new TechnologyEditorFixture("<techtreemods><tech name='A'><displaynameid>STR_TECH_A_NAME</displaynameid><future keep='yes'/></tech><tech name='Dependent'><prereqs><techstatus status='Active'>A</techstatus></prereqs></tech></techtreemods>",
            "ID = \"STR_TECH_A_NAME\" ; Str = \"Visible name\"\r\n");
        var proto = Path.Combine(fixture.DirectoryPath, "proto_mods.xml");
        File.WriteAllText(proto, "<protomods><unit name='U'><tech>A</tech><future keep='yes'/></unit></protomods>");
        AvaloniaTestHost.RunAsync(async () =>
        {
            var editor = new TechnologyEditorView([], null, fixture.TechtreePath,
                resolveStringAsync: _ => Task.FromResult<string?>("Visible name"),
                saveStringsAsync: (updates, removals) =>
                {
                    File.WriteAllText(fixture.StringsPath, StringTableDocument.Parse(File.ReadAllText(fixture.StringsPath))
                        .Apply(updates, removals, StringTableSection.Technologies));
                    return Task.CompletedTask;
                }, modStringsPath: fixture.StringsPath,
                prepareTechnologyReferenceMigration: operations => TechnologyReferenceMigrationPlan.Prepare(
                    [new(proto, TechnologyReferenceDocumentKind.ProtoUnits, "ProtoUnits")], operations));
            await InXmlWindow(editor, async _ =>
            {
                await UnlockTechnologyXml(editor);
                Raw(editor).Text = "<tech name='Renamed'><displaynameid>STR_TECH_A_NAME</displaynameid><!--raw--><future keep='yes'/></tech>";
                Assert.True(await SaveXmlChecked(editor));
                var disk = XDocument.Load(fixture.TechtreePath);
                Assert.Equal("Renamed", editor.CurrentTechnologyName);
                Assert.Equal("Renamed", disk.Descendants("techstatus").Single().Value);
                Assert.Equal("STR_TECH_RENAMED_NAME", FindTechnology(disk, "Renamed").Element("displaynameid")!.Value);
                Assert.Equal("Renamed", XDocument.Load(proto).Descendants("tech").Single().Value);
                var strings = File.ReadAllText(fixture.StringsPath);
                Assert.Contains("STR_TECH_RENAMED_NAME", strings); Assert.DoesNotContain("STR_TECH_A_NAME", strings);
                Assert.Contains("Visible name", strings); Assert.True(Raw(editor).IsReadOnly); Assert.False(editor.IsDirty);
            });
        });
    }

    [Fact]
    public void XmlNewerTextSurvivesActualSaveAndCanBeSavedOnTheNextAttempt()
    {
        using var fixture = new TechnologyEditorFixture(XmlInitial);
        AvaloniaTestHost.RunAsync(async () =>
        {
            TechnologyEditorView editor = null!;
            editor = new TechnologyEditorView([], null, fixture.TechtreePath, technologySavedAsync: _ =>
            {
                Raw(editor).Text = "<tech name='A'><researchpoints>3</researchpoints></tech>";
                return Task.CompletedTask;
            });
            await InXmlWindow(editor, async _ =>
            {
                await UnlockTechnologyXml(editor); Raw(editor).Text = "<tech name='A'><researchpoints>2</researchpoints></tech>";
                Assert.True(await SaveXmlChecked(editor));
                Assert.Equal("2", FindTechnology(XDocument.Load(fixture.TechtreePath), "A").Element("researchpoints")!.Value);
                Assert.Contains("<researchpoints>3</researchpoints>", Raw(editor).Text);
                Assert.False(Raw(editor).IsReadOnly); Assert.True(editor.IsDirty);
                Assert.True(await SaveXmlChecked(editor));
                Assert.Equal("3", FindTechnology(XDocument.Load(fixture.TechtreePath), "A").Element("researchpoints")!.Value);
                Assert.True(Raw(editor).IsReadOnly); Assert.False(editor.IsDirty);
            });
        });
    }

    [Fact]
    public void XmlExternalCancelKeepsDraftAndAcceptedReloadRelocksTheExternalVersion()
    {
        using var fixture = new TechnologyEditorFixture(XmlInitial);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var editor = fixture.CreateEditor();
            await InXmlWindow(editor, async owner =>
            {
                await UnlockTechnologyXml(editor); Raw(editor).Text = "<tech name='A'><researchpoints>2</researchpoints></tech>";
                File.WriteAllText(fixture.TechtreePath, "<techtreemods><tech name='A'><researchpoints>11</researchpoints></tech></techtreemods>");
                var external = File.ReadAllBytes(fixture.TechtreePath);
                var saving = editor.SaveAsync(); var popup = await XmlPopup(owner); Assert.True(popup.PromptIsConfirm); popup.Close();
                Assert.False(await saving); Assert.Contains("<researchpoints>2</researchpoints>", Raw(editor).Text);
                Assert.False(Raw(editor).IsReadOnly); Assert.True(editor.IsDirty);
                saving = editor.SaveAsync(); popup = await XmlPopup(owner);
                popup.FindControl<Button>("_confirmButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.False(await saving);
                Assert.Equal(external, File.ReadAllBytes(fixture.TechtreePath));
                Assert.Contains("<researchpoints>11</researchpoints>", Raw(editor).Text);
                Assert.True(Raw(editor).IsReadOnly); Assert.False(editor.IsDirty);
            });
        });
    }

    [Fact]
    public void XmlOriginalTechnologyCannotUnlockAndRemainsCopyable()
    {
        using var fixture = new TechnologyEditorFixture(XmlInitial);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var editor = fixture.CreateEditor(originalDocuments: [XDocument.Parse("<techtree><tech name='Original'><future/></tech></techtree>")]);
            await InXmlWindow(editor, async _ =>
            {
                await UnlockTechnologyXml(editor); Raw(editor).Text = "<tech name='A'><researchpoints>4</researchpoints></tech>";
                editor.SetModifiedMode(false);
                editor.SelectTechnology("Original"); await AwaitTechnologyBuild(editor);
                Assert.True(Raw(editor).IsReadOnly); Assert.True(Raw(editor).IsEnabled); Assert.True(Raw(editor).Focusable);
                Assert.False(GetPrivateField<Button>(editor, "_editXmlButton").IsVisible);
                GetPrivateField<Button>(editor, "_editXmlButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(20); Assert.True(Raw(editor).IsReadOnly); Assert.True(editor.IsDirty);
                Raw(editor).SelectAll(); Assert.Contains("Original", Raw(editor).SelectedText);
                Assert.True(await SaveXmlChecked(editor)); Assert.False(editor.IsDirty);
                Assert.Equal("Original", editor.CurrentTechnologyName); Assert.Contains("Original", Raw(editor).Text);
                Assert.True(Raw(editor).IsReadOnly);
                Assert.Equal("4", FindTechnology(XDocument.Load(fixture.TechtreePath), "A").Element("researchpoints")!.Value);
            });
        });
    }

    [Fact]
    public void XmlWriteFailureRollsBackFilesAndKeepsTheRawRenameAvailableForDiscard()
    {
        using var fixture = new TechnologyEditorFixture("<techtreemods><tech name='A'><displaynameid>STR_TECH_A_NAME</displaynameid></tech></techtreemods>",
            "ID = \"STR_TECH_A_NAME\" ; Str = \"Name\"\r\n");
        var before = File.ReadAllBytes(fixture.TechtreePath); var strings = File.ReadAllBytes(fixture.StringsPath);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var editor = fixture.CreateEditor(saveStringsAsync: (_, _) =>
            { File.WriteAllText(fixture.StringsPath, "partial write"); throw new IOException("Injected string write failure"); });
            await InXmlWindow(editor, async owner =>
            {
                await UnlockTechnologyXml(editor);
                Raw(editor).Text = "<tech name='Renamed'><displaynameid>STR_TECH_A_NAME</displaynameid><future/></tech>";
                var saving = editor.SaveAsync(); var popup = await XmlPopup(owner);
                Assert.Contains("Injected string write failure", popup.PromptText); popup.Close();
                Assert.False(await saving); Assert.Equal(before, File.ReadAllBytes(fixture.TechtreePath));
                Assert.Equal(strings, File.ReadAllBytes(fixture.StringsPath));
                Assert.Contains("Renamed", Raw(editor).Text); Assert.False(Raw(editor).IsReadOnly); Assert.True(editor.IsDirty);
                editor.DiscardTechnologyChanges("Renamed", editor.LoadSavedTechnologyElement("A"));
                Assert.False(editor.IsDirty);
            });
        });
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void XmlRemovingAgeUpgradeUsesTheExistingLinkedMinorGodConfirmation(bool accept)
    {
        using var fixture = new TechnologyEditorFixture("<techtreemods><tech name='ClassicalAgeGod'><flag>AgeUpgrade</flag></tech></techtreemods>");
        File.WriteAllText(fixture.MinorGodsPath, "<minorgodsmods><ClassicalAgeGod future='keep'/></minorgodsmods>");
        var before = File.ReadAllBytes(fixture.TechtreePath); var gods = File.ReadAllBytes(fixture.MinorGodsPath);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var confirmations = 0;
            var editor = new TechnologyEditorView([], null, fixture.TechtreePath,
                hasLinkedMinorGod: name => name == "ClassicalAgeGod",
                confirmLinkedMinorGodRemovalAsync: (_, name) => { Assert.Equal("ClassicalAgeGod", name); confirmations++; return Task.FromResult(accept); },
                prepareMinorGodLifecycle: (doc, renames, removed) =>
                {
                    var prepared = ModDocumentPersistence.Prepare(fixture.MinorGodsPath, ModXmlDocumentKind.MinorGods);
                    MinorGodCatalog.ApplyTechnologyLifecycle(prepared.Document, doc, renames, removed); return prepared;
                });
            await InXmlWindow(editor, async _ =>
            {
                await UnlockTechnologyXml(editor, "ClassicalAgeGod"); Raw(editor).Text = "<tech name='ClassicalAgeGod'><future/></tech>";
                Assert.Equal(accept, await editor.SaveAsync()); Assert.Equal(1, confirmations);
                if (accept)
                {
                    Assert.Empty(XDocument.Load(fixture.MinorGodsPath).Root!.Elements());
                    Assert.Empty(FindTechnology(XDocument.Load(fixture.TechtreePath), "ClassicalAgeGod").Elements("flag"));
                    Assert.True(Raw(editor).IsReadOnly);
                }
                else
                {
                    Assert.Equal(before, File.ReadAllBytes(fixture.TechtreePath)); Assert.Equal(gods, File.ReadAllBytes(fixture.MinorGodsPath));
                    Assert.True(editor.IsDirty); Assert.False(Raw(editor).IsReadOnly);
                }
            });
        });
    }

    [Fact]
    public void XmlUnlockCommitsAPendingFormNameBeforeCapturingTheRawBuffer()
    {
        using var fixture = new TechnologyEditorFixture(XmlInitial);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var editor = fixture.CreateEditor();
            await InXmlWindow(editor, async _ =>
            {
                editor.SetModifiedMode(true); editor.SelectTechnology("A"); await AwaitTechnologyBuild(editor);
                GetPrivateField<TextBox>(editor, "_techNameBox").Text = "Named";
                await UnlockTechnologyXml(editor);
                Assert.Equal("Named", editor.CurrentTechnologyName); Assert.Contains("name=\"Named\"", Raw(editor).Text);
                Raw(editor).Text = "<tech name='Named'><researchpoints>9</researchpoints></tech>";
                Assert.True(await SaveXmlChecked(editor)); Assert.True(Raw(editor).IsReadOnly);
                Assert.Equal("9", FindTechnology(XDocument.Load(fixture.TechtreePath), "Named").Element("researchpoints")!.Value);
            });
        });
    }

    private static (TechnologyEditorView View, object Kind) AttachXmlTechnologyHost(BaselineHost host)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        host.Call("EnsureTechnologyView");
        var view = (TechnologyEditorView)typeof(ProtoEditorWindow).GetField("_technologyView", flags)!.GetValue(host)!;
        var kind = Enum.Parse(typeof(ProtoEditorWindow).GetNestedType("EditorEntityKind", BindingFlags.NonPublic)!, "Technologies");
        if (typeof(ProtoEditorWindow).GetField("_mainDocumentTab", flags)!.GetValue(host) == null) host.Call("InitializeDocumentTabs");
        host.Set("_activeEntityKind", kind); host.Set("_documentTabActivationInProgress", true); host.Set("_suppressUnitListSelectionChange", true);
        ((Avalonia.Controls.Primitives.TabStrip)typeof(ProtoEditorWindow).GetField("_unitTabs", flags)!.GetValue(host)!).SelectedIndex = 1;
        host.Call("SetMainDocumentTarget", kind, true, "A"); host.GodHost.Content = view;
        view.SetModifiedMode(true); view.SelectTechnology("A"); host.Show(); Dispatcher.UIThread.RunJobs();
        host.Set("_documentTabActivationInProgress", false); host.Set("_suppressUnitListSelectionChange", false);
        return (view, kind);
    }

    [Fact]
    public void XmlGlobalSavePersistsAndRelocksEvenWhenUnlockingMadeNoDataChanges()
    {
        using var fixture = new BaselineSafetyFixture();
        var path = fixture.Workspace.WriteText("TestMod/game/data/gameplay/techtree_mods.xml", XmlInitial);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var host = fixture.CreateHost(false);
            try
            {
                var (view, _) = AttachXmlTechnologyHost(host);
                async Task GlobalSave()
                {
                    host.Call("Save_Click", null!, new RoutedEventArgs());
                    for (var i = 0; i < 1000 && (bool)typeof(ProtoEditorWindow).GetField("_isUnitSaveInProgress", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!; i++)
                    {
                        if (host.OwnedWindows.OfType<Prompt>().FirstOrDefault() is { } popup)
                        { var detail = popup.PromptText; popup.Close(); Assert.Fail(detail); }
                        await Task.Delay(10);
                    }
                    Assert.True(Raw(view).IsReadOnly);
                }
                await UnlockTechnologyXml(view); Raw(view).Text = "<tech name='A'><researchpoints>6</researchpoints></tech>";
                await GlobalSave(); Assert.Equal("6", FindTechnology(XDocument.Load(path), "A").Element("researchpoints")!.Value);
                await UnlockTechnologyXml(view); Assert.False(view.IsDirty); await GlobalSave(); Assert.False(view.IsDirty);
            }
            finally { foreach (var child in host.OwnedWindows.ToArray()) child.Close(); host.DisposeFixtureWindow(); }
        });
    }

    [Fact]
    public void XmlMalformedPinnedTabCanReachTheRealHostDiscardPromptAndClose()
    {
        using var fixture = new BaselineSafetyFixture();
        var path = fixture.Workspace.WriteText("TestMod/game/data/gameplay/techtree_mods.xml", XmlInitial);
        var bytes = File.ReadAllBytes(path);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var host = fixture.CreateHost(false);
            try
            {
                var (view, kind) = AttachXmlTechnologyHost(host);
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var tab = host.Call("CreateDocumentTab", false, kind, true, "A")!;
                tab.GetType().GetProperty("SavedElement")!.SetValue(tab, view.LoadSavedTechnologyElement("A"));
                var tabs = (IList)typeof(ProtoEditorWindow).GetField("_openDocumentTabs", flags)!.GetValue(host)!; tabs.Add(tab);
                ((IList)typeof(ProtoEditorWindow).GetField("_documentTabItems", flags)!.GetValue(host)!).Add(tab.GetType().GetProperty("TabItem")!.GetValue(tab));
                host.Set("_activeDocumentTab", tab);
                await UnlockTechnologyXml(view); Raw(view).Text = "<tech name='A'><broken>";
                var closing = (Task)host.Call("CloseDocumentTabAsync", tab)!;
                var prompt = await XmlPopup(host); Assert.Equal("Unsaved tab", prompt.PromptTitle);
                prompt.FindControl<Button>("_confirmButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await closing.WaitAsync(TimeSpan.FromSeconds(10)); Assert.DoesNotContain(tab, tabs.Cast<object>());
                Assert.Equal(bytes, File.ReadAllBytes(path)); Assert.False(view.IsDirty);
            }
            finally { foreach (var child in host.OwnedWindows.ToArray()) child.Close(); host.DisposeFixtureWindow(); }
        });
    }

    [Fact]
    public void XmlRenameRetainsOwnedStringMigrationWhenNewerXmlStillContainsOldIds()
    {
        using var fixture = new TechnologyEditorFixture("<techtreemods><tech name='A'><displaynameid>STR_TECH_A_NAME</displaynameid></tech></techtreemods>",
            "ID = \"STR_TECH_A_NAME\" ; Str = \"Preserved name\"\r\n");
        AvaloniaTestHost.RunAsync(async () =>
        {
            var callbackCount = 0; TechnologyEditorView editor = null!;
            editor = new TechnologyEditorView([], null, fixture.TechtreePath,
                resolveStringAsync: id => Task.FromResult<string?>(id == "STR_TECH_A_NAME" ? "Preserved name" : null),
                saveStringsAsync: (updates, removals) =>
                {
                    File.WriteAllText(fixture.StringsPath, StringTableDocument.Parse(File.ReadAllText(fixture.StringsPath))
                        .Apply(updates, removals, StringTableSection.Technologies)); return Task.CompletedTask;
                }, modStringsPath: fixture.StringsPath,
                technologySavedAsync: _ =>
                {
                    if (callbackCount++ == 0) Raw(editor).Text = "<tech name='Renamed'><displaynameid>STR_TECH_A_NAME</displaynameid><researchpoints>5</researchpoints></tech>";
                    return Task.CompletedTask;
                });
            await InXmlWindow(editor, async _ =>
            {
                await UnlockTechnologyXml(editor);
                Raw(editor).Text = "<tech name='Renamed'><displaynameid>STR_TECH_A_NAME</displaynameid></tech>";
                Assert.True(await SaveXmlChecked(editor)); Assert.True(editor.IsDirty);
                Assert.True(await SaveXmlChecked(editor));
                var saved = FindTechnology(XDocument.Load(fixture.TechtreePath), "Renamed");
                Assert.Equal("STR_TECH_RENAMED_NAME", saved.Element("displaynameid")!.Value);
                Assert.Equal("5", saved.Element("researchpoints")!.Value);
                var strings = File.ReadAllText(fixture.StringsPath);
                Assert.Contains("STR_TECH_RENAMED_NAME", strings); Assert.DoesNotContain("STR_TECH_A_NAME", strings);
                Assert.Contains("Preserved name", strings); Assert.False(editor.IsDirty); Assert.True(Raw(editor).IsReadOnly);
            });
        });
    }
}
