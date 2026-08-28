using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class TechnologyDataRound9RegressionTests
{
    [Fact]
    public void TechnologyEditor_DataRound9UsesContainedTypeEditorsAndCorrectLabels()
    {
        var root = FindProjectRoot();
        var code = File.ReadAllText(Path.Combine(root, "Windows", "TechnologyEditorView.axaml.cs"));

        Assert.Contains("ContainedTypeDataSubtypes", code, StringComparison.Ordinal);
        Assert.Contains("AddContainedTypeDataEffectEditor", code, StringComparison.Ordinal);
        Assert.Contains("\"addcontainedtype\" => \"Contain\"", code, StringComparison.Ordinal);
        Assert.Contains("\"addnotcontainedtype\" => \"Not contain\"", code, StringComparison.Ordinal);
        Assert.Contains("\"addsharedbuildlimitunittype\" => \"Shared with\"", code, StringComparison.Ordinal);
        Assert.Contains("\"addveterancyexcludetype\" => \"Exclude type\"", code, StringComparison.Ordinal);
        Assert.Contains("\"addveterancyincludetype\" => \"Include type\"", code, StringComparison.Ordinal);
        Assert.Contains("EnsureExactDataAttribute(effect, \"amount\", \"1\")", code, StringComparison.Ordinal);
        Assert.Contains("EnsureExactDataAttribute(effect, \"relativity\", \"Assign\")", code, StringComparison.Ordinal);
        Assert.Contains("GetCaseInsensitiveAttribute(effect, \"unittype\")", code, StringComparison.Ordinal);
        Assert.Contains("_prereqUnitNames", code, StringComparison.Ordinal);
    }

    [Fact]
    public void TechnologyEditor_DataRound9PolishesModifySpawnSelfDestructAndOptionalSpacing()
    {
        var root = FindProjectRoot();
        var code = File.ReadAllText(Path.Combine(root, "Windows", "TechnologyEditorView.axaml.cs"));

        Assert.Contains("CreateLabeledEffectSegment(\"Type\", CreateStrictEffectSelector(\n            ProtoConstants.KnownSpawnTypes", NormalizeNewlines(code), StringComparison.Ordinal);
        Assert.Contains("CreateLabeledEffectSegment(\"Action\", CreateStrictEffectSelector(\n            _protoActionNames", NormalizeNewlines(code), StringComparison.Ordinal);
        Assert.Contains("var segment = CreateLabeledEffectSegment(label, box, leftSpacing: 8);", code, StringComparison.Ordinal);
        Assert.Contains("segment.Children.Add(CreateRemoveButton", code, StringComparison.Ordinal);
    }

    private static string NormalizeNewlines(string value) => value.Replace("\r\n", "\n");

    private static string FindProjectRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AoMDivineDataEditor.csproj"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate AoMDivineDataEditor.csproj.");
    }
}
