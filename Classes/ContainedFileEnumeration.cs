namespace AoMDivineDataEditor.Classes;

/// <summary>
/// Enumerates files beneath a trusted root without following file-system links out of that tree.
/// </summary>
internal static class ContainedFileEnumeration
{
    public static IEnumerable<string> EnumerateFiles(string rootDirectory)
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

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            ReturnSpecialDirectories = false,
            IgnoreInaccessible = false,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint,
            MatchCasing = MatchCasing.CaseInsensitive
        };
        foreach (var path in Directory.EnumerateFiles(root, "*", options))
        {
            var fullPath = Path.GetFullPath(path);
            if (!IsContained(root, fullPath))
            {
                throw new IOException(
                    $"File discovery escaped the mod directory through '{path}'. The operation was cancelled.");
            }
            yield return fullPath;
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
