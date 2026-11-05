using Xunit;

namespace AoMDivineDataEditor.Tests;

internal static class OriginalCorpus
{
    public static bool IsRequired => Environment.GetEnvironmentVariable("AOM_REQUIRE_CORPUS") == "1";
    public static string PathForCurrentRun => ResolvePath(Environment.GetEnvironmentVariable("AOM_DATA_BAR"), ProjectSource.RootPath);

    internal static string ResolvePath(string? configured, string projectRoot)
        => !string.IsNullOrWhiteSpace(configured)
            ? Path.GetFullPath(configured)
            : Path.GetFullPath(Path.Combine(projectRoot, "..", "Age of Mythology Retold", "game", "data", "Data.bar"));

    internal static string? GetSkipReason(string path, bool required)
        => !required && !File.Exists(path)
            ? $"Original corpus not executed: Data.bar unavailable at '{path}'. Set AOM_DATA_BAR; releases require AOM_REQUIRE_CORPUS=1."
            : null;

    public static string RequirePath()
        => RequirePath(PathForCurrentRun, IsRequired, Environment.GetEnvironmentVariable("UPDATE_POWER_SCHEMA_BASELINE") == "1");

    internal static string RequirePath(string path, bool required, bool updateBaseline)
    {
        if (required && updateBaseline)
            throw new InvalidOperationException("Required corpus verification must not update its checked-in baseline.");
        if (!File.Exists(path))
            throw new FileNotFoundException("Original corpus verification could not execute; provide a real Data.bar through AOM_DATA_BAR.", path);
        return path;
    }
}

public sealed class CorpusFactAttribute : FactAttribute
{
    public CorpusFactAttribute()
        => Skip = OriginalCorpus.GetSkipReason(OriginalCorpus.PathForCurrentRun, OriginalCorpus.IsRequired);
}
