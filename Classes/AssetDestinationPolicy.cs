namespace AoMDivineDataEditor.Classes;

public sealed record AssetDestination(string AbsolutePath, string RelativePath, string XmlValue);

public static class AssetDestinationPolicy
{
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public static bool TryResolve(
        string rootDirectory,
        string relativeFolder,
        string fileName,
        string extension,
        string xmlPrefix,
        out AssetDestination? destination,
        out string error)
    {
        destination = null;
        error = "";
        if (string.IsNullOrWhiteSpace(rootDirectory)) { error = "The asset root is unavailable."; return false; }
        extension = extension.StartsWith('.') ? extension : "." + extension;
        fileName = fileName.Trim();
        if (fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            fileName = fileName[..^extension.Length];
        if (string.IsNullOrWhiteSpace(fileName)) { error = "Enter a file name."; return false; }
        if (fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || fileName.Contains('/') || fileName.Contains('\\') ||
            fileName.EndsWith(' ') || fileName.EndsWith('.') || ReservedNames.Contains(fileName))
        { error = "The file name is not valid."; return false; }

        var normalizedFolder = (relativeFolder ?? "").Trim().Replace('/', '\\').Trim('\\');
        var segments = normalizedFolder.Length == 0 ? [] : normalizedFolder.Split('\\');
        if (segments.Any(segment => string.IsNullOrWhiteSpace(segment) || segment is "." or ".." ||
                                    segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                                    segment.EndsWith(' ') || segment.EndsWith('.') || ReservedNames.Contains(segment)))
        { error = "The folder path is not valid. Use folders relative to the displayed root."; return false; }

        try
        {
            var root = Path.GetFullPath(rootDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var relativePath = normalizedFolder.Length == 0
                ? fileName + extension
                : normalizedFolder + "\\" + fileName + extension;
            var absolutePath = Path.GetFullPath(Path.Combine(root, relativePath.Replace('\\', Path.DirectorySeparatorChar)));
            if (!absolutePath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            { error = "The destination must remain inside the mod asset folder."; return false; }
            var xmlValue = string.IsNullOrWhiteSpace(xmlPrefix)
                ? relativePath
                : xmlPrefix.Trim().TrimEnd('\\', '/') + "\\" + relativePath;
            destination = new AssetDestination(absolutePath, relativePath, xmlValue);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        { error = "The destination path is not valid."; return false; }
    }
}
