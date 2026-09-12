using System.Text;
using System.Xml.Linq;

namespace AoMDivineDataEditor.Tests;

/// <summary>
/// Isolated filesystem owned by one test. All paths stay below a unique temporary
/// root so end-to-end tests can never read or write a user's real mods or settings.
/// </summary>
internal sealed class TestWorkspace : IDisposable
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(false);

    public TestWorkspace(string scenario = "workspace")
    {
        var safeScenario = string.Concat(scenario.Select(character =>
            char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '-'));
        RootPath = Path.Combine(
            Path.GetTempPath(),
            "AoMDivineDataEditor.Tests",
            safeScenario,
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(RootPath);
    }

    public string RootPath { get; }
    public string ModPath => Path.Combine(RootPath, "TestMod");
    public string GameplayPath => Path.Combine(ModPath, "game", "data", "gameplay");
    public string TacticsPath => Path.Combine(ModPath, "game", "data", "tactics");
    public string StringsPath => Path.Combine(ModPath, "game", "data", "strings", "stringmods.txt");

    public string GetPath(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        var combined = Path.GetFullPath(Path.Combine(
            RootPath,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var rootWithSeparator = RootPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!combined.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The test path must remain inside its workspace.", nameof(relativePath));
        return combined;
    }

    public string CreateDirectory(string relativePath)
    {
        var path = GetPath(relativePath);
        Directory.CreateDirectory(path);
        return path;
    }

    public string WriteText(string relativePath, string contents, Encoding? encoding = null)
    {
        var path = GetPath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents, encoding ?? Utf8WithoutBom);
        return path;
    }

    public string WriteXml(string relativePath, string xml)
    {
        _ = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        return WriteText(relativePath, xml);
    }

    public byte[] ReadBytes(string relativePath) => File.ReadAllBytes(GetPath(relativePath));

    public void Dispose()
    {
        if (Directory.Exists(RootPath))
            Directory.Delete(RootPath, recursive: true);
    }
}
