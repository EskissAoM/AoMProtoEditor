using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class TechnologyDataRound11RegressionTests
{
    [Fact]
    public void TechnologyEditor_Round11ExtendsExistingDataEffectFamilies()
    {
        var code = ReadTechnologyEditor();

        Assert.Contains("\"DeadTransformBuildLimit\", \"FreeRepair\"", code, StringComparison.Ordinal);
        Assert.Contains("\"SetNextResearchFree\", \"FakeConversion\"", code, StringComparison.Ordinal);
        Assert.Contains("\"HomingBallistics\", \"InstantBallistics\", \"PerfectAccuracy\", \"VolleyMode\"", code, StringComparison.Ordinal);
        Assert.Contains("\"StealthDetectionRadius\", \"DodgeChance\"", code, StringComparison.Ordinal);
        Assert.Contains("\"MaximumRange\", \"MinimumRange\"", code, StringComparison.Ordinal);
    }

    [Fact]
    public void TechnologyEditor_Round11StructuresMovementTypeAndRevealLos()
    {
        var code = ReadTechnologyEditor();

        Assert.Contains("AddMovementTypeDataEffectEditor", code, StringComparison.Ordinal);
        Assert.Contains("ProtoConstants.FieldSuggestions.TryGetValue(\"movementtype\"", code, StringComparison.Ordinal);
        Assert.Contains("CreateLabeledEffectSegment(\"Set movement type to\"", code, StringComparison.Ordinal);
        Assert.Contains("SetCaseInsensitiveAttribute(effect, \"movementtype\", selected)", code, StringComparison.Ordinal);
        Assert.Contains("EnsureExactDataAttribute(effect, \"amount\", \"1\")", code, StringComparison.Ordinal);
        Assert.Contains("EnsureExactDataAttribute(effect, \"relativity\", \"Assign\")", code, StringComparison.Ordinal);

        Assert.Contains("AddRevealLosDataEffectEditor", code, StringComparison.Ordinal);
        Assert.Contains("\"LOS revealed\"", code, StringComparison.Ordinal);
        Assert.Contains("CreateEnableDisableAmountCombo(effect, relativity: \"Absolute\"", code, StringComparison.Ordinal);
    }

    private static string ReadTechnologyEditor()
    {
        var root = FindProjectRoot();
        return File.ReadAllText(Path.Combine(root, "Windows", "TechnologyEditorView.axaml.cs"));
    }

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
