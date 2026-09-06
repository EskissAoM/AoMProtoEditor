using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class TechnologyDataRound25RegressionTests
{
    [Fact]
    public void EnableSupportsUnitAndPlayerSystemTargetsWithFixedAssignToggle()
    {
        var code = ReadTechnologyEditor();

        Assert.Contains("AddEnableDataEffectEditor(effect, content)", code, StringComparison.Ordinal);
        Assert.Contains("ItemsSource = new[] { \"Unit\", \"Player\" }", code, StringComparison.Ordinal);
        Assert.Contains("[\"BonusUnits\", \"BountyResourceEarning\"]", code, StringComparison.Ordinal);
        Assert.Contains("_protoUnitNames", ExtractMethod(code, "AddEnableDataEffectEditor"), StringComparison.Ordinal);
        Assert.DoesNotContain("_prereqUnitNames", ExtractMethod(code, "AddEnableDataEffectEditor"), StringComparison.Ordinal);
        Assert.Contains("CreateEnableDisableAmountCombo(effect)", ExtractMethod(code, "AddEnableDataEffectEditor"), StringComparison.Ordinal);
        Assert.Contains("EnsureExactDataAttribute(effect, \"relativity\", \"Assign\")", ExtractMethod(code, "AddEnableDataEffectEditor"), StringComparison.Ordinal);
    }

    [Fact]
    public void SharedTechnologyTargetEditorUsesCanonicalTargetTypesAndAllTechOptions()
    {
        var code = ReadTechnologyEditor();
        var method = ExtractMethod(code, "AddTechnologyTargetEditor");
        var allTechMethod = ExtractMethod(code, "AddAllTechnologiesTargetOptions");
        var targetMapping = ExtractMethod(code, "TechnologyTargetDisplayToType", returnType: "string");

        Assert.Contains("\"Tech\", \"All Techs\", \"Tech with Flag\", \"Tech Type\"", method, StringComparison.Ordinal);
        Assert.Contains("\"techAll\"", targetMapping, StringComparison.Ordinal);
        Assert.Contains("\"techWithFlag\"", method, StringComparison.Ordinal);
        Assert.Contains("\"TechType\"", method, StringComparison.Ordinal);
        Assert.Contains("Content = \"No age tech\"", allTechMethod, StringComparison.Ordinal);
        Assert.Contains("\"ignoreageups\"", allTechMethod, StringComparison.Ordinal);
        Assert.Contains("CreateOptionalPropertyButton(\"Exclude tech\")", allTechMethod, StringComparison.Ordinal);
        Assert.Contains("\"excludetypes\"", allTechMethod, StringComparison.Ordinal);
        Assert.Contains("string.Join('|',", allTechMethod, StringComparison.Ordinal);
        Assert.Contains("EditorChipService.CreateBlueChip", allTechMethod, StringComparison.Ordinal);
        var normalizedAllTechMethod = allTechMethod.Replace("\r\n", "\n");
        var labelIndex = normalizedAllTechMethod.IndexOf("exclusionGroup.Children.Add(CreateInlineLabel(\"Exclude\"))", StringComparison.Ordinal);
        var editableIndex = normalizedAllTechMethod.IndexOf("if (IsModifiedTab)", labelIndex, StringComparison.Ordinal);
        var selectorIndex = normalizedAllTechMethod.IndexOf("exclusionGroup.Children.Add(CreateStrictEffectSelector", editableIndex, StringComparison.Ordinal);
        Assert.True(labelIndex >= 0 && editableIndex > labelIndex && selectorIndex > editableIndex);
    }

    [Fact]
    public void DoubleEffectCostAndResearchPointsUseRequestedSharedControls()
    {
        var code = ReadTechnologyEditor();
        var doubleEffect = ExtractMethod(code, "AddDoubleEffectDataEffectEditor");
        var cost = ExtractMethod(code, "AddCostDataEffectEditor");
        var research = ExtractMethod(code, "AddResearchPointsDataEffectEditor");

        Assert.Contains("AddTechnologyTargetEditor(effect, row)", doubleEffect, StringComparison.Ordinal);
        Assert.Contains("CreateEnableDisableAmountCombo(effect)", doubleEffect, StringComparison.Ordinal);
        Assert.Contains("ItemsSource = new[] { \"Unit\", \"Tech\" }", cost, StringComparison.Ordinal);
        Assert.Contains("AddDataRelativityAndAmountEditors(effect, row, allowOverride: true)", cost, StringComparison.Ordinal);
        Assert.Contains("CreateResourceCombo(effect, \"resource\")", cost, StringComparison.Ordinal);
        Assert.Contains("\"reqTech\"", cost, StringComparison.Ordinal);
        Assert.Contains("AddTechnologyTargetEditor(effect, row)", research, StringComparison.Ordinal);
        Assert.Contains("AddDataRelativityAndAmountEditors(effect, row)", research, StringComparison.Ordinal);
    }

    private static string ExtractMethod(string source, string methodName, string returnType = "void")
    {
        var start = source.IndexOf($"private static {returnType} {methodName}(", StringComparison.Ordinal);
        if (start < 0)
            start = source.IndexOf($"private {returnType} {methodName}(", StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find {methodName}.");
        var next = source.IndexOf("\n    private ", start + 1, StringComparison.Ordinal);
        return next < 0 ? source[start..] : source[start..next];
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
