using System.Runtime.ExceptionServices;

namespace AoMDivineDataEditor.Classes;

/// <summary>
/// Captures exact file contents before a multi-file commit and restores every
/// captured path if the commit fails. Individual writers should still use
/// temp-file replacement so both the forward write and rollback are atomic.
/// </summary>
public static class FileIntegrityTransaction
{
    private sealed record Snapshot(string Path, bool Existed, byte[] Contents);

    public static void Execute(IEnumerable<string?> paths, Action commit)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(commit);

        var snapshots = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => new Snapshot(
                path,
                File.Exists(path),
                File.Exists(path) ? File.ReadAllBytes(path) : []))
            .ToList();

        try
        {
            commit();
        }
        catch (Exception commitException)
        {
            var rollbackErrors = new List<Exception>();
            foreach (var snapshot in snapshots.AsEnumerable().Reverse())
            {
                try
                {
                    if (snapshot.Existed)
                        AtomicWriteAllBytes(snapshot.Path, snapshot.Contents);
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
            throw;
        }
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
