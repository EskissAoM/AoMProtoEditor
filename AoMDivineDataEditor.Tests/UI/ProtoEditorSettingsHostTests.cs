using System.Collections;
using System.Reflection;
using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Windows;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class ProtoEditorSettingsHostTests
{
    [Fact]
    public void UncertainPreferencePublicationReportsRecoveryPathsWithoutClaimingModFailure()
    {
        using var fixture = new BaselineSafetyFixture();
        AvaloniaTestHost.Run(() =>
        {
            var host = fixture.CreateHost(false);
            try
            {
                var result = new ProtoEditorSettingsSaveResult(ProtoEditorSettingsSaveState.Failed,
                    fixture.RecoveryTarget, "Publication may have completed; inspect recovery candidates.", null, [fixture.RecoveryTarget]);
                host.Call("ReportSettingsSave", result, "The mod was loaded successfully");
                var notice = host.FindControl<TextBlock>("_settingsNotice")!;
                Assert.True(notice.IsVisible);
                Assert.Contains("mod was loaded successfully", notice.Text);
                Assert.Contains("not confirmed", notice.Text);
                Assert.Contains("may have completed", notice.Text);
                Assert.Contains(fixture.RecoveryTarget, notice.Text);
                Assert.Equal(fixture.OriginalBytes, File.ReadAllBytes(fixture.RecoveryTarget));
            }
            finally { host.DisposeFixtureWindow(); }
        });
    }

    [Fact]
    public void CreatingModStillSucceedsWhenPreferenceSaveIsBusy()
    {
        using var fixture = new BaselineSafetyFixture();
        using var settings = new SettingsWorkflowFixture();
        var local = settings.Workspace.CreateDirectory("mods/local");
        File.WriteAllText(settings.Path, System.Text.Json.JsonSerializer.Serialize(new ProtoEditorConfig { UserFolderPath = local }));
        var before = File.ReadAllBytes(settings.Path);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var host = fixture.CreateHost(false);
            Configure(host, settings);
            host.Show();
            try
            {
                using var gate = new ProtoEditorSettingsFileSystem().TryAcquireLock(settings.Path + ".lock");
                Assert.NotNull(gate);
                var create = (Task<bool>)host.Call("EnsureLocalModAsync", true)!;
                Dispatcher.UIThread.RunJobs();
                var prompt = Assert.Single(host.OwnedWindows.OfType<InputPromptWindow>());
                prompt.FindControl<TextBox>("_inputBox")!.Text = "SettingsFailureMod";
                prompt.FindControl<Button>("_confirmButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(await create.WaitAsync(TimeSpan.FromSeconds(10)));
                var created = Path.Combine(local, "SettingsFailureMod", "game", "data", "gameplay", "proto_mods.xml");
                Assert.True(File.Exists(created));
                Assert.NotNull(XDocument.Load(created).Root);
                Assert.Equal(created, Field(host, "_modFilePath"));
                Assert.Contains("Busy", host.FindControl<TextBlock>("_settingsNotice")!.Text);
                Assert.Equal(before, File.ReadAllBytes(settings.Path));
            }
            finally
            {
                foreach (var owned in host.OwnedWindows.ToArray()) owned.Close();
                host.DisposeFixtureWindow();
            }
        });
    }

    [Fact]
    public void LoadedModRemainsSuccessfulWhenRememberingItsPathIsBusy()
    {
        using var fixture = new BaselineSafetyFixture();
        using var settings = new SettingsWorkflowFixture();
        var bytes = File.ReadAllBytes(settings.Path);
        AvaloniaTestHost.Run(() =>
        {
            var host = fixture.CreateHost(false);
            Configure(host, settings);
            try
            {
                using var gate = new ProtoEditorSettingsFileSystem().TryAcquireLock(settings.Path + ".lock");
                Assert.NotNull(gate);
                Assert.True((bool)host.Call("TryLoadModFile", fixture.ProtoPath)!);
                Assert.Null(Field(host, "_lastModLoadError"));
                Assert.Equal(fixture.ProtoPath, Field(host, "_modFilePath"));
                var notice = host.FindControl<TextBlock>("_settingsNotice")!;
                Assert.True(notice.IsVisible);
                Assert.Contains("mod was loaded successfully", notice.Text);
                Assert.Contains("Busy", notice.Text);
                Assert.Equal(bytes, File.ReadAllBytes(settings.Path));
                // Normal operation statuses must not erase the preference warning.
                host.FindControl<TextBlock>("_statusMessage")!.Text = "Another operation succeeded";
                Assert.True(notice.IsVisible);
            }
            finally { host.DisposeFixtureWindow(); }
        });
    }

    [Fact]
    public void InitialInvalidSettingsWarnWithoutPreventingBaseBrowsing()
    {
        using var fixture = new BaselineSafetyFixture();
        using var settings = new SettingsWorkflowFixture();
        File.WriteAllText(settings.Path, "null");
        AvaloniaTestHost.RunAsync(async () =>
        {
            var host = fixture.CreateHost(false);
            Configure(host, settings);
            try
            {
                await (Task)host.Call("EnsureInitialModSelectionAsync")!;
                Assert.Contains("No mod selected", host.FindControl<TextBlock>("_fileLabel")!.Text);
                Assert.Contains(settings.Path, host.FindControl<TextBlock>("_settingsNotice")!.Text);
                Assert.Equal("null", File.ReadAllText(settings.Path));
            }
            finally { host.DisposeFixtureWindow(); }
        });
    }

    [Fact]
    public void ExtractionPreferenceConflictLeavesLoadedModAndUserPathIntact()
    {
        using var fixture = new BaselineSafetyFixture();
        using var settings = new SettingsWorkflowFixture();
        var observed = settings.Store.Load();
        File.WriteAllText(settings.Path, "{\"DataBarPath\":\"user-chosen.bar\"}");
        AvaloniaTestHost.Run(() =>
        {
            var host = fixture.CreateHost(false);
            Configure(host, settings);
            try
            {
                var root = Field(host, "_modXmlRoot");
                host.Call("RememberExtractedDataBar", observed, "working.bar", 12d);
                Assert.Same(root, Field(host, "_modXmlRoot"));
                Assert.Contains("Conflict", host.FindControl<TextBlock>("_settingsNotice")!.Text);
                Assert.Contains("user-chosen.bar", File.ReadAllText(settings.Path));
            }
            finally { host.DisposeFixtureWindow(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SettingsReturnPreservesLiveUnitAndGodDraftsTabsAndSelection(bool save)
    {
        using var fixture = new BaselineSafetyFixture();
        using var settings = new SettingsWorkflowFixture();
        File.WriteAllText(fixture.ProtoPath, "<protomods><unit name='agga'/><unit name='Other'/></protomods>");
        var unitBytes = File.ReadAllBytes(fixture.ProtoPath);
        var godBytes = File.ReadAllBytes(fixture.GodPath);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var host = fixture.CreateHost(true);
            Configure(host, settings);
            var window = new ProtoSettingsWindow(settings.Store);
            try
            {
                host.Show();
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(30); // Same population settling boundary used by existing God tests.
                host.ChangeGodKey();
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(30);
                Assert.True(host.God!.IsDirty);
                var godTab = host.AddGodTab(true, true);
                var root = Field(host, "_modXmlRoot");
                var controls = (IDictionary)Field(host, "_fieldControls")!;
                var typed = new TextBox { Text = "agga-renamed-but-unsaved" };
                controls["__proto_unit_name"] = typed;
                host.Set("_currentUnitName", "agga");
                host.Set("_isDirty", true);
                ((HashSet<string>)Field(host, "_dirtyUnitNames")!).Add("agga");
                var tabCount = ((IList)Field(host, "_openDocumentTabs")!).Count;
                var list = host.FindControl<ListBox>("_unitList")!;
                var selection = list.SelectedItem;
                window.Show();
                window.DataBarPath = "new.bar";
                Click(window, save ? "SaveSettingsButton" : "CancelSettingsButton");

                await (Task)host.Call("ApplySettingsWindowResultAsync", window)!;

                Assert.Equal(save, window.IsSaved);
                Assert.Same(root, Field(host, "_modXmlRoot"));
                Assert.True((bool)Field(host, "_isDirty")!);
                Assert.Contains("agga", (HashSet<string>)Field(host, "_dirtyUnitNames")!);
                Assert.Same(typed, controls["__proto_unit_name"]);
                Assert.Equal("agga-renamed-but-unsaved", typed.Text);
                Assert.True(host.God.IsDirty);
                Assert.Same(godTab, Field(host, "_activeDocumentTab"));
                Assert.Equal(tabCount, ((IList)Field(host, "_openDocumentTabs")!).Count);
                Assert.Same(selection, list.SelectedItem);
                Assert.Equal(unitBytes, File.ReadAllBytes(fixture.ProtoPath));
                Assert.Equal(godBytes, File.ReadAllBytes(fixture.GodPath));
            }
            finally { window.Close(); host.DisposeFixtureWindow(); }
        });
    }

    private static void Configure(BaselineHost host, SettingsWorkflowFixture settings)
    {
        host.Set("_readSettings", (Func<ProtoEditorSettingsLoadResult>)settings.Store.LoadCached);
        host.Set("_refreshSettings", (Func<ProtoEditorSettingsLoadResult>)settings.Store.Load);
        host.Set("_rememberModSettings", (Func<string, ProtoEditorSettingsSaveResult>)settings.Store.RememberLastModPath);
        host.Set("_rememberBarSettings", (Func<ProtoEditorSettingsLoadResult, string, double, ProtoEditorSettingsSaveResult>)settings.Store.RememberDataBar);
    }

    private static object? Field(BaselineHost host, string name)
        => typeof(ProtoEditorWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host);

    private static void Click(ProtoSettingsWindow window, string name)
        => window.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
}
