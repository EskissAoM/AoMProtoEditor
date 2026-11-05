using System.Security.Cryptography;
using System.Text;
using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class ProtoEditorSettingsStoreTests
{
    [Theory]
    [InlineData("profile")]
    [InlineData("app")]
    public void SoleExistingFileIsUsedWithoutAWriteProbe(string folder)
    {
        using var workspace = new TestWorkspace();
        var store = CreateStore(workspace, out var files);
        var path = workspace.WriteText($"{folder}/{ProtoEditorSettings.SettingsFilename}", "{\"DataBarPath\":\"existing.bar\"}");
        var before = File.ReadAllBytes(path);
        if (folder == "app")
            files.UnwritableDirectories.UnionWith([workspace.GetPath("app"), workspace.GetPath("profile")]);

        var result = store.Load();

        Assert.Equal(ProtoEditorSettingsLoadState.Valid, result.State);
        Assert.Equal(path, result.Source);
        Assert.Equal("existing.bar", result.Config!.DataBarPath);
        Assert.True(result.CanSave);
        Assert.Empty(files.Probes);
        Assert.Equal(0, files.Writes);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NewSettingsChooseOneWritableLocationAndDoNotProbeAgain(bool denyApplication)
    {
        using var workspace = new TestWorkspace();
        var store = CreateStore(workspace, out var files);
        if (denyApplication) files.UnwritableDirectories.Add(workspace.GetPath("app"));
        var first = store.Load();
        files.UnwritableDirectories.Clear();
        var second = store.Load();

        Assert.Equal(ProtoEditorSettingsLoadState.Missing, first.State);
        Assert.Equal(SettingsPath(workspace, denyApplication ? "profile" : "app"), first.Source);
        Assert.Equal(first.Source, second.Source);
        Assert.NotSame(first.Config, second.Config);
        Assert.False(first.Baseline!.Exists);
        Assert.Equal(0, first.Baseline.Length);
        Assert.Null(first.Baseline.Json);
        Assert.True(first.CanSave);
        Assert.Equal(denyApplication ? 2 : 1, files.Probes.Count);
        Assert.Equal(0, files.Writes);
        Assert.Empty(Directory.GetFiles(workspace.RootPath, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public void NoWritableDestinationIsUnavailableAndCanBeRetriedBeforeSelection()
    {
        using var workspace = new TestWorkspace();
        var store = CreateStore(workspace, out var files);
        files.UnwritableDirectories.UnionWith([workspace.GetPath("app"), workspace.GetPath("profile")]);

        var failed = store.Load();

        Assert.Equal(ProtoEditorSettingsLoadState.Unavailable, failed.State);
        Assert.Null(failed.Config);
        Assert.False(failed.CanSave);
        Assert.Contains(SettingsPath(workspace, "app"), failed.Error);
        Assert.Contains(SettingsPath(workspace, "profile"), failed.Error);
        files.UnwritableDirectories.Remove(workspace.GetPath("profile"));
        var retry = store.Load();
        Assert.Equal(ProtoEditorSettingsLoadState.Missing, retry.State);
        Assert.Equal(SettingsPath(workspace, "profile"), retry.Source);
        Assert.Empty(Directory.GetFiles(workspace.RootPath, "*", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TwoExistingFilesAreAmbiguousEvenWhenTheirContentsMatch(bool sameContents)
    {
        using var workspace = new TestWorkspace();
        var store = CreateStore(workspace, out var files);
        var app = workspace.WriteText($"app/{ProtoEditorSettings.SettingsFilename}", "{\"DataBarPath\":\"app.bar\"}");
        var profile = workspace.WriteText($"profile/{ProtoEditorSettings.SettingsFilename}",
            sameContents ? File.ReadAllText(app) : "{\"DataBarPath\":\"profile.bar\"}");
        var appBefore = File.ReadAllBytes(app);
        var profileBefore = File.ReadAllBytes(profile);

        var result = store.Load();

        Assert.Equal(ProtoEditorSettingsLoadState.Ambiguous, result.State);
        Assert.Null(result.Config);
        Assert.False(result.CanSave);
        Assert.Equal(new[] { app, profile }, result.CandidatePaths);
        Assert.Contains(app, result.Source);
        Assert.Contains(profile, result.Source);
        Assert.Throws<InvalidOperationException>(() => store.Save(new ProtoEditorConfig()));
        Assert.Empty(files.Probes);
        Assert.Equal(0, files.Reads);
        Assert.Equal(0, files.Writes);
        Assert.Equal(appBefore, File.ReadAllBytes(app));
        Assert.Equal(profileBefore, File.ReadAllBytes(profile));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("\"wrong root\"")]
    [InlineData("{\"DataBarMtime\":\"wrong type\"}")]
    [InlineData("")]
    public void InvalidJsonHasNoUsableConfigAndCannotBeOverwrittenWithDefaults(string json)
    {
        using var workspace = new TestWorkspace();
        var store = CreateStore(workspace, out var files);
        var path = workspace.WriteText($"app/{ProtoEditorSettings.SettingsFilename}", json);
        var before = File.ReadAllBytes(path);

        var result = store.Load();

        Assert.Equal(ProtoEditorSettingsLoadState.Invalid, result.State);
        Assert.Equal(path, result.Source);
        Assert.Null(result.Config);
        Assert.False(result.CanSave);
        Assert.True(result.Baseline!.Exists);
        Assert.Equal(json, result.Baseline.Json);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
        Assert.Throws<InvalidOperationException>(() => store.Save(new ProtoEditorConfig()));
        Assert.Equal(0, files.Writes);
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Empty(files.Probes);
    }

    [Fact]
    public void InvalidUtf8IsInvalidRatherThanSilentlyReplacingCharacters()
    {
        using var workspace = new TestWorkspace();
        var store = CreateStore(workspace, out _);
        var path = SettingsPath(workspace, "app");
        byte[] bytes = [123, 34, 68, 97, 116, 97, 66, 97, 114, 80, 97, 116, 104, 34, 58, 34, 255, 34, 125];
        File.WriteAllBytes(path, bytes);

        var result = store.Load();

        Assert.Equal(ProtoEditorSettingsLoadState.Invalid, result.State);
        Assert.Null(result.Config);
        Assert.Null(result.Baseline!.Json);
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void AnEmptyObjectIsValidAndOptionalSettingsRemainOptional()
    {
        using var workspace = new TestWorkspace();
        var store = CreateStore(workspace, out _);
        workspace.WriteText($"app/{ProtoEditorSettings.SettingsFilename}", "{}");

        var result = store.Load();

        Assert.Equal(ProtoEditorSettingsLoadState.Valid, result.State);
        Assert.Null(result.Config!.DataBarPath);
        Assert.Null(result.Config.CategoriesByModPath);
        Assert.Equal(0, result.Config.DataBarMtime);
        Assert.True(result.Baseline!.Exists);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadFailureIsUnavailableAndRetryUsesTheSameFile(bool accessDenied)
    {
        using var workspace = new TestWorkspace();
        var store = CreateStore(workspace, out var files);
        var path = workspace.WriteText($"app/{ProtoEditorSettings.SettingsFilename}", "{}");
        var before = File.ReadAllBytes(path);
        files.ReadFailure = accessDenied ? new UnauthorizedAccessException("fixture denied read") : new IOException("fixture locked read");

        var result = store.Load();

        Assert.Equal(ProtoEditorSettingsLoadState.Unavailable, result.State);
        Assert.Equal(path, result.Source);
        Assert.Null(result.Config);
        Assert.Null(result.Baseline);
        Assert.Contains(accessDenied ? "denied" : "locked", result.Error);
        Assert.Throws<InvalidOperationException>(() => store.Save(new ProtoEditorConfig()));
        files.ReadFailure = null;
        Assert.Equal(ProtoEditorSettingsLoadState.Valid, store.Load().State);
        Assert.Empty(files.Probes);
        Assert.Equal(0, files.Writes);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void InaccessibleCandidateIsNotTreatedAsAnAbsentOptionalFile()
    {
        using var workspace = new TestWorkspace();
        var store = CreateStore(workspace, out var files);
        var profile = workspace.WriteText($"profile/{ProtoEditorSettings.SettingsFilename}", "{}");
        files.InspectionDenied.Add(profile);

        var result = store.Load();

        Assert.Equal(ProtoEditorSettingsLoadState.Unavailable, result.State);
        Assert.Equal(profile, result.Source);
        Assert.False(result.CanSave);
        Assert.Empty(files.Probes);
        files.InspectionDenied.Clear();
        Assert.Equal(profile, store.Load().Source);
        Assert.Equal("{}", File.ReadAllText(profile));
    }

    [Fact]
    public void DirectoryAtSettingsFilenameIsUnavailableAndNotOverwritten()
    {
        using var workspace = new TestWorkspace();
        var store = CreateStore(workspace, out var files);
        var path = workspace.CreateDirectory($"app/{ProtoEditorSettings.SettingsFilename}");

        var result = store.Load();

        Assert.Equal(ProtoEditorSettingsLoadState.Unavailable, result.State);
        Assert.Equal(path, result.Source);
        Assert.Contains("directory", result.Error);
        Assert.Throws<InvalidOperationException>(() => store.Save(new ProtoEditorConfig()));
        Assert.True(Directory.Exists(path));
        Assert.Empty(files.Probes);
        Assert.Equal(0, files.Writes);
    }

    [Fact]
    public void FailedParseDoesNotPoisonRetryAtTheSameLengthAndTimestamp()
    {
        using var workspace = new TestWorkspace();
        var store = CreateStore(workspace, out var files);
        var path = workspace.WriteText($"app/{ProtoEditorSettings.SettingsFilename}", "null");
        var stamp = File.GetLastWriteTimeUtc(path);
        Assert.Equal(ProtoEditorSettingsLoadState.Invalid, store.Load().State);
        File.WriteAllText(path, "{}  ", new UTF8Encoding(false));
        File.SetLastWriteTimeUtc(path, stamp);

        var retry = store.Load();

        Assert.Equal(ProtoEditorSettingsLoadState.Valid, retry.State);
        Assert.Equal(4, retry.Baseline!.Length);
        Assert.Equal(path, retry.Source);
        Assert.Equal(2, files.Reads);
        Assert.Empty(files.Probes);
    }

    [Theory]
    [InlineData("utf8-bom")]
    [InlineData("utf16-le")]
    [InlineData("utf16-be")]
    public void MalformedBomEncodedJsonDoesNotBecomeAValidReplacementCharacter(string kind)
    {
        using var workspace = new TestWorkspace();
        var store = CreateStore(workspace, out var files);
        Encoding encoding = kind switch { "utf16-le" => Encoding.Unicode, "utf16-be" => Encoding.BigEndianUnicode, _ => new UTF8Encoding(true) };
        const string json = "{\"DataBarPath\":\"X\"}";
        var body = encoding.GetBytes(json);
        var offset = encoding.GetByteCount(json[..json.IndexOf('X')]);
        if (kind == "utf8-bom") body[offset] = 255;
        else
        {
            body[offset] = kind == "utf16-le" ? (byte)0 : (byte)216;
            body[offset + 1] = kind == "utf16-le" ? (byte)216 : (byte)0;
        }
        var bytes = encoding.GetPreamble().Concat(body).ToArray();
        var path = SettingsPath(workspace, "app");
        File.WriteAllBytes(path, bytes);

        var result = store.Load();

        Assert.Equal(ProtoEditorSettingsLoadState.Invalid, result.State);
        Assert.Null(result.Config);
        Assert.False(result.CanSave);
        Assert.Throws<InvalidOperationException>(() => store.Save(new ProtoEditorConfig()));
        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.Equal(0, files.Writes);
    }

    [Theory]
    [InlineData("utf8-bom")]
    [InlineData("utf16-le")]
    [InlineData("utf16-be")]
    public void ExistingBomEncodingsAndUnicodeAreReadWithoutRewriting(string kind)
    {
        using var workspace = new TestWorkspace();
        var store = CreateStore(workspace, out var files);
        Encoding encoding = kind switch { "utf16-le" => Encoding.Unicode, "utf16-be" => Encoding.BigEndianUnicode, _ => new UTF8Encoding(true) };
        const string json = "{\"DataBarPath\":\"神話-é.bar\"}";
        var bytes = encoding.GetPreamble().Concat(encoding.GetBytes(json)).ToArray();
        var path = SettingsPath(workspace, "app");
        File.WriteAllBytes(path, bytes);

        var result = store.Load();

        Assert.Equal(ProtoEditorSettingsLoadState.Valid, result.State);
        Assert.Equal("神話-é.bar", result.Config!.DataBarPath);
        Assert.Equal(json, result.Baseline!.Json);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)), result.Baseline.Sha256);
        Assert.Equal(bytes.LongLength, result.Baseline.Length);
        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.Equal(0, files.Writes);
        Assert.Empty(files.Probes);
    }

    [Fact]
    public void NestedLegacyFieldsAndBaselinesAreIndependentAcrossLoads()
    {
        using var workspace = new TestWorkspace();
        var store = CreateStore(workspace, out _);
        const string json = "{\"DataBarPath\":\"game.bar\",\"DataBarMtime\":42,\"CategoriesByModPath\":{\"mod\":{\"Categories\":[\"éλληνικά\"],\"UnitCategories\":{\"Hoplite\":\"éλληνικά\"}}}}";
        var path = workspace.WriteText($"app/{ProtoEditorSettings.SettingsFilename}", json);
        var first = store.Load();
        first.Config!.DataBarPath = "mutated.bar";
        first.Config.CategoriesByModPath!["mod"].Categories.Add("new");
        first.Config.CategoriesByModPath["mod"].UnitCategories["Hoplite"] = "changed";

        var second = store.Load();

        Assert.Equal("game.bar", second.Config!.DataBarPath);
        Assert.Equal(42, second.Config.DataBarMtime);
        Assert.Equal(new[] { "éλληνικά" }, second.Config.CategoriesByModPath!["mod"].Categories);
        Assert.Equal("éλληνικά", second.Config.CategoriesByModPath["mod"].UnitCategories["Hoplite"]);
        Assert.NotSame(first.Config, second.Config);
        Assert.NotSame(first.Config.CategoriesByModPath, second.Config.CategoriesByModPath);
        Assert.Equal(json, first.Baseline!.Json);
        Assert.Equal(first.Baseline, second.Baseline);
        Assert.Equal(json, File.ReadAllText(path));
    }

    [Fact]
    public void RemovingTheSelectedFileDoesNotSwitchToAnotherLocation()
    {
        using var workspace = new TestWorkspace();
        var store = CreateStore(workspace, out var files);
        var selected = workspace.WriteText($"app/{ProtoEditorSettings.SettingsFilename}", "{}");
        Assert.Equal(selected, store.Load().Source);
        File.Delete(selected);
        var alternate = workspace.WriteText($"profile/{ProtoEditorSettings.SettingsFilename}", "{\"DataBarPath\":\"other.bar\"}");

        var result = store.Load();

        Assert.Equal(ProtoEditorSettingsLoadState.Missing, result.State);
        Assert.Equal(selected, result.Source);
        Assert.Null(result.Config!.DataBarPath);
        Assert.Equal("{\"DataBarPath\":\"other.bar\"}", File.ReadAllText(alternate));
        Assert.Empty(files.Probes);
    }

    [Fact]
    public void LosingWriteAccessDoesNotSaveToTheProfileInstead()
    {
        using var workspace = new TestWorkspace();
        var store = CreateStore(workspace, out var files);
        var selected = workspace.WriteText($"app/{ProtoEditorSettings.SettingsFilename}", "{}");
        var loaded = store.Load();
        files.UnwritableDirectories.Add(workspace.GetPath("app"));
        loaded.Config!.DataBarPath = "new.bar";

        Assert.Throws<UnauthorizedAccessException>(() => store.Save(loaded.Config));

        Assert.Equal("{}", File.ReadAllText(selected));
        Assert.False(File.Exists(SettingsPath(workspace, "profile")));
        Assert.Equal(selected, store.Load().Source);
        Assert.Empty(files.Probes);
    }

    [Fact]
    public void SameApplicationAndProfileDirectoryIsOneCandidate()
    {
        using var workspace = new TestWorkspace();
        var directory = workspace.CreateDirectory("shared");
        var files = new TrackingFiles();
        var store = new ProtoEditorSettingsStore(directory, directory, files);

        var first = store.Load();
        var second = store.Load();

        Assert.Single(first.CandidatePaths);
        Assert.Single(files.Probes);
        Assert.Equal(first.Source, second.Source);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnAlternateFileAppearingAfterSelectionBlocksSaveWithoutSwitching(bool selectedFileExists)
    {
        using var workspace = new TestWorkspace();
        var store = CreateStore(workspace, out var files);
        var app = SettingsPath(workspace, "app");
        if (selectedFileExists) File.WriteAllText(app, "{}");
        var loaded = store.Load();
        var profile = workspace.WriteText($"profile/{ProtoEditorSettings.SettingsFilename}", "{\"DataBarPath\":\"external.bar\"}");
        var profileBefore = File.ReadAllBytes(profile);

        var exception = Assert.Throws<InvalidOperationException>(() => store.Save(loaded.Config!));

        Assert.Contains(profile, exception.Message);
        Assert.Equal(app, store.Load().Source);
        Assert.Equal(selectedFileExists, File.Exists(app));
        if (selectedFileExists) Assert.Equal("{}", File.ReadAllText(app));
        Assert.Equal(profileBefore, File.ReadAllBytes(profile));
        Assert.Equal(0, files.Writes);
    }

    [Fact]
    public void ValidAndNewSettingsUseThePinnedPathForCompatibleSaves()
    {
        using var workspace = new TestWorkspace();
        var store = CreateStore(workspace, out var files);
        var config = store.Load().Config!;
        config.DataBarPath = "portable.bar";
        config.UserFolderPath = "mods";
        config.LastModFilePath = "last.xml";
        config.CategoriesByModPath = new() { ["mod"] = new() { Categories = ["legacy"], UnitCategories = new() { ["Hoplite"] = "legacy" } } };

        store.Save(config);
        var result = store.Load();
        result.Config!.DataBarPath = "changed.bar";
        store.Save(result.Config);
        var saved = store.Load();

        Assert.Equal(SettingsPath(workspace, "app"), saved.Source);
        Assert.Equal("changed.bar", saved.Config!.DataBarPath);
        Assert.Equal("mods", saved.Config.UserFolderPath);
        Assert.Equal("last.xml", saved.Config.LastModFilePath);
        Assert.Equal("legacy", Assert.Single(saved.Config.CategoriesByModPath!["mod"].Categories));
        Assert.Equal("legacy", saved.Config.CategoriesByModPath["mod"].UnitCategories["Hoplite"]);
        Assert.Single(files.Probes);
        Assert.Equal(2, files.Writes);
        Assert.False(File.Exists(SettingsPath(workspace, "profile")));
    }

    private static ProtoEditorSettingsStore CreateStore(TestWorkspace workspace, out TrackingFiles files)
    {
        files = new TrackingFiles();
        return new(workspace.CreateDirectory("app"), workspace.CreateDirectory("profile"), files);
    }

    private static string SettingsPath(TestWorkspace workspace, string folder)
        => workspace.GetPath($"{folder}/{ProtoEditorSettings.SettingsFilename}");

    private sealed class TrackingFiles : IProtoEditorSettingsFileSystem
    {
        private readonly ProtoEditorSettingsFileSystem _disk = new();
        internal readonly List<string> Probes = [];
        internal readonly HashSet<string> UnwritableDirectories = new(StringComparer.OrdinalIgnoreCase);
        internal readonly HashSet<string> InspectionDenied = new(StringComparer.OrdinalIgnoreCase);
        internal Exception? ReadFailure;
        internal int Reads, Writes;

        public FileAttributes GetAttributes(string path)
        {
            if (InspectionDenied.Contains(path)) throw new UnauthorizedAccessException("fixture denied inspection");
            return _disk.GetAttributes(path);
        }

        public byte[] ReadAllBytes(string path)
        {
            Reads++;
            if (ReadFailure != null) throw ReadFailure;
            return _disk.ReadAllBytes(path);
        }

        public void ProbeWritableDirectory(string directory)
        {
            Probes.Add(directory);
            if (UnwritableDirectories.Contains(directory)) throw new UnauthorizedAccessException("fixture denied write probe");
            _disk.ProbeWritableDirectory(directory);
        }

        public IDisposable? TryAcquireLock(string path) => _disk.TryAcquireLock(path);
        public void Replace(string temporaryPath, string targetPath, string? backupPath)
            => _disk.Replace(temporaryPath, targetPath, backupPath);
        public void MoveNew(string temporaryPath, string targetPath) => _disk.MoveNew(temporaryPath, targetPath);
        public void Delete(string path) => _disk.Delete(path);

        public void WriteNewAndFlush(string path, byte[] contents)
        {
            if (UnwritableDirectories.Contains(Path.GetDirectoryName(path)!)) throw new UnauthorizedAccessException("fixture denied write");
            Writes++;
            _disk.WriteNewAndFlush(path, contents);
        }
    }
}
