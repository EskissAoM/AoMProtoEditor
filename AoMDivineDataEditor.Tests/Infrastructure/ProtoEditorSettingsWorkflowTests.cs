using System.Text;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Services;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class ProtoEditorSettingsWorkflowTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitChoiceWritesOnlyChosenSourceAndRetainsItsBackup(bool profile)
    {
        using var fixture = new SettingsWorkflowFixture();
        File.WriteAllText(fixture.ProfilePath, "{\"DataBarPath\":\"profile.bar\"}");
        Assert.Equal(ProtoEditorSettingsLoadState.Ambiguous, fixture.Store.Load().State);
        var selectedPath = profile ? fixture.ProfilePath : fixture.Path;
        var otherPath = profile ? fixture.Path : fixture.ProfilePath;
        var other = File.ReadAllBytes(otherPath);
        var original = File.ReadAllBytes(selectedPath);
        var selected = fixture.Store.SelectSource(selectedPath);
        Assert.Equal(original, File.ReadAllBytes(selectedPath));
        selected.Config!.DataBarPath = "selected.bar";

        var result = fixture.Store.TrySave(selected, selected.Config);

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(other, File.ReadAllBytes(otherPath));
        Assert.Equal(original, File.ReadAllBytes(selectedPath + ".bak"));
        Assert.Equal(selectedPath, fixture.Store.LoadCached().Source);
    }

    [Fact]
    public void SourceSwitchRejectsOldBaselinesEvenAfterSwitchingBack()
    {
        using var fixture = new SettingsWorkflowFixture();
        var old = fixture.Store.Load();
        File.WriteAllText(fixture.ProfilePath, "{}");
        fixture.Store.SelectSource(fixture.ProfilePath);
        fixture.Store.SelectSource(fixture.Path);
        old.Config!.DataBarPath = "stale.bar";

        Assert.Equal(ProtoEditorSettingsSaveState.Refused, fixture.Store.TrySave(old, old.Config).State);
        Assert.DoesNotContain("stale.bar", File.ReadAllText(fixture.Path));
    }

    [Fact]
    public void InvalidChoiceCannotChangePinnedSourceOrWriteOutsideCandidates()
    {
        using var fixture = new SettingsWorkflowFixture();
        var old = fixture.Store.Load();
        var outside = fixture.Workspace.GetPath("not-a-settings-source.json");
        Assert.Equal(ProtoEditorSettingsLoadState.Unavailable, fixture.Store.SelectSource(outside).State);
        Assert.Equal(fixture.Path, fixture.Store.LoadCached().Source);
        Assert.False(File.Exists(outside));
        old.Config!.DataBarPath = "valid.bar";
        Assert.True(fixture.Store.TrySave(old, old.Config).Succeeded);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SourceChoiceDoesNotBypassBusyOrStaleEditGuards(bool stale)
    {
        using var fixture = new SettingsWorkflowFixture();
        File.WriteAllText(fixture.ProfilePath, "{}");
        var selected = fixture.Store.SelectSource(fixture.Path);
        selected.Config!.DataBarPath = "mine.bar";
        if (stale)
        {
            File.WriteAllText(fixture.Path, "{\"DataBarPath\":\"external.bar\"}");
            Assert.Equal(ProtoEditorSettingsSaveState.Conflict, fixture.Store.TrySave(selected, selected.Config).State);
        }
        else
        {
            using var gate = new ProtoEditorSettingsFileSystem().TryAcquireLock(fixture.Path + ".lock");
            Assert.NotNull(gate);
            Assert.Equal(ProtoEditorSettingsSaveState.Busy, fixture.Store.TrySave(selected, selected.Config).State);
        }
        Assert.DoesNotContain("mine.bar", File.ReadAllText(fixture.Path));
        Assert.Equal("{}", File.ReadAllText(fixture.ProfilePath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NewlyAppearingAlternateBackupOrChangedAcknowledgedSourceNeedsNewChoice(bool backup)
    {
        using var fixture = new SettingsWorkflowFixture();
        File.WriteAllText(fixture.ProfilePath, "{}");
        var selected = fixture.Store.SelectSource(fixture.Path);
        selected.Config!.DataBarPath = "mine.bar";
        fixture.Files.AfterStage = () => File.WriteAllText(fixture.ProfilePath + (backup ? ".bak" : ""), "{\"Future\":1}");

        Assert.Equal(ProtoEditorSettingsSaveState.Conflict, fixture.Store.TrySave(selected, selected.Config).State);
        Assert.DoesNotContain("mine.bar", File.ReadAllText(fixture.Path));
    }

    [Fact]
    public void WarmSnapshotsPerformNoSettingsIoAndReturnDeepIndependentCopies()
    {
        using var fixture = new SettingsWorkflowFixture();
        File.WriteAllText(fixture.Path, "{\"CategoriesByModPath\":{\"mod\":{\"Categories\":[\"old\"],\"UnitCategories\":{\"Hoplite\":\"old\"}}}}");
        var first = fixture.Store.LoadCached();
        var reads = fixture.Files.Reads;
        var inspections = fixture.Files.Inspections;
        first.Config!.CategoriesByModPath!["mod"].Categories.Add("mutated");
        first.Config.CategoriesByModPath["mod"].UnitCategories["Hoplite"] = "mutated";
        for (var index = 0; index < 20; index++)
        {
            var copy = fixture.Store.LoadCached();
            Assert.Single(copy.Config!.CategoriesByModPath!["mod"].Categories);
            Assert.Equal("old", copy.Config.CategoriesByModPath["mod"].UnitCategories["Hoplite"]);
            Assert.NotSame(first.Config, copy.Config);
        }
        Assert.Equal(reads, fixture.Files.Reads);
        Assert.Equal(inspections, fixture.Files.Inspections);
    }

    [Fact]
    public void ExplicitRefreshAdoptsExternalChangesAndNeverCachesInvalidDefaults()
    {
        using var fixture = new SettingsWorkflowFixture();
        Assert.Equal("old.bar", fixture.Store.LoadCached().Config!.DataBarPath);
        File.WriteAllText(fixture.Path, "{\"DataBarPath\":\"external.bar\"}");
        Assert.Equal("old.bar", fixture.Store.LoadCached().Config!.DataBarPath);
        Assert.Equal("external.bar", fixture.Store.Load().Config!.DataBarPath);
        File.WriteAllText(fixture.Path, "null");
        Assert.Equal(ProtoEditorSettingsLoadState.Invalid, fixture.Store.Load().State);
        File.WriteAllText(fixture.Path, "{\"DataBarPath\":\"repaired.bar\"}");
        Assert.Equal("repaired.bar", fixture.Store.LoadCached().Config!.DataBarPath);
    }

    [Fact]
    public void CachedBaselineCannotClobberExternalEditAndSuccessfulSaveUpdatesCache()
    {
        using var fixture = new SettingsWorkflowFixture();
        var old = fixture.Store.LoadCached();
        old.Config!.DataBarPath = "mine.bar";
        File.WriteAllText(fixture.Path, "{\"DataBarPath\":\"external.bar\"}");
        Assert.Equal(ProtoEditorSettingsSaveState.Conflict, fixture.Store.TrySave(old, old.Config).State);
        Assert.Equal("external.bar", fixture.Store.LoadCached().Config!.DataBarPath);
        var refreshed = fixture.Store.LoadCached();
        refreshed.Config!.DataBarPath = "reviewed.bar";
        Assert.True(fixture.Store.TrySave(refreshed, refreshed.Config).Succeeded);
        var reads = fixture.Files.Reads;
        Assert.Equal("reviewed.bar", fixture.Store.LoadCached().Config!.DataBarPath);
        Assert.Equal(reads, fixture.Files.Reads);
    }

    [Fact]
    public void NarrowUpdatesPreserveUnrelatedExternalAndUnknownSettings()
    {
        using var fixture = new SettingsWorkflowFixture();
        var beforeExtraction = fixture.Store.Load();
        File.WriteAllText(fixture.Path, "{\"DataBarPath\":\"old.bar\",\"UserFolderPath\":\"external-mods\",\"LastModFilePath\":\"elsewhere.xml\",\"Future\":42}");
        Assert.True(fixture.Store.RememberDataBar(beforeExtraction, "new.bar", 123).Succeeded);
        Assert.True(fixture.Store.RememberLastModPath("loaded.xml").Succeeded);
        var saved = fixture.Store.LoadCached().Config!;
        Assert.Equal("new.bar", saved.DataBarPath);
        Assert.Equal(123, saved.DataBarMtime);
        Assert.Equal("external-mods", saved.UserFolderPath);
        Assert.Equal("loaded.xml", saved.LastModFilePath);
        Assert.Contains("\"Future\": 42", File.ReadAllText(fixture.Path));
    }

    [Fact]
    public void ExtractionDoesNotOverrideAPathChangedWhileItWasWorking()
    {
        using var fixture = new SettingsWorkflowFixture();
        var observed = fixture.Store.Load();
        File.WriteAllText(fixture.Path, "{\"DataBarPath\":\"user-selected.bar\"}");
        var result = fixture.Store.RememberDataBar(observed, "extracted.bar", 123);
        Assert.Equal(ProtoEditorSettingsSaveState.Conflict, result.State);
        Assert.Contains("user-selected.bar", File.ReadAllText(fixture.Path));
    }

    [Fact]
    public void NarrowUpdatesRefuseRecoveryOrInvalidInputsWithoutChangingFiles()
    {
        using var fixture = new SettingsWorkflowFixture();
        var observed = fixture.Store.Load();
        File.WriteAllBytes(fixture.Path + ".bak", File.ReadAllBytes(fixture.Path));
        File.WriteAllText(fixture.Path, "null");
        Assert.Equal(ProtoEditorSettingsSaveState.Refused, fixture.Store.RememberLastModPath("no.xml").State);
        Assert.Equal(ProtoEditorSettingsSaveState.Refused, fixture.Store.RememberDataBar(observed, "no.bar", 123).State);
        Assert.Equal("null", File.ReadAllText(fixture.Path));
    }

    [Fact]
    public async Task GameDataWarmLookupsReuseSettingsAndAdoptSavedAndRefreshedPaths()
    {
        using var fixture = new SettingsWorkflowFixture();
        var first = WriteStringArchive(fixture, "game1", "one");
        var second = WriteStringArchive(fixture, "game2", "two");
        File.WriteAllText(fixture.Path, System.Text.Json.JsonSerializer.Serialize(new ProtoEditorConfig { DataBarPath = first }));
        var service = new EditorGameDataService(fixture.Store);
        Assert.Equal("one", await service.LookupStringKeyAsync("STR_TEST"));
        var reads = fixture.Files.Reads;
        for (var index = 0; index < 12; index++)
        {
            Assert.Equal(fixture.Workspace.GetPath("game1"), service.RootDirectory);
            Assert.Equal("one", await service.LookupStringKeyAsync("STR_TEST"));
        }
        Assert.Equal(reads, fixture.Files.Reads);
        var edited = fixture.Store.LoadCached();
        edited.Config!.DataBarPath = second;
        Assert.True(fixture.Store.TrySave(edited, edited.Config).Succeeded);
        Assert.Equal(fixture.Workspace.GetPath("game2"), service.RootDirectory);
        Assert.Equal("two", await service.LookupStringKeyAsync("STR_TEST"));

        File.WriteAllText(fixture.Path, System.Text.Json.JsonSerializer.Serialize(new ProtoEditorConfig { DataBarPath = first }));
        Assert.Equal("two", await service.LookupStringKeyAsync("STR_TEST"));
        service.RefreshSettings();
        Assert.Equal("one", await service.LookupStringKeyAsync("STR_TEST"));
        File.WriteAllText(fixture.Path, "null");
        Assert.Equal(ProtoEditorSettingsLoadState.Recovered, service.RefreshSettings().State);
        Assert.Equal("one", await service.LookupStringKeyAsync("STR_TEST"));
        File.WriteAllText(fixture.Path + ".bak", "null");
        Assert.Equal(ProtoEditorSettingsLoadState.Invalid, service.RefreshSettings().State);
        Assert.Null(service.RootDirectory);
        Assert.Null(await service.LookupStringKeyAsync("STR_TEST"));
    }

    [Fact]
    public async Task ConcurrentLookupsAndSourceChangesKeepResultsFromTheirOwnArchive()
    {
        using var fixture = new SettingsWorkflowFixture();
        var first = WriteStringArchive(fixture, "game1", "one");
        var second = WriteStringArchive(fixture, "game2", "two");
        File.WriteAllText(fixture.Path, System.Text.Json.JsonSerializer.Serialize(new ProtoEditorConfig { DataBarPath = first }));
        File.WriteAllText(fixture.ProfilePath, System.Text.Json.JsonSerializer.Serialize(new ProtoEditorConfig { DataBarPath = second }));
        fixture.Store.SelectSource(fixture.Path);
        var service = new EditorGameDataService(fixture.Store);
        var tasks = Enumerable.Range(0, 8).Select(_ => service.LookupStringKeyAsync("STR_TEST").AsTask()).ToArray();
        fixture.Store.SelectSource(fixture.ProfilePath);
        Assert.Equal("two", await service.LookupStringKeyAsync("STR_TEST"));
        Assert.All(await Task.WhenAll(tasks), result => Assert.Equal("one", result));
        Assert.Equal("two", await service.LookupStringKeyAsync("STR_TEST"));
    }

    private static string WriteStringArchive(SettingsWorkflowFixture fixture, string game, string text)
    {
        var path = fixture.Workspace.GetPath(game + "/data/Data.bar");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var bar = BarArchiveTests.BuildArchive(new BarArchiveTests.TestEntry("strings/english/string_table.txt",
            Encoding.UTF8.GetBytes($"ID = \"STR_TEST\" ; Str = \"{text}\""), 0, false));
        File.WriteAllBytes(path, bar.ToArray());
        return path;
    }
}

internal sealed class SettingsWorkflowFixture : IDisposable
{
    internal TestWorkspace Workspace { get; } = new("f11-d-workflow");
    internal string Path { get; }
    internal string ProfilePath { get; }
    internal SettingsCountingFiles Files { get; } = new();
    internal ProtoEditorSettingsStore Store { get; }
    internal SettingsWorkflowFixture()
    {
        var app = Workspace.CreateDirectory("app");
        var profile = Workspace.CreateDirectory("profile");
        Path = Workspace.WriteText("app/" + ProtoEditorSettings.SettingsFilename, "{\"DataBarPath\":\"old.bar\"}");
        ProfilePath = System.IO.Path.Combine(profile, ProtoEditorSettings.SettingsFilename);
        Store = new(app, profile, Files);
    }
    public void Dispose() => Workspace.Dispose();
}

internal sealed class SettingsCountingFiles : IProtoEditorSettingsFileSystem
{
    private readonly ProtoEditorSettingsFileSystem _disk = new();
    internal int Reads, Inspections;
    internal Action? AfterStage;
    public FileAttributes GetAttributes(string path) { Inspections++; return _disk.GetAttributes(path); }
    public byte[] ReadAllBytes(string path) { Reads++; return _disk.ReadAllBytes(path); }
    public void ProbeWritableDirectory(string directory) => _disk.ProbeWritableDirectory(directory);
    public IDisposable? TryAcquireLock(string path) => _disk.TryAcquireLock(path);
    public void WriteNewAndFlush(string path, byte[] contents) { _disk.WriteNewAndFlush(path, contents); AfterStage?.Invoke(); }
    public void Replace(string temporaryPath, string targetPath, string? backupPath) => _disk.Replace(temporaryPath, targetPath, backupPath);
    public void MoveNew(string temporaryPath, string targetPath) => _disk.MoveNew(temporaryPath, targetPath);
    public void Delete(string path) => _disk.Delete(path);
}
