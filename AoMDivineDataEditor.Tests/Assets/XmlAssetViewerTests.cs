using System.Reflection;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Windows;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using AvaloniaEdit;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class XmlAssetViewerTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Field<T>(AnimFileViewerWindow viewer, string name) => (T)typeof(AnimFileViewerWindow).GetField(name, Private)!.GetValue(viewer)!;
    private static TextEditor Raw(AnimFileViewerWindow viewer) => Field<TextEditor>(viewer, "_preview");
    private static string Xml(bool sound, string value = "Original") => $"<{(sound ? "soundset" : "animfile")} future='keep'><!--keep--><future>{value}</future></{(sound ? "soundset" : "animfile")}>";
    private static async Task Unlock(AnimFileViewerWindow viewer)
    {
        Field<Button>(viewer, "_editXml").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
        Assert.False(Raw(viewer).IsReadOnly);
    }
    private static async Task<List<string>> Save(AnimFileViewerWindow viewer, bool reload = false)
    {
        var errors = new List<string>();
        var saving = (Task)typeof(AnimFileViewerWindow).GetMethod("SaveAsync", Private)!.Invoke(viewer, null)!;
        for (var i = 0; !saving.IsCompleted && i < 800; i++)
        {
            foreach (var popup in viewer.OwnedWindows.OfType<Prompt>().ToArray())
            {
                errors.Add(popup.PromptText);
                if (reload && popup.PromptIsConfirm) popup.FindControl<Button>("_confirmButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                else popup.Close();
            }
            await Task.Delay(10);
        }
        await saving.WaitAsync(TimeSpan.FromSeconds(2));
        return errors;
    }
    private static async Task WithViewer(bool sound, Func<AnimFileViewerWindow, string, Task> check, bool original = false)
    {
        using var workspace = new TestWorkspace();
        var path = workspace.WriteText("unit.xml", Xml(sound));
        var source = original ? null : await XmlAssetEditingSource.OpenAsync(workspace.RootPath, "unit.xml");
        AvaloniaTestHost.RunAsync(async () =>
        {
            var viewer = new AnimFileViewerWindow("unit.xml", Xml(sound), sound ? "Sound Set" : "Anim File", source); viewer.Show();
            try { await check(viewer, path); }
            finally
            {
                foreach (var popup in viewer.OwnedWindows.OfType<Prompt>().ToArray()) popup.Close();
                typeof(AnimFileViewerWindow).GetField("_allowClose", Private)!.SetValue(viewer, true); viewer.Close();
            }
        });
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public Task CustomAssetsStartLockedAndCleanUnlockSaveDoesNotWrite(bool sound)
        => WithViewer(sound, async (viewer, path) =>
        {
            var before = File.ReadAllBytes(path); var commits = 0; viewer.Saved += (_, _) => commits++;
            Assert.True(Raw(viewer).IsReadOnly); Assert.True(Raw(viewer).IsEnabled);
            var formatted = Raw(viewer).Text;
            Assert.Contains(Environment.NewLine + "\t<future>", formatted);
            await Unlock(viewer); Assert.False(Field<XmlEditSession>(viewer, "_session").IsDirty);
            Assert.Equal(formatted, Raw(viewer).Text);
            Assert.Empty(await Save(viewer)); Assert.True(Raw(viewer).IsReadOnly);
            Assert.Equal(0, commits); Assert.Equal(before, File.ReadAllBytes(path));
            viewer.Close(); Assert.False(viewer.IsVisible);
        });

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public Task OriginalsHaveNoEditingOrSaveCapability(bool sound)
        => WithViewer(sound, async (viewer, path) =>
        {
            var before = File.ReadAllBytes(path);
            Assert.False(Field<Button>(viewer, "_editXml").IsVisible); Assert.False(Field<Button>(viewer, "_saveButton").IsVisible);
            Field<Button>(viewer, "_editXml").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Empty(await Save(viewer)); Assert.True(Raw(viewer).IsReadOnly); Assert.Equal(before, File.ReadAllBytes(path));
        }, original: true);

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task BeautifiedCustomXmlKeepsDeclarationEncodingAndNoopBytes(bool sound)
    {
        using var workspace = new TestWorkspace();
        var text = "<?xml version='1.0' encoding='utf-16'?>" + Xml(sound);
        var path = workspace.WriteText("unit.xml", "");
        var encoding = new System.Text.UnicodeEncoding(false, true, true);
        var encoded = new AoMDivineDataEditor.GameData.PreservedTextEncoding(text, encoding, encoding.GetPreamble());
        File.WriteAllBytes(path, encoded.Encode(text)); var before = File.ReadAllBytes(path);
        var source = await XmlAssetEditingSource.OpenAsync(workspace.RootPath, "unit.xml");
        AvaloniaTestHost.RunAsync(async () =>
        {
            var viewer = new AnimFileViewerWindow("unit.xml", text, sound ? "Sound Set" : "Anim File", source); viewer.Show();
            try
            {
                var pretty = Raw(viewer).Text;
                Assert.Contains("encoding=\"utf-16\"", pretty); Assert.Contains(Environment.NewLine + "\t<future>", pretty);
                await Unlock(viewer); Assert.Equal(pretty, Raw(viewer).Text);
                Assert.Empty(await Save(viewer)); Assert.Equal(before, File.ReadAllBytes(path));
                await Unlock(viewer); Raw(viewer).Text = pretty.Replace("Original", "Edited");
                Assert.Empty(await Save(viewer)); Assert.True(Raw(viewer).IsReadOnly);
                var saved = AoMDivineDataEditor.GameData.PreservedTextEncoding.Decode(File.ReadAllBytes(path));
                Assert.Equal(encoding.GetPreamble(), saved.Preamble); Assert.Equal(encoding.CodePage, saved.Encoding.CodePage);
                Assert.Equal("utf-16", System.Xml.Linq.XDocument.Parse(saved.Text).Declaration!.Encoding);
                Assert.Contains("Edited", saved.Text);
                Assert.Equal(XmlPreviewFormatter.Beautify(Raw(viewer).Text), Raw(viewer).Text);
            }
            finally { typeof(AnimFileViewerWindow).GetField("_allowClose", Private)!.SetValue(viewer, true); viewer.Close(); }
        });
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public Task RawSavePersistsAndNewerTextSurvivesTheCompletedSave(bool sound)
        => WithViewer(sound, async (viewer, path) =>
        {
            await Unlock(viewer); Raw(viewer).Text = Xml(sound, "First");
            var newer = true;
            viewer.Saved += (_, _) => { if (newer) Raw(viewer).Text = Xml(sound, "Second"); };
            Assert.Empty(await Save(viewer)); Assert.Equal(Xml(sound, "First"), File.ReadAllText(path));
            Assert.Equal(Xml(sound, "Second"), Raw(viewer).Text); Assert.False(Raw(viewer).IsReadOnly);
            newer = false; Assert.Empty(await Save(viewer)); Assert.True(Raw(viewer).IsReadOnly);
            Assert.Contains(Environment.NewLine + "\t<future>", Raw(viewer).Text);
            Assert.Equal(Xml(sound, "Second"), File.ReadAllText(path));
        });

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public Task InvalidXmlKeepsBufferAndDiskAndCanBeDiscarded(bool sound)
        => WithViewer(sound, async (viewer, path) =>
        {
            var before = File.ReadAllBytes(path); await Unlock(viewer); Raw(viewer).Text = "<unfinished>";
            Assert.Single(await Save(viewer)); Assert.Equal("<unfinished>", Raw(viewer).Text); Assert.Equal(before, File.ReadAllBytes(path));
            viewer.Close();
            for (var i = 0; !viewer.OwnedWindows.OfType<Prompt>().Any() && i < 200; i++) await Task.Delay(10);
            var discard = Assert.Single(viewer.OwnedWindows.OfType<Prompt>());
            discard.FindControl<Button>("_confirmButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (var i = 0; viewer.IsVisible && i < 200; i++) await Task.Delay(10);
            Assert.False(viewer.IsVisible); Assert.Equal(before, File.ReadAllBytes(path));
        });

    [Theory]
    [InlineData(false, false)] [InlineData(true, false)]
    [InlineData(false, true)] [InlineData(true, true)]
    public Task ExternalChangeOffersReloadOrKeepsLocalXmlWithoutOverwriting(bool sound, bool reload)
        => WithViewer(sound, async (viewer, path) =>
        {
            await Unlock(viewer); Raw(viewer).Text = Xml(sound, "Local");
            File.WriteAllText(path, Xml(sound, "External")); var before = File.ReadAllBytes(path);
            Assert.Single(await Save(viewer, reload)); Assert.Equal(before, File.ReadAllBytes(path));
            Assert.Equal(reload ? XmlPreviewFormatter.Beautify(Xml(sound, "External")) : Xml(sound, "Local"), Raw(viewer).Text);
            Assert.Equal(reload, Raw(viewer).IsReadOnly);
        });

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public Task ReloadingNewCompiledAliasLocksTheViewerWithoutChangingEitherFile(bool sound)
        => WithViewer(sound, async (viewer, path) =>
        {
            await Unlock(viewer); Raw(viewer).Text = Xml(sound, "Local");
            File.WriteAllText(path + ".xmb", "compiled bytes");
            Assert.Single(await Save(viewer, reload: true));
            Assert.True(Raw(viewer).IsReadOnly); Assert.False(Field<Button>(viewer, "_editXml").IsVisible);
            Assert.False(Field<Button>(viewer, "_saveButton").IsVisible);
            Assert.Equal(Xml(sound), File.ReadAllText(path)); Assert.Equal("compiled bytes", File.ReadAllText(path + ".xmb"));
        });

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public Task WriteFailureKeepsDraftAndDoesNotReportACommit(bool sound)
        => WithViewer(sound, async (viewer, path) =>
        {
            var commits = 0; viewer.Saved += (_, _) => commits++;
            await Unlock(viewer); Raw(viewer).Text = Xml(sound, "Local");
            File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
            try
            {
                Assert.Single(await Save(viewer)); Assert.Equal(0, commits); Assert.False(Raw(viewer).IsReadOnly);
                Assert.Equal(Xml(sound, "Local"), Raw(viewer).Text); Assert.Equal(Xml(sound), File.ReadAllText(path));
            }
            finally { File.SetAttributes(path, FileAttributes.Normal); }
            Assert.Empty(await Save(viewer)); Assert.Equal(1, commits); Assert.True(Raw(viewer).IsReadOnly);
        });

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ManagerOpensCustomSourceAndRecordsOnlyCommittedSaves(bool sound)
    {
        using var workspace = new TestWorkspace(); var path = workspace.WriteText("custom/unit.xml", Xml(sound));
        AvaloniaTestHost.RunAsync(async () =>
        {
            var item = new AnimFileCatalogEntry("custom\\unit.xml", "Custom", true);
            var manager = new AnimFileManagerWindow([item], _ => Task.FromResult<string?>(Xml(sound)), workspace.RootPath,
                profile: sound ? XmlAssetManagerProfile.SoundSet : XmlAssetManagerProfile.AnimFile);
            manager.Show();
            try
            {
                var button = new Button();
                var opening = (Task)typeof(AnimFileManagerWindow).GetMethod("OpenViewerAsync", Private)!.Invoke(manager, [item, button])!;
                for (var i = 0; !manager.OwnedWindows.OfType<AnimFileViewerWindow>().Any() && i < 300; i++) await Task.Delay(10);
                var viewer = Assert.Single(manager.OwnedWindows.OfType<AnimFileViewerWindow>());
                Assert.False(manager.HasSavedXml); Assert.False(button.IsEnabled);
                await Unlock(viewer); Raw(viewer).Text = Xml(sound, "Saved");
                Assert.Empty(await Save(viewer)); Assert.True(manager.HasSavedXml); Assert.Equal(Xml(sound, "Saved"), File.ReadAllText(path));
                viewer.Close(); await opening.WaitAsync(TimeSpan.FromSeconds(2)); Assert.True(button.IsEnabled);
            }
            finally { foreach (var child in manager.OwnedWindows.ToArray()) child.Close(); manager.Close(); }
        });
    }
}

public sealed partial class TacticsStartupTests
{
    [Fact]
    public void SavingAnAnimFileThroughActualManagerRetiresTheHostsOldAnimationCatalog()
    {
        using var fixture = new BaselineSafetyFixture();
        ArchiveCatalogAvailabilityTests.WriteArchive(fixture.Workspace, "IsolatedGame/art/ArtTest.bar", "original.xml.XMB");
        var path = fixture.Workspace.WriteText("TestMod/game/art/unit.xml", "<animfile><anim>Old<file>model.gr2</file></anim></animfile>");
        var protoBefore = File.ReadAllBytes(fixture.ProtoPath);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var host = SeedOwner(fixture); host.Show();
            try
            {
                host.Call("LoadCurrentModAssetCatalogs");
                var oldTask = (Task<AnimFileAnimationNames?>)host.Call("LoadDirectAnimFileAnimationCatalogAsync", "unit.xml")!;
                Assert.Contains("Old", (await oldTask)!.UnitAnimations);
                host.Set("_currentUnitAnimationCatalogTask", oldTask); host.Set("_currentUnitAnimationCatalogResolved", true);
                var opening = (Task)host.Call("OpenAnimFileManagerAsync")!;
                for (var i = 0; !host.OwnedWindows.OfType<AnimFileManagerWindow>().Any() && i < 300; i++) await Task.Delay(10);
                var manager = Assert.Single(host.OwnedWindows.OfType<AnimFileManagerWindow>());
                var entry = new AnimFileCatalogEntry("unit.xml", "Custom", true);
                var viewing = (Task)typeof(AnimFileManagerWindow).GetMethod("OpenViewerAsync", Private)!.Invoke(manager, [entry, new Button()])!;
                for (var i = 0; !manager.OwnedWindows.OfType<AnimFileViewerWindow>().Any() && i < 300; i++) await Task.Delay(10);
                var viewer = Assert.Single(manager.OwnedWindows.OfType<AnimFileViewerWindow>());
                var session = (XmlEditSession)typeof(AnimFileViewerWindow).GetField("_session", Private)!.GetValue(viewer)!;
                session.BeginEditing(); session.Edit(File.ReadAllText(path).Replace("Old", "New"));
                await ((Task)typeof(AnimFileViewerWindow).GetMethod("SaveAsync", Private)!.Invoke(viewer, null)!).WaitAsync(TimeSpan.FromSeconds(5));
                viewer.Close(); await viewing.WaitAsync(TimeSpan.FromSeconds(2));
                manager.Close(); await opening.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Null(Field(host, "_currentUnitAnimationCatalogTask"));
                Assert.False((bool)Field(host, "_currentUnitAnimationCatalogResolved")!);
                var nextTask = (Task<AnimFileAnimationNames?>)host.Call("LoadDirectAnimFileAnimationCatalogAsync", "unit.xml")!;
                Assert.NotSame(oldTask, nextTask); Assert.Equal(new[] { "New" }, (await nextTask)!.UnitAnimations);
                Assert.Equal(protoBefore, File.ReadAllBytes(fixture.ProtoPath));
            }
            finally { foreach (var child in host.OwnedWindows.ToArray()) child.Close(); host.DisposeFixtureWindow(); }
        });
    }
}
