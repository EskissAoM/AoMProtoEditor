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
    private sealed record Snapshot(
        string Path,
        bool Existed,
        FileStream? BackupStream,
        FileState State);

    private sealed class SnapshotCollection : IDisposable
    {
        public SnapshotCollection(string backupDirectory, IReadOnlyList<Snapshot> items)
        {
            BackupDirectory = backupDirectory;
            Items = items;
        }

        public string BackupDirectory { get; }
        public IReadOnlyList<Snapshot> Items { get; }

        public void Dispose()
        {
            foreach (var snapshot in Items)
            {
                try { snapshot.BackupStream?.Dispose(); }
                catch { }
            }
            try
            {
                if (Directory.Exists(BackupDirectory))
                    Directory.Delete(BackupDirectory, recursive: true);
            }
            catch
            {
                // The transaction result is already known. Cleanup failure must not turn a
                // successful commit into a reported failure or hide the original exception.
            }
        }
    }

    public sealed record FileState(bool Exists, long Length, string Sha256);

    public static FileState CaptureState(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        EnsureRegularFileOrMissing(fullPath);
        if (!File.Exists(fullPath))
            return new FileState(false, 0, "");

        using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 128 * 1024,
            FileOptions.SequentialScan);
        var length = stream.Length;
        return new FileState(true, length, Convert.ToHexString(SHA256.HashData(stream)));
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

        using var snapshots = CaptureSnapshots(paths);
        EnsureExpectedStatesMatch(snapshots.Items, expectedStates);
        EnsureSnapshotsStillCurrent(snapshots.Items);

        try
        {
            commit();
        }
        catch (Exception commitException)
        {
            RollBack(snapshots.Items, commitException);
        }
    }

    public static async Task ExecuteAsync(
        IEnumerable<string?> paths,
        IReadOnlyDictionary<string, FileState>? expectedStates,
        Func<Task> commit)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(commit);

        using var snapshots = CaptureSnapshots(paths);
        EnsureExpectedStatesMatch(snapshots.Items, expectedStates);
        EnsureSnapshotsStillCurrent(snapshots.Items);

        try
        {
            await commit();
        }
        catch (Exception commitException)
        {
            RollBack(snapshots.Items, commitException);
        }
    }

    private static SnapshotCollection CaptureSnapshots(IEnumerable<string?> paths)
    {
        var normalizedPaths = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var backupDirectory = Path.Combine(
            Path.GetTempPath(),
            "AoMDivineDataEditor",
            "transactions",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(backupDirectory);
        try
        {
            var snapshots = new List<Snapshot>(normalizedPaths.Count);
            try
            {
                for (var index = 0; index < normalizedPaths.Count; index++)
                    snapshots.Add(CaptureSnapshot(normalizedPaths[index], backupDirectory, index));
            }
            catch
            {
                foreach (var snapshot in snapshots)
                    snapshot.BackupStream?.Dispose();
                throw;
            }
            return new SnapshotCollection(backupDirectory, snapshots);
        }
        catch
        {
            try { Directory.Delete(backupDirectory, recursive: true); }
            catch { }
            throw;
        }
    }

    private static Snapshot CaptureSnapshot(string path, string backupDirectory, int index)
    {
        var stateBeforeCopy = CaptureState(path);
        if (!stateBeforeCopy.Exists)
            return new Snapshot(path, false, null, stateBeforeCopy);

        var backupPath = Path.Combine(backupDirectory, index.ToString("D6") + ".snapshot");
        var backupStream = new FileStream(
            backupPath,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.Read | FileShare.Delete,
            bufferSize: 128 * 1024,
            FileOptions.SequentialScan | FileOptions.DeleteOnClose);
        try
        {
            using (var source = new FileStream(
                       path, FileMode.Open, FileAccess.Read, FileShare.Read,
                       bufferSize: 128 * 1024, FileOptions.SequentialScan))
                source.CopyTo(backupStream);
            backupStream.Flush(flushToDisk: true);
            var stateAfterCopy = CaptureState(path);
            var backupState = CaptureState(backupStream);
            if (stateBeforeCopy != stateAfterCopy || stateBeforeCopy != backupState)
            {
                throw new IOException(
                    $"'{path}' changed while its rollback snapshot was being created. No files were written.");
            }

            return new Snapshot(path, true, backupStream, stateAfterCopy);
        }
        catch
        {
            backupStream.Dispose();
            throw;
        }
    }

    private static FileState CaptureState(FileStream stream)
    {
        stream.Position = 0;
        var state = new FileState(true, stream.Length, Convert.ToHexString(SHA256.HashData(stream)));
        stream.Position = 0;
        return state;
    }

    private static void EnsureExpectedStatesMatch(
        IReadOnlyList<Snapshot> snapshots,
        IReadOnlyDictionary<string, FileState>? expectedStates)
    {
        if (expectedStates == null || expectedStates.Count == 0)
            return;

        var snapshotsByPath = snapshots.ToDictionary(
            snapshot => snapshot.Path,
            StringComparer.OrdinalIgnoreCase);
        foreach (var (path, expected) in expectedStates)
        {
            var fullPath = Path.GetFullPath(path);
            var actual = snapshotsByPath.TryGetValue(fullPath, out var snapshot)
                ? snapshot.State
                : CaptureState(fullPath);
            if (actual == expected)
                continue;

            throw new IOException(
                $"'{fullPath}' changed outside the editor after it was loaded. No files were written. " +
                "Reload the mod before saving so those external changes are not overwritten.");
        }
    }

    private static void EnsureSnapshotsStillCurrent(IReadOnlyList<Snapshot> snapshots)
    {
        foreach (var snapshot in snapshots)
        {
            var current = CaptureState(snapshot.Path);
            if (current == snapshot.State)
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
                var removedLinkedTarget = RemoveReparsePointIfPresent(snapshot.Path);
                if (snapshot.Existed)
                {
                    if (removedLinkedTarget || CaptureState(snapshot.Path) != snapshot.State)
                        AtomicRestore(snapshot);
                }
                else
                {
                    if (removedLinkedTarget)
                        continue;
                    EnsureRegularFileOrMissing(snapshot.Path);
                    if (File.Exists(snapshot.Path))
                        File.Delete(snapshot.Path);
                }
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

    private static void AtomicRestore(Snapshot snapshot)
    {
        if (snapshot.BackupStream == null)
            throw new InvalidOperationException($"The rollback snapshot for '{snapshot.Path}' is unavailable.");
        EnsureRegularFileOrMissing(snapshot.Path);
        var directory = Path.GetDirectoryName(snapshot.Path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var tempPath = Path.Combine(
            directory ?? ".",
            $".{Path.GetFileName(snapshot.Path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            snapshot.BackupStream.Position = 0;
            using (var target = new FileStream(
                       tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                       bufferSize: 128 * 1024, FileOptions.SequentialScan))
            {
                snapshot.BackupStream.CopyTo(target);
                target.Flush(flushToDisk: true);
            }
            EnsureRegularFileOrMissing(snapshot.Path);
            File.Move(tempPath, snapshot.Path, overwrite: true);
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

    private static void EnsureRegularFileOrMissing(string path)
    {
        EnsureNoLinkedDirectoryAncestors(path);
        if (Directory.Exists(path))
            throw new IOException($"'{path}' is a directory, not an editable file.");
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException(
                    $"'{path}' is a symbolic link or reparse-point file. The editor will not read or overwrite linked save targets.");
            }
        }
        catch (FileNotFoundException)
        {
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    private static void EnsureNoLinkedDirectoryAncestors(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        while (!string.IsNullOrWhiteSpace(directory))
        {
            if (Directory.Exists(directory) &&
                (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException(
                    $"'{path}' is inside a symbolic-link or reparse-point directory. " +
                    "The editor will not read or overwrite save targets through linked directories.");
            }
            directory = Path.GetDirectoryName(directory);
        }
    }

    private static bool RemoveReparsePointIfPresent(string path)
    {
        FileAttributes attributes;
        try
        {
            attributes = File.GetAttributes(path);
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }

        if ((attributes & FileAttributes.ReparsePoint) == 0)
            return false;

        // Delete the link itself, never its target. This lets rollback recover even if
        // a failed writer swapped an ordinary save target for a link mid-transaction.
        if ((attributes & FileAttributes.Directory) != 0)
            Directory.Delete(path);
        else
            File.Delete(path);
        return true;
    }
}
