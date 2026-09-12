using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class TechnologyDataRound17RegressionTests
{
    [Fact]
    public void FixedInitialVeterancyRelativityIsSerializedButNotDisplayed()
    {
        var code = ReadTechnologyEditor();

        Assert.Contains("EnsureExactDataAttribute(effect, \"relativity\", \"Assign\")", code, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateFixedAssignRelativityCombo", code, StringComparison.Ordinal);
    }

    [Fact]
    public void RegenSubtypesUseTheirRequestedStructuredEditors()
    {
        var code = ReadTechnologyEditor();

        Assert.Contains("AddUnitRegenRateLimitDataEffectEditor", code, StringComparison.Ordinal);
        Assert.Contains("ItemsSource = new[] { \"Player\", \"Unit\" }", code, StringComparison.Ordinal);
        Assert.Contains("SetCaseInsensitiveAttribute(currentTarget, \"type\", \"Player\")", code, StringComparison.Ordinal);
        Assert.Contains("SetCaseInsensitiveAttribute(effect, \"unittype\", value)", code, StringComparison.Ordinal);
        Assert.Contains("RemoveCaseInsensitiveAttribute(currentTarget, \"unittype\")", code, StringComparison.Ordinal);
        Assert.Contains("currentTarget.Value = value", code, StringComparison.Ordinal);

        foreach (var subtype in new[]
                 {
                     "UnitShieldRegenDamageTimeout", "UnitShieldRegenIdleTimeout",
                     "UnitShieldRegenRateLimit", "UnitShieldRegenRate"
                 })
        {
            Assert.Contains($"\"{subtype}\"", code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void YieldEditorsReuseWorkRateLayoutsWithGatherAndNoOfferedOverride()
    {
        var code = ReadTechnologyEditor();

        Assert.Contains("AddMinWorkRateDataEffectEditor(effect, content, defaultAction: \"Gather\", allowOverride: false)", code, StringComparison.Ordinal);
        Assert.Contains("AddWorkRateSpecificDataEffectEditor(effect, content, defaultAction: \"Gather\", allowOverride: false)", code, StringComparison.Ordinal);
        Assert.Contains("EnsureDefaultDataAction(effect, defaultAction)", code, StringComparison.Ordinal);
        Assert.Contains("CreateLabeledEffectSegment(\"For\", CreateStrictEffectSelector", code, StringComparison.Ordinal);
        Assert.Contains("CreateLabeledEffectSegment(\"Resource\", CreateResourceCombo(effect, \"resource\")", code, StringComparison.Ordinal);
    }

    private static string ReadTechnologyEditor()
        => File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Windows", "TechnologyEditorView.axaml.cs"));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AoMDivineDataEditor.csproj")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the AoMDivineDataEditor repository root.");
    }
}
