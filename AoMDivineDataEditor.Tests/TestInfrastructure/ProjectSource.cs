namespace AoMDivineDataEditor.Tests;

/// <summary>Centralized access for the small set of intentional architecture-contract tests.</summary>
internal static class ProjectSource
{
    private static readonly Lazy<string> ProjectRoot = new(FindProjectRoot);

    public static string RootPath => ProjectRoot.Value;

    public static string Read(params string[] pathParts)
        => File.ReadAllText(Path.Combine([RootPath, .. pathParts]));

    public static string ExtractMethod(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        if (start < 0)
            throw new InvalidOperationException($"Could not find method '{signature}'.");
        var next = source.IndexOf("\n    private ", start + signature.Length, StringComparison.Ordinal);
        if (next < 0)
            next = source.Length;
        return source[start..next];
    }

    public static string ExtractBetween(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = start < 0 ? -1 : source.IndexOf(endMarker, start, StringComparison.Ordinal);
        if (start < 0 || end <= start)
            throw new InvalidOperationException($"Could not extract source between '{startMarker}' and '{endMarker}'.");
        return source[start..end];
    }

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AoMDivineDataEditor.csproj")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate AoMDivineDataEditor.csproj.");
    }
}
