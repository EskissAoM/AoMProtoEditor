using System.Text;
using System.Text.Json.Nodes;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.GameData;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class ProtoEditorSettingsPersistenceTests
{
    [Fact]
    public void FirstCreateAndReplacementPublishValidJsonAndKeepExactPreviousBytes()
    {
        using var fixture = new Fixture();
        var first = fixture.Store.Load();
        first.Config!.DataBarPath = "first.bar";
        Assert.Equal(ProtoEditorSettingsSaveState.Saved, fixture.Store.TrySave(first, first.Config).State);
        var before = File.ReadAllBytes(fixture.Path);
        Assert.False(File.Exists(fixture.Backup));
        var second = fixture.Store.Load();
        second.Config!.UserFolderPath = "mods";

        var saved = fixture.Store.TrySave(second, second.Config);

        Assert.True(saved.Succeeded, saved.Error);
        Assert.Equal("first.bar", fixture.Store.Load().Config!.DataBarPath);
        Assert.Equal("mods", fixture.Store.Load().Config!.UserFolderPath);
        Assert.Equal(before, File.ReadAllBytes(fixture.Backup));
        Assert.Equal(fixture.Store.Load().BackupBaseline, saved.Current!.BackupBaseline);
        Assert.True(File.Exists(fixture.Path + ".lock"));
        Assert.Empty(fixture.TemporaryFiles());
    }

    [Fact]
    public void NoOpPreservesOriginalFormattingAndDoesNotRotateBackup()
    {
        using var fixture = new Fixture("{ \"DataBarPath\" : \"old.bar\", \"Future\" : 7 }\r\n");
        File.WriteAllText(fixture.Backup, "{\"DataBarPath\":\"previous.bar\"}");
        var before = File.ReadAllBytes(fixture.Path);
        var backup = File.ReadAllBytes(fixture.Backup);
        var loaded = fixture.Store.Load();

        var result = fixture.Store.TrySave(loaded, loaded.Config!);

        Assert.Equal(ProtoEditorSettingsSaveState.Unchanged, result.State);
        Assert.Equal(before, File.ReadAllBytes(fixture.Path));
        Assert.Equal(backup, File.ReadAllBytes(fixture.Backup));
        Assert.Equal(0, fixture.Files.Stages);
        Assert.Equal(0, fixture.Files.Publications);
    }

    [Fact]
    public void ValidPrimaryReplacesAnInvalidBackupWithTheLastGoodBytes()
    {
        using var fixture = new Fixture();
        fixture.WritePrimary("{\"DataBarPath\":\"old.bar\"}");
        File.WriteAllText(fixture.Backup, "{");
        var before = File.ReadAllBytes(fixture.Path);
        var loaded = fixture.Store.Load();
        Assert.Equal(ProtoEditorSettingsLoadState.Valid, loaded.State);
        loaded.Config!.DataBarPath = "new.bar";

        Assert.True(fixture.Store.TrySave(loaded, loaded.Config).Succeeded);

        Assert.Equal(before, File.ReadAllBytes(fixture.Backup));
        Assert.Equal("new.bar", fixture.Store.Load().Config!.DataBarPath);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BackupRecoveryIsReadOnlyUntilExplicitlyConfirmed(bool primaryExists)
    {
        using var fixture = new Fixture();
        if (primaryExists) fixture.WritePrimary("null");
        File.WriteAllText(fixture.Backup, "{\"DataBarPath\":\"backup.bar\"}");
        var backup = File.ReadAllBytes(fixture.Backup);

        var loaded = fixture.Store.Load();

        Assert.Equal(ProtoEditorSettingsLoadState.Recovered, loaded.State);
        Assert.Equal("backup.bar", loaded.Config!.DataBarPath);
        Assert.Equal(fixture.Backup, loaded.RecoverySource);
        Assert.False(loaded.CanSave);
        Assert.Equal(ProtoEditorSettingsSaveState.Refused, fixture.Store.TrySave(loaded, loaded.Config).State);
        Assert.Throws<InvalidOperationException>(() => fixture.Store.Save(loaded.Config));
        Assert.Equal(primaryExists, File.Exists(fixture.Path));
        if (primaryExists) Assert.Equal("null", File.ReadAllText(fixture.Path));
        Assert.Equal(backup, File.ReadAllBytes(fixture.Backup));
        Assert.Equal(0, fixture.Files.Stages);
        Assert.Equal(0, fixture.Files.Publications);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConfirmedRecoveryKeepsGoodBackupAndPreservesDamagedPrimary(bool primaryExists)
    {
        using var fixture = new Fixture();
        byte[] damaged = [255, 123, 1];
        if (primaryExists) File.WriteAllBytes(fixture.Path, damaged);
        File.WriteAllText(fixture.Backup, "{\"DataBarPath\":\"backup.bar\"}");
        var backup = File.ReadAllBytes(fixture.Backup);
        var loaded = fixture.Store.Load();
        loaded.Config!.DataBarPath = "recovered.bar";

        var result = fixture.Store.TrySave(loaded, loaded.Config, confirmRecovery: true);

        Assert.Equal(ProtoEditorSettingsSaveState.Saved, result.State);
        Assert.Equal("recovered.bar", fixture.Store.Load().Config!.DataBarPath);
        Assert.Equal(backup, File.ReadAllBytes(fixture.Backup));
        if (primaryExists) Assert.Equal(damaged, File.ReadAllBytes(Assert.Single(result.RecoveryPaths)));
        else Assert.Empty(result.RecoveryPaths);
        Assert.All(fixture.Files.ReplacementBackups, path => Assert.Null(path));
        Assert.Empty(fixture.TemporaryFiles());
    }

    [Fact]
    public void TwoInvalidCopiesCannotBeOverwrittenByDefaultsOrRecoveryConfirmation()
    {
        using var fixture = new Fixture("{");
        File.WriteAllText(fixture.Backup, "null");
        var loaded = fixture.Store.Load();

        Assert.Equal(ProtoEditorSettingsLoadState.Invalid, loaded.State);
        Assert.Null(loaded.Config);
        Assert.Equal(ProtoEditorSettingsSaveState.Refused,
            fixture.Store.TrySave(loaded, new ProtoEditorConfig(), confirmRecovery: true).State);
        Assert.Throws<InvalidOperationException>(() => fixture.Store.Save(new ProtoEditorConfig()));
        Assert.Equal("{", File.ReadAllText(fixture.Path));
        Assert.Equal("null", File.ReadAllText(fixture.Backup));
        Assert.Equal(0, fixture.Files.Stages);
    }

    [Fact]
    public void OrphanedProfileBackupSelectsTheExistingSettingsLocationWithoutProbing()
    {
        using var fixture = new Fixture();
        var backup = System.IO.Path.Combine(fixture.Profile, ProtoEditorSettings.SettingsFilename + ".bak");
        File.WriteAllText(backup, "{\"DataBarPath\":\"profile.bar\"}");

        var loaded = fixture.Store.Load();

        Assert.Equal(ProtoEditorSettingsLoadState.Recovered, loaded.State);
        Assert.Equal(backup, loaded.RecoverySource);
        Assert.Equal("profile.bar", loaded.Config!.DataBarPath);
        Assert.Empty(fixture.Files.Probes);
        Assert.False(File.Exists(fixture.Path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InaccessiblePrimaryOrBackupIsNeverOverwritten(bool denyPrimary)
    {
        using var fixture = new Fixture("{\"DataBarPath\":\"primary.bar\"}");
        File.WriteAllText(fixture.Backup, "{\"DataBarPath\":\"backup.bar\"}");
        var primary = File.ReadAllBytes(fixture.Path);
        var backup = File.ReadAllBytes(fixture.Backup);
        fixture.Files.DeniedReads.Add(denyPrimary ? fixture.Path : fixture.Backup);
        var loaded = fixture.Store.Load();

        var result = fixture.Store.TrySave(loaded, loaded.Config!, confirmRecovery: true);

        Assert.False(result.Succeeded);
        Assert.Equal(denyPrimary ? ProtoEditorSettingsLoadState.Recovered : ProtoEditorSettingsLoadState.Valid, loaded.State);
        Assert.Equal(primary, File.ReadAllBytes(fixture.Path));
        Assert.Equal(backup, File.ReadAllBytes(fixture.Backup));
        Assert.Equal(0, fixture.Files.Stages);
    }

    [Fact]
    public void IndependentFieldsMergeAcrossStoresAndRetainUnknownAndLegacyValues()
    {
        const string json = "{\"DataBarPath\":\"old.bar\",\"UserFolderPath\":\"oldmods\",\"Future\":{\"keep\":7},\"CategoriesByModPath\":{\"mod\":{\"Categories\":[\"éλληνικά\"],\"UnitCategories\":{\"Hoplite\":\"éλληνικά\"},\"FutureNested\":42}}}";
        using var fixture = new Fixture(json);
        var other = fixture.NewStore();
        var first = fixture.Store.Load();
        var second = other.Load();
        first.Config!.DataBarPath = "new.bar";
        Assert.True(fixture.Store.TrySave(first, first.Config).Succeeded);
        second.Config!.LastModFilePath = "last.xml";

        var result = other.TrySave(second, second.Config);

        Assert.True(result.Succeeded, result.Error);
        var loaded = fixture.Store.Load();
        Assert.Equal("new.bar", loaded.Config!.DataBarPath);
        Assert.Equal("oldmods", loaded.Config.UserFolderPath);
        Assert.Equal("last.xml", loaded.Config.LastModFilePath);
        Assert.Equal("éλληνικά", loaded.Config.CategoriesByModPath!["mod"].UnitCategories["Hoplite"]);
        var root = JsonNode.Parse(File.ReadAllText(fixture.Path))!;
        Assert.Equal(7, root["Future"]!["keep"]!.GetValue<int>());
        Assert.Equal(42, root["CategoriesByModPath"]!["mod"]!["FutureNested"]!.GetValue<int>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OverlappingChangesConflictUnlessBothWritersChoseTheSameValue(bool sameValue)
    {
        using var fixture = new Fixture("{\"DataBarPath\":\"old.bar\"}");
        var other = fixture.NewStore();
        var first = fixture.Store.Load();
        var second = other.Load();
        first.Config!.DataBarPath = "first.bar";
        Assert.True(fixture.Store.TrySave(first, first.Config).Succeeded);
        var before = File.ReadAllBytes(fixture.Path);
        var backup = File.ReadAllBytes(fixture.Backup);
        var stages = fixture.Files.Stages;
        second.Config!.DataBarPath = sameValue ? "first.bar" : "second.bar";

        var result = other.TrySave(second, second.Config);

        Assert.Equal(sameValue ? ProtoEditorSettingsSaveState.Unchanged : ProtoEditorSettingsSaveState.Conflict, result.State);
        if (!sameValue) Assert.Contains("DataBarPath", result.Error);
        Assert.Equal(before, File.ReadAllBytes(fixture.Path));
        Assert.Equal(backup, File.ReadAllBytes(fixture.Backup));
        Assert.Equal(stages, fixture.Files.Stages);
    }

    [Fact]
    public void LegacyConfigIsRebasedAfterMergeAndCanDeliberatelyRevertLater()
    {
        using var fixture = new Fixture("{\"DataBarPath\":\"old.bar\",\"UserFolderPath\":\"oldmods\"}");
        var other = fixture.NewStore();
        var config = fixture.Store.Load().Config!;
        var elsewhere = other.Load().Config!;
        elsewhere.UserFolderPath = "newmods";
        other.Save(elsewhere);
        config.DataBarPath = "new.bar";
        fixture.Store.Save(config);
        Assert.Equal("newmods", config.UserFolderPath);
        config.DataBarPath = "old.bar";

        fixture.Store.Save(config);

        Assert.Equal("old.bar", fixture.Store.Load().Config!.DataBarPath);
        Assert.Equal("newmods", fixture.Store.Load().Config!.UserFolderPath);
    }

    [Fact]
    public void NewConfigWithoutLoadedBaselineCannotReplaceExistingSettings()
    {
        using var fixture = new Fixture("{\"DataBarPath\":\"keep.bar\",\"Future\":42}");

        Assert.Throws<InvalidOperationException>(() => fixture.Store.Save(new ProtoEditorConfig { DataBarPath = "replace.bar" }));

        Assert.Contains("keep.bar", File.ReadAllText(fixture.Path));
        Assert.Equal(0, fixture.Files.Stages);
    }

    [Fact]
    public void InitiallyMissingStoresMergeDifferentFieldsRatherThanOverwritingTheFirstSave()
    {
        using var fixture = new Fixture();
        var other = fixture.NewStore();
        var first = fixture.Store.Load();
        var second = other.Load();
        first.Config!.LastModFilePath = "first.xml";
        second.Config!.UserFolderPath = "mods";
        Assert.True(fixture.Store.TrySave(first, first.Config).Succeeded);

        Assert.True(other.TrySave(second, second.Config).Succeeded);

        Assert.Equal("first.xml", fixture.Store.Load().Config!.LastModFilePath);
        Assert.Equal("mods", fixture.Store.Load().Config!.UserFolderPath);
    }

    [Fact]
    public void ChangedRecoveryBackupRequiresNewConfirmation()
    {
        using var fixture = new Fixture("null");
        File.WriteAllText(fixture.Backup, "{\"DataBarPath\":\"before.bar\"}");
        var loaded = fixture.Store.Load();
        File.WriteAllText(fixture.Backup, "{\"DataBarPath\":\"after.bar\"}");

        var result = fixture.Store.TrySave(loaded, loaded.Config!, confirmRecovery: true);

        Assert.Equal(ProtoEditorSettingsSaveState.Conflict, result.State);
        Assert.Equal("null", File.ReadAllText(fixture.Path));
        Assert.Contains("after.bar", File.ReadAllText(fixture.Backup));
        Assert.Equal(0, fixture.Files.Stages);
    }

    [Fact]
    public void DeletedSettingsAreNotRecreatedFromAnOldBaseline()
    {
        using var fixture = new Fixture("{}");
        var loaded = fixture.Store.Load();
        loaded.Config!.DataBarPath = "stale.bar";
        File.Delete(fixture.Path);

        var result = fixture.Store.TrySave(loaded, loaded.Config);

        Assert.Equal(ProtoEditorSettingsSaveState.Conflict, result.State);
        Assert.False(File.Exists(fixture.Path));
        Assert.Equal(0, fixture.Files.Stages);
    }

    [Theory]
    [InlineData("primary")]
    [InlineData("backup")]
    [InlineData("alternate")]
    [InlineData("alternate-backup")]
    public void NewOrChangedFilesDuringStagingAreDetectedBeforePublication(string changed)
    {
        using var fixture = new Fixture();
        if (changed != "primary") fixture.WritePrimary("{}");
        var loaded = fixture.Store.Load();
        loaded.Config!.DataBarPath = "editor.bar";
        var externalPath = changed switch
        {
            "primary" => fixture.Path, "backup" => fixture.Backup,
            "alternate" => System.IO.Path.Combine(fixture.Profile, ProtoEditorSettings.SettingsFilename),
            _ => System.IO.Path.Combine(fixture.Profile, ProtoEditorSettings.SettingsFilename + ".bak")
        };
        fixture.Files.AfterStage = _ => File.WriteAllText(externalPath, "{\"DataBarPath\":\"external.bar\"}");

        var result = fixture.Store.TrySave(loaded, loaded.Config);

        Assert.Equal(ProtoEditorSettingsSaveState.Conflict, result.State);
        Assert.Contains("external.bar", File.ReadAllText(externalPath));
        Assert.Equal(0, fixture.Files.Publications);
        Assert.Empty(fixture.TemporaryFiles());
    }

    [Fact]
    public void SameLengthAndTimestampExternalChangeStillConflicts()
    {
        using var fixture = new Fixture("{\"DataBarPath\":\"old\"}");
        var loaded = fixture.Store.Load();
        var stamp = File.GetLastWriteTimeUtc(fixture.Path);
        loaded.Config!.DataBarPath = "new";
        fixture.Files.AfterStage = _ =>
        {
            File.WriteAllText(fixture.Path, "{\"DataBarPath\":\"ext\"}");
            File.SetLastWriteTimeUtc(fixture.Path, stamp);
        };

        var result = fixture.Store.TrySave(loaded, loaded.Config);

        Assert.Equal(ProtoEditorSettingsSaveState.Conflict, result.State);
        Assert.Equal(loaded.Baseline!.Length, new FileInfo(fixture.Path).Length);
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(fixture.Path));
        Assert.Contains("ext", File.ReadAllText(fixture.Path));
        Assert.Equal(0, fixture.Files.Publications);
    }

    [Fact]
    public void HeldOsLockReturnsBusyImmediatelyAndRetrySucceeds()
    {
        using var fixture = new Fixture("{}");
        var loaded = fixture.Store.Load();
        loaded.Config!.DataBarPath = "new.bar";
        using (var held = new ProtoEditorSettingsFileSystem().TryAcquireLock(fixture.Path + ".lock"))
        {
            Assert.NotNull(held);
            Assert.Equal(ProtoEditorSettingsSaveState.Busy, fixture.Store.TrySave(loaded, loaded.Config).State);
            Assert.Equal(0, fixture.Files.Stages);
            Assert.Equal(1, fixture.Files.LockAttempts);
        }

        Assert.True(fixture.Store.TrySave(loaded, loaded.Config).Succeeded);
        Assert.Equal(2, fixture.Files.LockAttempts);
    }

    [Fact]
    public void AnotherStoreCannotPublishWhileTheFirstHoldsTheLock()
    {
        using var fixture = new Fixture("{}");
        var other = fixture.NewStore();
        var first = fixture.Store.Load();
        var second = other.Load();
        first.Config!.DataBarPath = "first.bar";
        second.Config!.UserFolderPath = "mods";
        ProtoEditorSettingsSaveResult? concurrent = null;
        fixture.Files.AfterStage = _ => concurrent = other.TrySave(second, second.Config);

        Assert.True(fixture.Store.TrySave(first, first.Config).Succeeded);

        Assert.Equal(ProtoEditorSettingsSaveState.Busy, concurrent!.State);
        fixture.Files.AfterStage = null;
        Assert.True(other.TrySave(second, second.Config).Succeeded);
        Assert.Equal("first.bar", fixture.Store.Load().Config!.DataBarPath);
        Assert.Equal("mods", fixture.Store.Load().Config!.UserFolderPath);
    }

    [Theory]
    [InlineData("before")]
    [InlineData("partial")]
    [InlineData("flushed")]
    public void StagingFailuresLeaveOriginalAndBackupUntouched(string phase)
    {
        using var fixture = new Fixture("{}");
        File.WriteAllText(fixture.Backup, "{\"DataBarPath\":\"backup.bar\"}");
        var before = File.ReadAllBytes(fixture.Path);
        var backup = File.ReadAllBytes(fixture.Backup);
        var loaded = fixture.Store.Load();
        loaded.Config!.DataBarPath = "new.bar";
        fixture.Files.StageOverride = (path, bytes) =>
        {
            if (phase == "partial") File.WriteAllBytes(path, bytes[..3]);
            if (phase == "flushed") new ProtoEditorSettingsFileSystem().WriteNewAndFlush(path, bytes);
            throw new IOException("fixture staging failure");
        };

        var result = fixture.Store.TrySave(loaded, loaded.Config);

        Assert.Equal(ProtoEditorSettingsSaveState.Failed, result.State);
        Assert.Equal(before, File.ReadAllBytes(fixture.Path));
        Assert.Equal(backup, File.ReadAllBytes(fixture.Backup));
        Assert.Equal(0, fixture.Files.Publications);
        if (phase != "before")
        {
            var retained = Assert.Single(fixture.TemporaryFiles());
            Assert.Contains(retained, result.RecoveryPaths);
            if (phase == "flushed") Assert.Contains("new.bar", File.ReadAllText(retained));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UncertainReplacementKeepsRecoveryCandidatesAndAValidOldOrNewPrimary(bool afterReplacement)
    {
        using var fixture = new Fixture("{\"DataBarPath\":\"old.bar\"}");
        File.WriteAllText(fixture.Backup, "{\"DataBarPath\":\"previous.bar\"}");
        var original = File.ReadAllBytes(fixture.Path);
        var loaded = fixture.Store.Load();
        loaded.Config!.DataBarPath = "new.bar";
        fixture.Files.ReplaceOverride = (temp, target, backup) =>
        {
            if (afterReplacement) new ProtoEditorSettingsFileSystem().Replace(temp, target, backup);
            throw new IOException("fixture uncertain replacement");
        };

        var result = fixture.Store.TrySave(loaded, loaded.Config);

        Assert.Equal(ProtoEditorSettingsSaveState.Failed, result.State);
        Assert.Contains("may have completed", result.Error);
        Assert.Contains(fixture.Path, result.RecoveryPaths);
        Assert.Contains(fixture.Backup, result.RecoveryPaths);
        Assert.Equal(afterReplacement ? "new.bar" : "old.bar", fixture.Store.Load().Config!.DataBarPath);
        if (afterReplacement) Assert.Equal(original, File.ReadAllBytes(fixture.Backup));
        else Assert.Single(fixture.TemporaryFiles());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedRecoveryNeverRotatesBadPrimaryBytesOverGoodBackup(bool afterReplacement)
    {
        using var fixture = new Fixture("null");
        File.WriteAllText(fixture.Backup, "{\"DataBarPath\":\"backup.bar\"}");
        var backup = File.ReadAllBytes(fixture.Backup);
        var loaded = fixture.Store.Load();
        fixture.Files.ReplaceOverride = (temp, target, backupPath) =>
        {
            Assert.Null(backupPath);
            if (afterReplacement) new ProtoEditorSettingsFileSystem().Replace(temp, target, backupPath);
            throw new IOException("fixture recovery failure");
        };

        var result = fixture.Store.TrySave(loaded, loaded.Config!, confirmRecovery: true);

        Assert.Equal(ProtoEditorSettingsSaveState.Failed, result.State);
        Assert.Equal(backup, File.ReadAllBytes(fixture.Backup));
        var damaged = Assert.Single(result.RecoveryPaths, path => path.EndsWith(".recovery", StringComparison.Ordinal));
        Assert.Equal("null", File.ReadAllText(damaged));
        Assert.Equal("backup.bar", fixture.Store.Load().Config!.DataBarPath);
    }

    [Fact]
    public void CorruptedStagingOutputCannotBePublished()
    {
        using var fixture = new Fixture("{}");
        var loaded = fixture.Store.Load();
        loaded.Config!.DataBarPath = "new.bar";
        fixture.Files.AfterStage = path => File.WriteAllText(path, "{");

        var result = fixture.Store.TrySave(loaded, loaded.Config);

        Assert.Equal(ProtoEditorSettingsSaveState.Failed, result.State);
        Assert.Equal("{}", File.ReadAllText(fixture.Path));
        Assert.Equal(0, fixture.Files.Publications);
        Assert.Empty(fixture.TemporaryFiles());
    }

    [Fact]
    public void FileCreatedAtTheMoveRaceIsNotOverwritten()
    {
        using var fixture = new Fixture();
        var loaded = fixture.Store.Load();
        loaded.Config!.DataBarPath = "editor.bar";
        fixture.Files.BeforeMove = () => File.WriteAllText(fixture.Path, "{\"DataBarPath\":\"external.bar\"}");

        var result = fixture.Store.TrySave(loaded, loaded.Config);

        Assert.Equal(ProtoEditorSettingsSaveState.Failed, result.State);
        Assert.Equal("external.bar", fixture.Store.Load().Config!.DataBarPath);
        Assert.Single(fixture.TemporaryFiles());
    }

    [Theory]
    [InlineData("utf8")]
    [InlineData("utf8-bom")]
    [InlineData("utf16-le")]
    [InlineData("utf16-be")]
    [InlineData("utf32-le")]
    [InlineData("utf32-be")]
    public void ReplacementPreservesEncodingBomNewlinesAndExactBackup(string kind)
    {
        using var fixture = new Fixture();
        Encoding encoding = kind switch
        {
            "utf16-le" => Encoding.Unicode, "utf16-be" => Encoding.BigEndianUnicode,
            "utf32-le" => new UTF32Encoding(false, true), "utf32-be" => new UTF32Encoding(true, true),
            "utf8-bom" => new UTF8Encoding(true), _ => new UTF8Encoding(false)
        };
        const string json = "{\r\n  \"DataBarPath\": \"神話.bar\",\r\n  \"Future\": 42\r\n}\r\n";
        var original = encoding.GetPreamble().Concat(encoding.GetBytes(json)).ToArray();
        File.WriteAllBytes(fixture.Path, original);
        var loaded = fixture.Store.Load();
        loaded.Config!.UserFolderPath = "mods";

        var result = fixture.Store.TrySave(loaded, loaded.Config);

        Assert.True(result.Succeeded, result.Error);
        var decoded = PreservedTextEncoding.Decode(File.ReadAllBytes(fixture.Path));
        Assert.Equal(encoding.WebName, decoded.Encoding.WebName);
        Assert.Equal(encoding.GetPreamble(), decoded.Preamble);
        Assert.Contains("\r\n", decoded.Text);
        Assert.DoesNotContain("\n", decoded.Text.Replace("\r\n", "", StringComparison.Ordinal));
        Assert.EndsWith("\r\n", decoded.Text);
        Assert.Equal(original, File.ReadAllBytes(fixture.Backup));
        Assert.Equal(42, JsonNode.Parse(decoded.Text)!["Future"]!.GetValue<int>());
        Assert.Equal("神話.bar", fixture.Store.Load().Config!.DataBarPath);
    }

    [Fact]
    public void EditingLegacyCategoriesPreservesUnknownNestedMembers()
    {
        using var fixture = new Fixture("{\"CategoriesByModPath\":{\"mod\":{\"Categories\":[\"old\"],\"UnitCategories\":{},\"Future\":42}}}");
        var loaded = fixture.Store.Load();
        loaded.Config!.CategoriesByModPath!["mod"].Categories.Add("new");

        Assert.True(fixture.Store.TrySave(loaded, loaded.Config).Succeeded);

        Assert.Equal(42, JsonNode.Parse(File.ReadAllText(fixture.Path))!["CategoriesByModPath"]!["mod"]!["Future"]!.GetValue<int>());
        Assert.Equal(new[] { "old", "new" }, fixture.Store.Load().Config!.CategoriesByModPath!["mod"].Categories);
    }

    [Fact]
    public void UnserializableEditFailsBeforeStagingAndDoesNotChangeDisk()
    {
        using var fixture = new Fixture("{}");
        var loaded = fixture.Store.Load();
        loaded.Config!.DataBarMtime = double.NaN;

        var result = fixture.Store.TrySave(loaded, loaded.Config);

        Assert.Equal(ProtoEditorSettingsSaveState.Failed, result.State);
        Assert.Equal("{}", File.ReadAllText(fixture.Path));
        Assert.Equal(0, fixture.Files.Stages);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly TestWorkspace _workspace = new("settings-persistence");
        internal readonly FaultFiles Files = new();
        internal string Application { get; }
        internal string Profile { get; }
        internal string Path => System.IO.Path.Combine(Application, ProtoEditorSettings.SettingsFilename);
        internal string Backup => Path + ".bak";
        internal ProtoEditorSettingsStore Store { get; }

        internal Fixture(string? json = null)
        {
            Application = _workspace.CreateDirectory("app");
            Profile = _workspace.CreateDirectory("profile");
            if (json != null) WritePrimary(json);
            Store = NewStore();
        }

        internal ProtoEditorSettingsStore NewStore() => new(Application, Profile, Files);
        internal void WritePrimary(string json) => File.WriteAllText(Path, json, new UTF8Encoding(false));
        internal string[] TemporaryFiles() => Directory.GetFiles(Application, "*.tmp");
        public void Dispose() => _workspace.Dispose();
    }

    private sealed class FaultFiles : IProtoEditorSettingsFileSystem
    {
        private readonly ProtoEditorSettingsFileSystem _disk = new();
        internal readonly List<string> Probes = [];
        internal readonly List<string?> ReplacementBackups = [];
        internal readonly HashSet<string> DeniedReads = new(StringComparer.OrdinalIgnoreCase);
        internal Action<string>? AfterStage;
        internal Action<string, byte[]>? StageOverride;
        internal Action<string, string, string?>? ReplaceOverride;
        internal Action? BeforeMove;
        internal int Stages, Publications, LockAttempts;
        public FileAttributes GetAttributes(string path) => _disk.GetAttributes(path);
        public byte[] ReadAllBytes(string path)
        {
            if (DeniedReads.Contains(path)) throw new UnauthorizedAccessException("fixture denied read");
            return _disk.ReadAllBytes(path);
        }
        public void ProbeWritableDirectory(string directory) { Probes.Add(directory); _disk.ProbeWritableDirectory(directory); }
        public IDisposable? TryAcquireLock(string path) { LockAttempts++; return _disk.TryAcquireLock(path); }
        public void WriteNewAndFlush(string path, byte[] contents)
        {
            Stages++;
            if (StageOverride != null) StageOverride(path, contents); else _disk.WriteNewAndFlush(path, contents);
            AfterStage?.Invoke(path);
        }
        public void Replace(string temporaryPath, string targetPath, string? backupPath)
        {
            Publications++;
            ReplacementBackups.Add(backupPath);
            if (ReplaceOverride != null) ReplaceOverride(temporaryPath, targetPath, backupPath);
            else _disk.Replace(temporaryPath, targetPath, backupPath);
        }
        public void MoveNew(string temporaryPath, string targetPath)
        {
            Publications++;
            BeforeMove?.Invoke();
            _disk.MoveNew(temporaryPath, targetPath);
        }
        public void Delete(string path) => _disk.Delete(path);
    }
}
