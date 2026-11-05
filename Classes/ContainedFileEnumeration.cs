namespace AoMDivineDataEditor.Classes;

/// <summary>
/// Enumerates files beneath a trusted root without following file-system links out of that tree.
/// </summary>
internal static class ContainedFileEnumeration
{
    public static IEnumerable<string> EnumerateFiles(string rootDirectory,
        bool includeHiddenAndSystem = false, int? maximumEntries = null, Action<string>? skippedLink = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        var root = Path.GetFullPath(rootDirectory);
        if (!Directory.Exists(root))
            yield break;
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException(
                $"File discovery will not traverse the linked root directory '{root}'.");
        }

        var pending = new Stack<string>();
        pending.Push(root);
        var visited = 0L;
        while (pending.TryPop(out var directory))
        {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            {
                skippedLink?.Invoke(directory);
                continue;
            }
            foreach (var path in Directory.EnumerateFileSystemEntries(directory, "*", new EnumerationOptions
                     { AttributesToSkip = 0, IgnoreInaccessible = false, ReturnSpecialDirectories = false }))
            {
                if (maximumEntries.HasValue && ++visited > maximumEntries.Value)
                    throw new IOException($"Discovery exceeded the {maximumEntries.Value:N0} file/directory entry limit.");
                var fullPath = Path.GetFullPath(path);
                if (!IsContained(root, fullPath))
                    throw new IOException($"File discovery escaped the mod directory through '{path}'. The operation was cancelled.");
                var attributes = File.GetAttributes(fullPath);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    skippedLink?.Invoke(fullPath);
                    continue;
                }
                if (!includeHiddenAndSystem && (attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0)
                    continue;
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(fullPath);
                else yield return fullPath;
            }
        }
    }

    private static bool IsContained(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return !Path.IsPathRooted(relative) &&
               !relative.Equals("..", StringComparison.Ordinal) &&
               !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
               !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
    }
}
