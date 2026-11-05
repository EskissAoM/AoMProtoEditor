using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Windows;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class ProtoSettingsWindowTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StorageLocationRemainsInternalForApplicationAndProfileSettings(bool profile)
        => WithWindow((fixture, window) =>
        {
            var path = profile ? fixture.ProfilePath : fixture.Path;
            Assert.Null(window.FindControl<ComboBox>("SettingsSourcePicker"));
            Assert.Null(window.FindControl<Button>("SelectSettingsSourceButton"));
            var labels = window.GetVisualDescendants().OfType<TextBlock>().Select(label => label.Text ?? "").ToArray();
            Assert.DoesNotContain("Settings source", labels);
            Assert.DoesNotContain(labels, text => text.Contains("aom_editor_settings.json", StringComparison.Ordinal));
            Assert.False(window.FindControl<Button>("ReloadSettingsButton")!.IsVisible);
            Assert.True(window.CanSaveSettings);
            Edit(window, "DataBarPathBox", "discard.bar");
            Click(window, "CancelSettingsButton");
            Assert.False(window.IsSaved);
            Assert.Equal(path, fixture.Store.LoadCached().Source);
            Assert.Equal(fixture.Original, File.ReadAllBytes(path));
        }, fixture => { if (profile) File.Move(fixture.Path, fixture.ProfilePath); });

    [Fact]
    public void SuccessfulSaveUsesBoundTextAndClosesOnlyAfterCommit()
        => WithWindow((fixture, window) =>
        {
            Edit(window, "DataBarPathBox", "new.bar");
            Edit(window, "UserFolderPathBox", "new-mods");
            Click(window, "SaveSettingsButton");

            Assert.True(window.IsSaved);
            Assert.False(window.IsVisible);
            var saved = fixture.Store.Load().Config!;
            Assert.Equal("new.bar", saved.DataBarPath);
            Assert.Equal("new-mods", saved.UserFolderPath);
            Assert.Equal("remember.xml", saved.LastModFilePath);
            Assert.Contains("Future", File.ReadAllText(fixture.Path));
            Assert.Equal(fixture.Original, File.ReadAllBytes(fixture.Path + ".bak"));
        });

    [Fact]
    public void CancelDiscardsOnlySettingsEditsWithoutWriting()
        => WithWindow((fixture, window) =>
        {
            Edit(window, "DataBarPathBox", "discard.bar");
            Click(window, "CancelSettingsButton");

            Assert.False(window.IsSaved);
            Assert.False(window.IsVisible);
            Assert.Equal(fixture.Original, File.ReadAllBytes(fixture.Path));
            Assert.False(File.Exists(fixture.Path + ".bak"));
        });

    [Fact]
    public void BusySaveKeepsTypedValuesAndAllowsRetry()
        => WithWindow((fixture, window) =>
        {
            Edit(window, "DataBarPathBox", "retry.bar");
            using (var gate = new ProtoEditorSettingsFileSystem().TryAcquireLock(fixture.Path + ".lock"))
            {
                Assert.NotNull(gate);
                Click(window, "SaveSettingsButton");
                AssertUnsuccessfulOpen(window, "Busy");
                Assert.Equal("retry.bar", window.FindControl<TextBox>("DataBarPathBox")!.Text);
                Assert.Equal(fixture.Original, File.ReadAllBytes(fixture.Path));
            }

            Click(window, "SaveSettingsButton");
            Assert.True(window.IsSaved);
            Assert.Equal("retry.bar", fixture.Store.Load().Config!.DataBarPath);
        });

    [Fact]
    public void ConflictRemainsBlockedUntilAnExplicitReloadAndNewEdit()
        => WithWindow((fixture, window) =>
        {
            Edit(window, "DataBarPathBox", "mine.bar");
            var other = fixture.NewStore();
            var config = other.Load().Config!;
            config.DataBarPath = "external.bar";
            other.Save(config);
            var external = File.ReadAllBytes(fixture.Path);

            Click(window, "SaveSettingsButton");
            AssertUnsuccessfulOpen(window, "Conflict");
            Assert.Equal("mine.bar", window.DataBarPath);
            Click(window, "SaveSettingsButton");
            AssertUnsuccessfulOpen(window, "Conflict");
            Assert.Equal(external, File.ReadAllBytes(fixture.Path));

            Click(window, "ReloadSettingsButton");
            Assert.Equal("external.bar", window.FindControl<TextBox>("DataBarPathBox")!.Text);
            Edit(window, "DataBarPathBox", "reviewed.bar");
            Click(window, "SaveSettingsButton");
            Assert.True(window.IsSaved);
            Assert.Equal("reviewed.bar", fixture.Store.Load().Config!.DataBarPath);
        });

    [Fact]
    public void ExternalUnrelatedEditSurvivesWindowSave()
        => WithWindow((fixture, window) =>
        {
            Edit(window, "DataBarPathBox", "mine.bar");
            var other = fixture.NewStore();
            var config = other.Load().Config!;
            config.LastModFilePath = "external.xml";
            other.Save(config);

            Click(window, "SaveSettingsButton");

            Assert.True(window.IsSaved);
            Assert.Equal("mine.bar", fixture.Store.Load().Config!.DataBarPath);
            Assert.Equal("external.xml", fixture.Store.Load().Config!.LastModFilePath);
        });

    [Fact]
    public void NoOpSaveClosesSuccessfullyWithoutRotatingBackup()
        => WithWindow((fixture, window) =>
        {
            Click(window, "SaveSettingsButton");

            Assert.True(window.IsSaved);
            Assert.False(window.IsVisible);
            Assert.Equal(fixture.Original, File.ReadAllBytes(fixture.Path));
            Assert.False(File.Exists(fixture.Path + ".bak"));
        });

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    public void InvalidSettingsShowReasonDisableSaveAndAllowCancel(string invalid)
        => WithWindow((fixture, window) =>
        {
            Assert.False(window.FindControl<Button>("SaveSettingsButton")!.IsEnabled);
            Assert.Contains(fixture.Path, window.FindControl<TextBlock>("SettingsStatus")!.Text);
            Assert.False(window.IsSaved);
            Click(window, "CancelSettingsButton");
            Assert.False(window.IsVisible);
            Assert.Equal(invalid, File.ReadAllText(fixture.Path));
        }, fixture => File.WriteAllText(fixture.Path, invalid));

    [Fact]
    public void AmbiguousSettingsRemainProtectedWithoutExposingStorageControls()
        => WithWindow((fixture, window) =>
        {
            Assert.Null(window.FindControl<ComboBox>("SettingsSourcePicker"));
            Assert.False(window.CanSaveSettings);
            Assert.Contains("Conflicting saved settings", window.StatusMessage);
            Assert.DoesNotContain("Select a source", window.StatusMessage);
            Click(window, "CancelSettingsButton");
            Assert.Equal(fixture.Original, File.ReadAllBytes(fixture.Path));
            Assert.Equal("{}", File.ReadAllText(fixture.ProfilePath));
        }, fixture => File.WriteAllText(fixture.ProfilePath, "{}"));

    [Fact]
    public void BackupRecoveryRequiresCheckboxAndKeepsGoodBackup()
        => WithWindow((fixture, window) =>
        {
            Assert.True(window.FindControl<CheckBox>("RecoveryConfirmation")!.IsVisible);
            Assert.False(window.ConfirmRecovery);
            Edit(window, "DataBarPathBox", "recovered.bar");
            Click(window, "SaveSettingsButton");
            AssertUnsuccessfulOpen(window, "Confirm recovery");
            Assert.Equal("null", File.ReadAllText(fixture.Path));

            window.FindControl<CheckBox>("RecoveryConfirmation")!.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Click(window, "SaveSettingsButton");

            Assert.True(window.IsSaved);
            Assert.Equal("recovered.bar", fixture.Store.Load().Config!.DataBarPath);
            Assert.Equal(fixture.Original, File.ReadAllBytes(fixture.Path + ".bak"));
            var damaged = Assert.Single(Directory.GetFiles(fixture.Application, "*.recovery"));
            Assert.Equal("null", File.ReadAllText(damaged));
        }, PrepareRecovery);

    [Fact]
    public void RecoveryInputChangeRetainsEditsAndCancelStillWorks()
        => WithWindow((fixture, window) =>
        {
            Edit(window, "DataBarPathBox", "typed.bar");
            window.ConfirmRecovery = true;
            File.WriteAllText(fixture.Path + ".bak", "{\"DataBarPath\":\"other.bar\"}");
            Click(window, "SaveSettingsButton");

            AssertUnsuccessfulOpen(window, "Conflict");
            Assert.Equal("typed.bar", window.DataBarPath);
            Assert.Equal("null", File.ReadAllText(fixture.Path));
            Click(window, "CancelSettingsButton");
            Assert.False(window.IsVisible);
            Assert.Contains("other.bar", File.ReadAllText(fixture.Path + ".bak"));
        }, PrepareRecovery);

    [Fact]
    public void ReloadClearsRecoveryConsentAndAdoptsRepairedPrimary()
        => WithWindow((fixture, window) =>
        {
            window.ConfirmRecovery = true;
            File.WriteAllText(fixture.Path, "{\"DataBarPath\":\"repaired.bar\"}");
            Click(window, "ReloadSettingsButton");

            Assert.False(window.ConfirmRecovery);
            Assert.False(window.FindControl<CheckBox>("RecoveryConfirmation")!.IsVisible);
            Assert.Equal("repaired.bar", window.DataBarPath);
            Assert.False(window.IsSaved);
        }, PrepareRecovery);

    [Fact]
    public void AccessFailureIsVisibleAndCancelRemainsAvailable()
        => WithWindow((fixture, window) =>
        {
            Edit(window, "DataBarPathBox", "typed.bar");
            File.WriteAllText(fixture.Path + ".bak", "{}");
            using var locked = new FileStream(fixture.Path + ".bak", FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Click(window, "SaveSettingsButton");

            AssertUnsuccessfulOpen(window, "Failed");
            Assert.Equal("typed.bar", window.DataBarPath);
            Assert.Equal(fixture.Original, File.ReadAllBytes(fixture.Path));
            Click(window, "CancelSettingsButton");
            Assert.False(window.IsVisible);
        });

    private static void PrepareRecovery(Fixture fixture)
    {
        File.WriteAllBytes(fixture.Path + ".bak", fixture.Original);
        File.WriteAllText(fixture.Path, "null");
    }

    private static void AssertUnsuccessfulOpen(ProtoSettingsWindow window, string message)
    {
        Assert.False(window.IsSaved);
        Assert.True(window.IsVisible);
        Assert.Contains(message, window.FindControl<TextBlock>("SettingsStatus")!.Text);
        Assert.True(window.FindControl<Button>("CancelSettingsButton")!.IsEnabled);
    }

    private static void Edit(ProtoSettingsWindow window, string name, string text)
    {
        window.FindControl<TextBox>(name)!.Text = text;
        Dispatcher.UIThread.RunJobs();
    }

    private static void Click(ProtoSettingsWindow window, string name)
    {
        window.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    private static void WithWindow(Action<Fixture, ProtoSettingsWindow> test, Action<Fixture>? prepare = null)
    {
        using var fixture = new Fixture();
        prepare?.Invoke(fixture);
        AvaloniaTestHost.Run(() =>
        {
            var window = new ProtoSettingsWindow(fixture.Store);
            try { window.Show(); test(fixture, window); }
            finally { window.Close(); }
        });
    }

    private sealed class Fixture : IDisposable
    {
        private readonly TestWorkspace _workspace = new("f11-d-settings-window");
        internal string Application { get; }
        internal string Profile { get; }
        internal string Path { get; }
        internal string ProfilePath => System.IO.Path.Combine(Profile, ProtoEditorSettings.SettingsFilename);
        internal byte[] Original { get; }
        internal ProtoEditorSettingsStore Store { get; }

        internal Fixture()
        {
            Application = _workspace.CreateDirectory("app");
            Profile = _workspace.CreateDirectory("profile");
            Path = _workspace.WriteText("app/" + ProtoEditorSettings.SettingsFilename,
                "{\"DataBarPath\":\"old.bar\",\"UserFolderPath\":\"old-mods\",\"LastModFilePath\":\"remember.xml\",\"Future\":42}");
            Original = File.ReadAllBytes(Path);
            Store = NewStore();
        }

        internal ProtoEditorSettingsStore NewStore() => new(Application, Profile);
        public void Dispose() => _workspace.Dispose();
    }
}
