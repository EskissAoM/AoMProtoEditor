using System.Runtime.ExceptionServices;
using System.Security.Cryptography;

namespace AoMDivineDataEditor.Classes;

/// <summary>
/// Captures exact file contents before a multi-file commit and restores every
/// captured path if the commit fails. Individual writers should still use
/// temp-file replacement so both the forward write and rollback are atomic.
/// </summary>
public static class FileIntegrityTransaction
{
    private sealed record Snapshot(string Path, bool Existed, byte[] Contents);

    public sealed record FileState(bool Exists, long Length, string Sha256);

    public static FileState CaptureState(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            return new FileState(false, 0, "");

        var contents = File.ReadAllBytes(fullPath);
        return CreateState(contents);
    }

    public static IReadOnlyDictionary<string, FileState> CaptureStates(IEnumerable<string?> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        return paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                path => path,
                CaptureState,
                StringComparer.OrdinalIgnoreCase);
    }

    public static void Execute(IEnumerable<string?> paths, Action commit)
        => Execute(paths, expectedStates: null, commit);

    public static void Execute(
        IEnumerable<string?> paths,
        IReadOnlyDictionary<string, FileState>? expectedStates,
        Action commit)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(commit);

        var snapshots = CaptureSnapshots(paths);
        EnsureExpectedStatesMatch(snapshots, expectedStates);
        EnsureSnapshotsStillCurrent(snapshots);

        try
        {
            commit();
        }
        catch (Exception commitException)
        {
            RollBack(snapshots, commitException);
        }
    }

    public static async Task ExecuteAsync(
        IEnumerable<string?> paths,
        IReadOnlyDictionary<string, FileState>? expectedStates,
        Func<Task> commit)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(commit);

        var snapshots = CaptureSnapshots(paths);
        EnsureExpectedStatesMatch(snapshots, expectedStates);
        EnsureSnapshotsStillCurrent(snapshots);

        try
        {
            await commit();
        }
        catch (Exception commitException)
        {
            RollBack(snapshots, commitException);
        }
    }

    private static List<Snapshot> CaptureSnapshots(IEnumerable<string?> paths)
        => paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(CaptureSnapshot)
            .ToList();

    private static Snapshot CaptureSnapshot(string path)
    {
        var existed = File.Exists(path);
        return new Snapshot(path, existed, existed ? File.ReadAllBytes(path) : []);
    }

    private static void EnsureExpectedStatesMatch(
        IReadOnlyList<Snapshot> snapshots,
        IReadOnlyDictionary<string, FileState>? expectedStates)
    {
        if (expectedStates == null || expectedStates.Count == 0)
            return;

        foreach (var snapshot in snapshots)
        {
            if (!expectedStates.TryGetValue(snapshot.Path, out var expected))
                continue;
            var actual = snapshot.Existed ? CreateState(snapshot.Contents) : new FileState(false, 0, "");
            if (actual == expected)
                continue;

            throw new IOException(
                $"'{snapshot.Path}' changed outside the editor after it was loaded. No files were written. " +
                "Reload the mod before saving so those external changes are not overwritten.");
        }
    }

    private static FileState CreateState(byte[] contents)
        => new(true, contents.LongLength, Convert.ToHexString(SHA256.HashData(contents)));

    private static void EnsureSnapshotsStillCurrent(IReadOnlyList<Snapshot> snapshots)
    {
        foreach (var snapshot in snapshots)
        {
            var current = CaptureState(snapshot.Path);
            var captured = snapshot.Existed ? CreateState(snapshot.Contents) : new FileState(false, 0, "");
            if (current == captured)
                continue;

            throw new IOException(
                $"'{snapshot.Path}' changed while the save transaction was being prepared. No files were written. " +
                "Retry after the other program has finished writing the mod.");
        }
    }

    private static void RollBack(IReadOnlyList<Snapshot> snapshots, Exception commitException)
    {
        var rollbackErrors = new List<Exception>();
        foreach (var snapshot in snapshots.Reverse())
        {
            try
            {
                if (snapshot.Existed)
                {
                    if (!File.Exists(snapshot.Path) ||
                        !File.ReadAllBytes(snapshot.Path).AsSpan().SequenceEqual(snapshot.Contents))
                    {
                        AtomicWriteAllBytes(snapshot.Path, snapshot.Contents);
                    }
                }
                else if (File.Exists(snapshot.Path))
                    File.Delete(snapshot.Path);
            }
            catch (Exception rollbackException)
            {
                rollbackErrors.Add(new IOException(
                    $"Could not restore '{snapshot.Path}' after the failed save.",
                    rollbackException));
            }
        }

        if (rollbackErrors.Count > 0)
        {
            throw new AggregateException(
                "The save failed and one or more files could not be restored. Restore the affected mod files from backup before continuing.",
                new[] { commitException }.Concat(rollbackErrors));
        }

        ExceptionDispatchInfo.Capture(commitException).Throw();
        throw new InvalidOperationException("Unreachable rollback path.");
    }

    private static void AtomicWriteAllBytes(string path, byte[] contents)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var tempPath = Path.Combine(directory ?? ".", $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(tempPath, contents);
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch
            {
                // The target has already been restored; a stranded temp file is
                // preferable to masking the original save result.
            }
        }
    }
}
