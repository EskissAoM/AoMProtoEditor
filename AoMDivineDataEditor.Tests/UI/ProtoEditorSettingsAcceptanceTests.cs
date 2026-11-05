using System.Reflection;
using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Windows;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class ProtoEditorSettingsAcceptanceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SaveAndCancelSurviveFreshWindowsWithoutChangingNeighborFiles(bool profile)
    {
        using var fixture = new SettingsWorkflowFixture();
        if (profile) File.Move(fixture.Path, fixture.ProfilePath);
        var selectedPath = profile ? fixture.ProfilePath : fixture.Path;
        var selectedDirectory = Path.GetDirectoryName(selectedPath)!;
        var neighbors = new[] { "aom_editor_ability_bindings.json", "aom_editor_proto_metadata_cache.json", "cache_merged_proto.xml" }
            .ToDictionary(name => Path.Combine(selectedDirectory, name), name => System.Text.Encoding.UTF8.GetBytes("preserve exactly: " + name + "\r\n"));
        foreach (var entry in neighbors) File.WriteAllBytes(entry.Key, entry.Value);
        var dataBar = fixture.Workspace.WriteText("game/data/Data.bar", "isolated path for preference persistence");
        var userFolder = fixture.Workspace.CreateDirectory("mods/local");

        AvaloniaTestHost.Run(() =>
        {
            var first = new ProtoSettingsWindow(fixture.Store);
            try
            {
                first.Show();
                first.FindControl<TextBox>("DataBarPathBox")!.Text = dataBar;
                first.FindControl<TextBox>("UserFolderPathBox")!.Text = userFolder;
                Dispatcher.UIThread.RunJobs();
                Click(first, "SaveSettingsButton");
                Assert.True(first.IsSaved);
            }
            finally { first.Close(); }

            var savedBytes = File.ReadAllBytes(selectedPath);
            var reopened = new ProtoSettingsWindow(NewStore(fixture));
            try
            {
                reopened.Show();
                Assert.Equal(dataBar, reopened.FindControl<TextBox>("DataBarPathBox")!.Text);
                Assert.Equal(userFolder, reopened.FindControl<TextBox>("UserFolderPathBox")!.Text);
                reopened.FindControl<TextBox>("DataBarPathBox")!.Text = "discard-this-change.bar";
                Dispatcher.UIThread.RunJobs();
                Click(reopened, "CancelSettingsButton");
                Assert.False(reopened.IsSaved);
            }
            finally { reopened.Close(); }

            Assert.Equal(savedBytes, File.ReadAllBytes(selectedPath));
            var final = NewStore(fixture).Load();
            Assert.Equal(selectedPath, final.Source);
            Assert.Equal(dataBar, final.Config!.DataBarPath);
            Assert.Equal(userFolder, final.Config.UserFolderPath);
            foreach (var entry in neighbors) Assert.Equal(entry.Value, File.ReadAllBytes(entry.Key));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RememberedModIsActuallyLoadedByFreshHostAndSettingsStore(bool profile)
    {
        using var fixture = new BaselineSafetyFixture();
        using var settings = new SettingsWorkflowFixture();
        if (profile) File.Move(settings.Path, settings.ProfilePath);
        var rememberedMod = fixture.Workspace.WriteXml("RememberMe/game/data/gameplay/proto_mods.xml",
            "<protomods><unit name='ReturnedAfterRestart'/></protomods>");
        var before = File.ReadAllBytes(rememberedMod);

        AvaloniaTestHost.RunAsync(async () =>
        {
            var first = fixture.CreateHost(false);
            Configure(first, settings.Store);
            try
            {
                Assert.True((bool)first.Call("TryLoadModFile", rememberedMod)!);
                Assert.Equal(rememberedMod, settings.Store.LoadCached().Config!.LastModFilePath);
            }
            finally { first.DisposeFixtureWindow(); }

            var restarted = fixture.CreateHost(false);
            var freshStore = NewStore(settings);
            Configure(restarted, freshStore);
            try
            {
                Assert.NotEqual(rememberedMod, Field(restarted, "_modFilePath"));
                await (Task)restarted.Call("EnsureInitialModSelectionAsync")!;
                Assert.Equal(rememberedMod, Field(restarted, "_modFilePath"));
                var root = (XElement)Field(restarted, "_modXmlRoot")!;
                Assert.Equal("ReturnedAfterRestart", root.Element("unit")!.Attribute("name")!.Value);
                Assert.Null(Field(restarted, "_lastModLoadError"));
                Assert.Equal(before, File.ReadAllBytes(rememberedMod));
                Assert.Equal(profile ? settings.ProfilePath : settings.Path, freshStore.LoadCached().Source);
            }
            finally { restarted.DisposeFixtureWindow(); }
        });
    }

    private static ProtoEditorSettingsStore NewStore(SettingsWorkflowFixture fixture)
        => new(Path.GetDirectoryName(fixture.Path)!, Path.GetDirectoryName(fixture.ProfilePath)!, fixture.Files);

    private static void Configure(BaselineHost host, ProtoEditorSettingsStore store)
    {
        host.Set("_readSettings", (Func<ProtoEditorSettingsLoadResult>)store.LoadCached);
        host.Set("_refreshSettings", (Func<ProtoEditorSettingsLoadResult>)store.Load);
        host.Set("_rememberModSettings", (Func<string, ProtoEditorSettingsSaveResult>)store.RememberLastModPath);
        host.Set("_rememberBarSettings", (Func<ProtoEditorSettingsLoadResult, string, double, ProtoEditorSettingsSaveResult>)store.RememberDataBar);
    }

    private static object? Field(BaselineHost host, string name)
        => typeof(ProtoEditorWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host);

    private static void Click(ProtoSettingsWindow window, string name)
    {
        window.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }
}
