using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using AoMDivineDataEditor.GameData;

namespace AoMDivineDataEditor.Classes;

public enum ProtoEditorSettingsLoadState { Missing, Valid, Invalid, Unavailable, Ambiguous, Recovered }

/// <summary>Immutable loaded-file baseline; the editable config is a separate object.</summary>
public sealed record ProtoEditorSettingsBaseline(
    string Path, bool Exists, long Length, string Sha256, string? Json);

public sealed record ProtoEditorSettingsLoadResult(
    ProtoEditorSettingsLoadState State, string Source, ProtoEditorConfig? Config,
    ProtoEditorSettingsBaseline? Baseline, string? Error, IReadOnlyList<string> CandidatePaths)
{
    public bool CanSave => State is ProtoEditorSettingsLoadState.Valid or ProtoEditorSettingsLoadState.Missing;
    public ProtoEditorSettingsBaseline? BackupBaseline { get; init; }
    public string? RecoverySource { get; init; }
    public long SourceVersion { get; init; }
}

public enum ProtoEditorSettingsSaveState { Saved, Unchanged, Conflict, Busy, Refused, Failed }

public sealed record ProtoEditorSettingsSaveResult(
    ProtoEditorSettingsSaveState State, string Source, string? Error,
    ProtoEditorSettingsLoadResult? Current, IReadOnlyList<string> RecoveryPaths)
{
    public bool Succeeded => State is ProtoEditorSettingsSaveState.Saved or ProtoEditorSettingsSaveState.Unchanged;
    internal Exception? Cause { get; init; }
}

internal interface IProtoEditorSettingsFileSystem
{
    FileAttributes GetAttributes(string path);
    byte[] ReadAllBytes(string path);
    void ProbeWritableDirectory(string directory);
    IDisposable? TryAcquireLock(string path);
    void WriteNewAndFlush(string path, byte[] contents);
    void Replace(string temporaryPath, string targetPath, string? backupPath);
    void MoveNew(string temporaryPath, string targetPath);
    void Delete(string path);
}

internal sealed class ProtoEditorSettingsFileSystem : IProtoEditorSettingsFileSystem
{
    public FileAttributes GetAttributes(string path) => File.GetAttributes(path);
    public byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);
    public IDisposable? TryAcquireLock(string path)
    {
        try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException exception) when ((exception.HResult & 0xffff) is 32 or 33) { return null; }
    }

    public void WriteNewAndFlush(string path, byte[] contents)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(contents);
        stream.Flush(flushToDisk: true);
    }

    public void Replace(string temporaryPath, string targetPath, string? backupPath)
        => File.Replace(temporaryPath, targetPath, backupPath);
    public void MoveNew(string temporaryPath, string targetPath) => File.Move(temporaryPath, targetPath);
    public void Delete(string path) => File.Delete(path);

    public void ProbeWritableDirectory(string directory)
    {
        var probe = Path.Combine(directory, $".aom-settings-probe-{Guid.NewGuid():N}.tmp");
        using var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.ReadWrite,
            FileShare.None, 1, FileOptions.DeleteOnClose);
    }
}

/// <summary>Settings-only location/load boundary. Other editor metadata retains its existing paths.</summary>
internal sealed class ProtoEditorSettingsStore
{
    private readonly IProtoEditorSettingsFileSystem _files;
    private readonly IReadOnlyList<string> _candidates;
    private readonly object _locationGate = new();
    private readonly ConditionalWeakTable<ProtoEditorConfig, ProtoEditorSettingsLoadResult> _loadedConfigs = new();
    private string? _selectedPath;
    private long _sourceVersion;
    private Dictionary<string, ProtoEditorSettingsBaseline>? _acknowledgedAlternates;
    private ProtoEditorSettingsLoadResult? _cachedRead;

    private sealed record Snapshot(ProtoEditorSettingsBaseline? Baseline, byte[]? Bytes,
        ProtoEditorConfig? Config, Exception? Error);
    private sealed record SnapshotPair(Snapshot Primary, Snapshot Backup, ProtoEditorSettingsLoadResult Result);

    internal ProtoEditorSettingsStore(string applicationDirectory, string profileDirectory,
        IProtoEditorSettingsFileSystem? files = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileDirectory);
        _files = files ?? new ProtoEditorSettingsFileSystem();
        _candidates = Array.AsReadOnly(new[]
        {
            Path.Combine(Path.GetFullPath(applicationDirectory), ProtoEditorSettings.SettingsFilename),
            Path.Combine(Path.GetFullPath(profileDirectory), ProtoEditorSettings.SettingsFilename)
        }.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    internal ProtoEditorSettingsLoadResult Load()
    {
        lock (_locationGate)
        {
            var location = ResolveLocation();
            var result = location.Failure ?? Track(ReadPair(location.Path!).Result);
            RememberRead(result);
            return result;
        }
    }

    // One validated snapshot only. Return independent editable objects; failed
    // reads are never retained as default settings. Load() is the explicit refresh.
    internal ProtoEditorSettingsLoadResult LoadCached()
    {
        lock (_locationGate)
            return _cachedRead == null ? Load() : Track(CloneResult(_cachedRead));
    }

    internal ProtoEditorSettingsLoadResult SelectSource(string path)
    {
        lock (_locationGate)
        {
            var candidate = _candidates.FirstOrDefault(value => value.Equals(path, StringComparison.OrdinalIgnoreCase));
            if (candidate == null)
                return Failure(ProtoEditorSettingsLoadState.Unavailable, path, "Choose one of the listed settings sources.");
            var chosen = ReadPair(candidate);
            if (chosen.Result.State == ProtoEditorSettingsLoadState.Unavailable) return chosen.Result;
            var acknowledgements = new Dictionary<string, ProtoEditorSettingsBaseline>(StringComparer.OrdinalIgnoreCase);
            foreach (var alternate in _candidates.Where(value => value != candidate)
                         .SelectMany(value => new[] { value, value + ".bak" }))
            {
                var snapshot = ReadSnapshot(alternate);
                if (snapshot.Baseline == null)
                    return Failure(ProtoEditorSettingsLoadState.Unavailable, alternate, snapshot.Error!.Message);
                acknowledgements[alternate] = snapshot.Baseline;
            }
            _selectedPath = candidate;
            _acknowledgedAlternates = acknowledgements;
            _sourceVersion++;
            // Adopt the validated choice without a second read that could fail
            // after pinning it. Saving still checks the original bytes afresh.
            var selected = Track(chosen.Result);
            RememberRead(selected);
            return selected; // Selection never modifies either settings file.
        }
    }

    internal ProtoEditorSettingsSaveResult RememberLastModPath(string path)
    {
        lock (_locationGate)
        {
            var observed = Load();
            if (!observed.CanSave || observed.Config == null)
                return Outcome(ProtoEditorSettingsSaveState.Refused, observed.Source, observed.Error, observed);
            var edited = CloneConfig(observed.Config);
            edited.LastModFilePath = path;
            return TrySave(observed, edited);
        }
    }

    internal ProtoEditorSettingsSaveResult RememberDataBar(ProtoEditorSettingsLoadResult observed, string path, double mtime)
    {
        if (!observed.CanSave || observed.Config == null)
            return Outcome(ProtoEditorSettingsSaveState.Refused, observed.Source, observed.Error, observed);
        var edited = CloneConfig(observed.Config);
        edited.DataBarPath = path;
        edited.DataBarMtime = mtime;
        return TrySave(observed, edited);
    }

    private void RememberRead(ProtoEditorSettingsLoadResult result)
        => _cachedRead = result.State is ProtoEditorSettingsLoadState.Valid or ProtoEditorSettingsLoadState.Recovered
            ? CloneResult(result) : null;

    private static ProtoEditorSettingsLoadResult CloneResult(ProtoEditorSettingsLoadResult result)
        => result with { Config = result.Config == null ? null : CloneConfig(result.Config) };

    private static ProtoEditorConfig CloneConfig(ProtoEditorConfig config) => new()
    {
        DataBarPath = config.DataBarPath, UserFolderPath = config.UserFolderPath,
        LastModFilePath = config.LastModFilePath, DataBarMtime = config.DataBarMtime,
        CategoriesByModPath = config.CategoriesByModPath?.ToDictionary(pair => pair.Key,
            pair => pair.Value == null ? null! : new ProtoEditorCategoryData
            {
                Categories = pair.Value.Categories == null ? null! : [.. pair.Value.Categories],
                UnitCategories = pair.Value.UnitCategories == null ? null! : new(pair.Value.UnitCategories, pair.Value.UnitCategories.Comparer)
            }, config.CategoriesByModPath.Comparer)
    };

    private Snapshot ReadSnapshot(string path)
    {
        byte[] bytes;
        try
        {
            if (!FileIsPresent(path))
                return new(new(path, false, 0, "", null), null, null, null);
            bytes = _files.ReadAllBytes(path);
        }
        catch (Exception exception) when (IsAccessFailure(exception))
        { return new(null, null, null, exception); }

        var baseline = new ProtoEditorSettingsBaseline(path, true, bytes.LongLength,
            Convert.ToHexString(SHA256.HashData(bytes)), null);
        try
        {
            var json = PreservedTextEncoding.Decode(bytes).Text;
            baseline = baseline with { Json = json };
            var config = JsonSerializer.Deserialize(json, ProtoEditorJsonContext.Default.ProtoEditorConfig);
            if (config == null) throw new JsonException("Settings must contain a JSON object, not null.");
            return new(baseline, bytes, config, null);
        }
        catch (Exception exception) when (exception is JsonException or DecoderFallbackException)
        {
            return new(baseline, bytes, null, exception);
        }
    }

    private SnapshotPair ReadPair(string path)
    {
        var primary = ReadSnapshot(path);
        var backup = ReadSnapshot(path + ".bak");
        ProtoEditorSettingsLoadResult result;
        if (primary.Config != null)
            result = new(ProtoEditorSettingsLoadState.Valid, path, primary.Config, primary.Baseline,
                backup.Error == null ? null : $"Backup '{path}.bak': {backup.Error.Message}", _candidates);
        else if (backup.Config != null)
            result = new(ProtoEditorSettingsLoadState.Recovered, path, backup.Config, primary.Baseline,
                $"Primary settings are missing or unreadable: {primary.Error?.Message ?? path}. A validated backup is available; confirm recovery before saving.", _candidates)
                { RecoverySource = path + ".bak" };
        else if (primary.Baseline is { Exists: false } && backup.Baseline is { Exists: false })
            result = new(ProtoEditorSettingsLoadState.Missing, path, new ProtoEditorConfig(), primary.Baseline, null, _candidates);
        else
            result = new(primary.Baseline == null || backup.Baseline == null
                    ? ProtoEditorSettingsLoadState.Unavailable : ProtoEditorSettingsLoadState.Invalid,
                path, null, primary.Baseline,
                $"Settings could not be loaded. Primary '{path}': {primary.Error?.Message ?? "missing"}. Backup '{path}.bak': {backup.Error?.Message ?? "missing"}. Restore valid settings or explicitly choose a reset; existing files were not changed.", _candidates);
        return new(primary, backup, result with { BackupBaseline = backup.Baseline });
    }

    internal void Save(ProtoEditorConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (!_loadedConfigs.TryGetValue(config, out var observed))
        {
            observed = Load();
            if (observed.State != ProtoEditorSettingsLoadState.Missing)
                throw new InvalidOperationException($"Load settings before saving to '{observed.Source}': {observed.Error ?? "the config has no loaded baseline"}");
        }
        var result = TrySave(observed, config);
        if (!result.Succeeded)
            ExceptionDispatchInfo.Capture(result.Cause ?? new InvalidOperationException(result.Error)).Throw();

        // Legacy callers keep the config object. Rebase it to the merged committed
        // values, so a later save neither reverts another writer nor loses a deliberate revert.
        var current = result.Current!;
        var copy = JsonSerializer.Deserialize(current.Baseline!.Json!, ProtoEditorJsonContext.Default.ProtoEditorConfig)!;
        config.DataBarPath = copy.DataBarPath;
        config.UserFolderPath = copy.UserFolderPath;
        config.LastModFilePath = copy.LastModFilePath;
        config.DataBarMtime = copy.DataBarMtime;
        config.CategoriesByModPath = copy.CategoriesByModPath;
        Track(current with { Config = config });
    }

    internal ProtoEditorSettingsSaveResult TrySave(ProtoEditorSettingsLoadResult observed,
        ProtoEditorConfig edited, bool confirmRecovery = false)
    {
        lock (_locationGate)
        {
            var result = TrySaveCore(observed, edited, confirmRecovery);
            if (result.Succeeded && result.Current != null) RememberRead(result.Current);
            else _cachedRead = null;
            return result;
        }
    }

    private ProtoEditorSettingsSaveResult TrySaveCore(ProtoEditorSettingsLoadResult observed,
        ProtoEditorConfig edited, bool confirmRecovery)
    {
        ArgumentNullException.ThrowIfNull(observed);
        ArgumentNullException.ThrowIfNull(edited);
        var location = ResolveLocation();
        if (location.Failure != null)
            return Outcome(ProtoEditorSettingsSaveState.Refused, location.Failure.Source, location.Failure.Error, location.Failure);
        var path = location.Path!;
        if (observed.SourceVersion != _sourceVersion || !observed.Source.Equals(path, StringComparison.OrdinalIgnoreCase) || observed.Baseline == null ||
            !observed.Baseline.Path.Equals(path, StringComparison.OrdinalIgnoreCase) ||
            (observed.State == ProtoEditorSettingsLoadState.Valid && (!observed.Baseline.Exists || observed.Baseline.Json == null)) ||
            (observed.State == ProtoEditorSettingsLoadState.Missing && observed.Baseline.Exists) ||
            (observed.State == ProtoEditorSettingsLoadState.Recovered && observed.BackupBaseline is not { Exists: true, Json: not null }) ||
            (!observed.CanSave && observed.State != ProtoEditorSettingsLoadState.Recovered))
            return Outcome(ProtoEditorSettingsSaveState.Refused, path, "A usable baseline from this settings location is required. Reload settings before saving.");

        try
        {
            // Persistent sidecar: never unlink a lock file while another process may
            // be waiting on that identity. One non-waiting acquisition bounds UI work.
            using var gate = _files.TryAcquireLock(path + ".lock");
            if (gate == null)
                return Outcome(ProtoEditorSettingsSaveState.Busy, path, "Another editor is saving settings. Retry after that operation finishes.");
            var current = ReadPair(path);
            if (current.Primary.Baseline == null || current.Backup.Baseline == null)
                return Outcome(ProtoEditorSettingsSaveState.Failed, path, current.Result.Error, current.Result,
                    current.Primary.Error ?? current.Backup.Error);
            if (current.Result.State == ProtoEditorSettingsLoadState.Recovered)
            {
                if (!confirmRecovery || observed.State != ProtoEditorSettingsLoadState.Recovered)
                    return Outcome(ProtoEditorSettingsSaveState.Refused, path, "A valid backup is available. Reload and explicitly confirm recovery before saving.", current.Result);
                if (observed.Baseline != current.Primary.Baseline || observed.BackupBaseline != current.Backup.Baseline)
                    return Outcome(ProtoEditorSettingsSaveState.Conflict, path, "Recovery inputs changed. Reload and confirm the current backup.", current.Result);
            }
            else if (!current.Result.CanSave)
                return Outcome(ProtoEditorSettingsSaveState.Refused, path, current.Result.Error, current.Result);
            if (current.Result.State == ProtoEditorSettingsLoadState.Missing && observed.Baseline.Exists)
                return Outcome(ProtoEditorSettingsSaveState.Conflict, path, "Settings were removed after loading. Reload before saving.", current.Result);
            if (FindAlternate(path) is { } alternate)
                return Outcome(ProtoEditorSettingsSaveState.Refused, path, $"Another settings source changed or appeared at '{alternate}'. Select a source before saving; neither file was changed.", current.Result);

            var merge = ProtoEditorSettingsMerge.Merge(observed, current.Result, edited);
            if (merge.Conflict != null)
                return Outcome(ProtoEditorSettingsSaveState.Conflict, path, merge.Conflict, current.Result);
            if (!merge.Changed && current.Result.State == ProtoEditorSettingsLoadState.Valid)
                return Outcome(ProtoEditorSettingsSaveState.Unchanged, path, null, Track(current.Result));
            return Publish(path, current, merge.Json!);
        }
        catch (Exception exception) when (IsAccessFailure(exception) || exception is JsonException)
        { return Outcome(ProtoEditorSettingsSaveState.Failed, path, exception.Message, cause: exception); }
    }

    private ProtoEditorSettingsSaveResult Publish(string path, SnapshotPair current, string json)
    {
        var original = current.Result.State == ProtoEditorSettingsLoadState.Recovered ? current.Backup : current.Primary;
        var encoding = original.Bytes == null ? PreservedTextEncoding.Utf8WithoutBom("") : PreservedTextEncoding.Decode(original.Bytes);
        var newline = encoding.Text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        json = json.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", newline, StringComparison.Ordinal);
        if (encoding.Text.EndsWith('\n')) json += newline;
        var output = encoding.Encode(json);
        var temporary = Path.Combine(Path.GetDirectoryName(path)!, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        string? damaged = null;
        var staged = false;
        var publicationAttempted = false;
        ProtoEditorSettingsSaveResult result;
        try
        {
            _files.WriteNewAndFlush(temporary, output);
            staged = true;
            if (!_files.ReadAllBytes(temporary).AsSpan().SequenceEqual(output))
                throw new InvalidDataException("Staged settings do not match the validated output.");
            if (current.Result.State == ProtoEditorSettingsLoadState.Recovered && current.Primary.Bytes != null)
            {
                damaged = path + $".damaged-{Guid.NewGuid():N}.recovery";
                _files.WriteNewAndFlush(damaged, current.Primary.Bytes);
                if (!_files.ReadAllBytes(damaged).AsSpan().SequenceEqual(current.Primary.Bytes))
                    throw new InvalidDataException("The preserved damaged settings copy could not be verified.");
            }

            var latest = ReadPair(path);
            if (latest.Primary.Baseline != current.Primary.Baseline || latest.Backup.Baseline != current.Backup.Baseline ||
                FindAlternate(path) != null)
                result = Outcome(ProtoEditorSettingsSaveState.Conflict, path, "Settings or backup changed before publication. Reload and retry; no settings file was replaced.", latest.Result);
            else
            {
                publicationAttempted = true;
                if (current.Primary.Baseline!.Exists)
                    _files.Replace(temporary, path, current.Primary.Config == null ? null : path + ".bak");
                else _files.MoveNew(temporary, path);
                var baseline = new ProtoEditorSettingsBaseline(path, true, output.LongLength,
                    Convert.ToHexString(SHA256.HashData(output)), json);
                var committed = Track(new(ProtoEditorSettingsLoadState.Valid, path,
                    JsonSerializer.Deserialize(json, ProtoEditorJsonContext.Default.ProtoEditorConfig), baseline, null, _candidates)
                { BackupBaseline = current.Primary.Config == null ? current.Backup.Baseline : current.Primary.Baseline! with { Path = path + ".bak" } });
                result = Outcome(ProtoEditorSettingsSaveState.Saved, path, null, committed);
            }
        }
        catch (Exception exception) when (IsAccessFailure(exception) || exception is InvalidDataException)
        {
            result = Outcome(ProtoEditorSettingsSaveState.Failed, path,
                $"Settings save failed{(publicationAttempted ? "; publication may have completed" : " before publication")}: {exception.Message}. Inspect the recovery paths before retrying.", cause: exception)
                with { RecoveryPaths = Array.AsReadOnly(new[] { path, path + ".bak", temporary }.Concat(damaged == null ? [] : new[] { damaged }).ToArray()) };
        }
        if (staged && !publicationAttempted)
        {
            try { _files.Delete(temporary); }
            catch (Exception exception) when (IsAccessFailure(exception))
            {
                result = result with { Error = $"{result.Error} Temporary output retained at '{temporary}': {exception.Message}",
                    RecoveryPaths = Array.AsReadOnly(result.RecoveryPaths.Append(temporary).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()) };
            }
        }
        if (damaged != null)
            result = result with { RecoveryPaths = Array.AsReadOnly(result.RecoveryPaths.Append(damaged).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()) };
        return result;
    }

    private string? FindAlternate(string selected)
    {
        foreach (var path in _candidates.Where(path => !path.Equals(selected, StringComparison.OrdinalIgnoreCase))
                     .SelectMany(path => new[] { path, path + ".bak" }))
        {
            if (_acknowledgedAlternates != null && _acknowledgedAlternates.TryGetValue(path, out var baseline))
            {
                if (ReadSnapshot(path).Baseline != baseline) return path;
            }
            else if (FileIsPresent(path)) return path;
        }
        return null;
    }

    private ProtoEditorSettingsLoadResult Track(ProtoEditorSettingsLoadResult result)
    {
        result = result with { SourceVersion = _sourceVersion };
        if (result.Config != null)
        {
            lock (_loadedConfigs)
            {
                _loadedConfigs.Remove(result.Config);
                _loadedConfigs.Add(result.Config, result);
            }
        }
        return result;
    }

    private static ProtoEditorSettingsSaveResult Outcome(ProtoEditorSettingsSaveState state, string path,
        string? error, ProtoEditorSettingsLoadResult? current = null, Exception? cause = null)
        => new(state, path, error, current, Array.Empty<string>()) { Cause = cause };

    private (string? Path, ProtoEditorSettingsLoadResult? Failure) ResolveLocation()
    {
        lock (_locationGate)
        {
            if (_selectedPath != null) return (_selectedPath, null);
            var existing = new List<string>();
            foreach (var path in _candidates)
            {
                try { if (FileIsPresent(path) || FileIsPresent(path + ".bak")) existing.Add(path); }
                catch (Exception exception) when (IsAccessFailure(exception))
                { return (null, Failure(ProtoEditorSettingsLoadState.Unavailable, path, exception.Message)); }
            }
            if (existing.Count > 1)
                return (null, Failure(ProtoEditorSettingsLoadState.Ambiguous, string.Join(" | ", existing),
                    "Settings exist in both the application and profile locations. Select a source before saving; neither file was changed."));
            if (existing.Count == 1)
            {
                _selectedPath = existing[0];
                return (_selectedPath, null);
            }

            var errors = new List<string>();
            foreach (var path in _candidates)
            {
                try
                {
                    _files.ProbeWritableDirectory(Path.GetDirectoryName(path)!);
                    _selectedPath = path;
                    return (_selectedPath, null);
                }
                catch (Exception exception) when (IsAccessFailure(exception))
                { errors.Add($"{path}: {exception.Message}"); }
            }
            // No location has been selected yet: a later retry may recover from an access failure.
            return (null, Failure(ProtoEditorSettingsLoadState.Unavailable, string.Join(" | ", _candidates),
                "No writable settings location was found. " + string.Join(" ", errors)));
        }
    }

    private bool FileIsPresent(string path)
    {
        try
        {
            if ((_files.GetAttributes(path) & FileAttributes.Directory) != 0)
                throw new IOException($"The settings path '{path}' is a directory, not a file.");
            return true;
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        { return false; }
    }

    private ProtoEditorSettingsLoadResult Failure(ProtoEditorSettingsLoadState state, string source, string error)
        => new(state, source, null, null, error, _candidates);

    private static bool IsAccessFailure(Exception exception)
        => exception is IOException or UnauthorizedAccessException or SecurityException or ArgumentException or NotSupportedException;
}
