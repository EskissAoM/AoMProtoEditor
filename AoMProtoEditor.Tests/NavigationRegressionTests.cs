using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace AoMProtoEditor.Tests;

public sealed class NavigationRegressionTests
{
    [Fact]
    public void ProtoUnitMenu_TacticsAndAbilitiesRemainConnectedToTheirManagers()
    {
        var root = FindProjectRoot();
        var xamlPath = Path.Combine(root, "Windows", "ProtoEditorWindow.axaml");
        var codePath = Path.Combine(root, "Windows", "ProtoEditorWindow.axaml.cs");
        var xaml = XDocument.Load(xamlPath);
        var code = File.ReadAllText(codePath);

        Assert.Contains(xaml.Descendants(), element =>
            string.Equals((string?)element.Attribute("Content"), "Tactics", StringComparison.Ordinal) &&
            string.Equals(element.Attributes().FirstOrDefault(a => a.Name.LocalName == "Click")?.Value, "ProtounitTactics_Click", StringComparison.Ordinal));
        Assert.Contains(xaml.Descendants(), element =>
            string.Equals((string?)element.Attribute("Content"), "Abilities", StringComparison.Ordinal) &&
            string.Equals(element.Attributes().FirstOrDefault(a => a.Name.LocalName == "Click")?.Value, "ProtounitAbilities_Click", StringComparison.Ordinal));

        AssertHandlerCalls(code, "ProtounitTactics_Click", "OpenTacticsManagerAsync");
        AssertHandlerCalls(code, "ProtounitAbilities_Click", "OpenAbilitiesManagerAsync");
    }

    [Fact]
    public void ProtoUnitEditor_StillContainsAllFiveEditorTabsInOrder()
    {
        var root = FindProjectRoot();
        var xaml = XDocument.Load(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml"));
        var headers = xaml.Descendants()
            .Where(e => e.Name.LocalName == "TabItem")
            .Select(e => (string?)e.Attribute("Header"))
            .Where(header => header is "Stats" or "Actions" or "Commands" or "Abilities" or "Train/Research")
            .ToList();

        Assert.Equal(["Stats", "Actions", "Commands", "Abilities", "Train/Research"], headers);
    }

    private static void AssertHandlerCalls(string code, string handler, string target)
    {
        var match = Regex.Match(code,
            $@"{Regex.Escape(handler)}\s*\([^)]*\)\s*\{{(?<body>.*?)\n\s*\}}",
            RegexOptions.Singleline);
        Assert.True(match.Success, $"Could not find handler {handler}.");
        Assert.Contains(target, match.Groups["body"].Value, StringComparison.Ordinal);
    }

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var direct = Path.Combine(directory.FullName, "AoMDivineDataEditor.csproj");
            if (File.Exists(direct))
                return directory.FullName;

            // Normal test output is .../AoMDivineDataEditor/AoMDivineDataEditor.Tests/bin/...;
            // this handles running the tests from any build configuration.
            var sibling = Path.Combine(directory.FullName, "AoMDivineDataEditor", "AoMDivineDataEditor.csproj");
            if (File.Exists(sibling))
                return Path.Combine(directory.FullName, "AoMDivineDataEditor");
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate AoMDivineDataEditor.csproj from the test output directory.");
    }
}
