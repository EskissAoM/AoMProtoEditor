using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class TechnologyDataRound22RegressionTests
{
    [Fact]
    public void BuildingChainEditorsUseCanonicalConditionalAttributes()
    {
        var code = ReadTechnologyEditor();

        Assert.Contains("AddBuildingChainEffectDataEffectEditor", code, StringComparison.Ordinal);
        Assert.Contains("effect, \"effecttype\", [\"Connected\", \"Isolate\", \"InRange\"]", code, StringComparison.Ordinal);
        Assert.Contains("CreateRequiredDataTypeCombo(effect, currentModifyType == \"DamageSpecific\")", code, StringComparison.Ordinal);
        Assert.Contains("row, effect, \"Damage time out\", \"timeout\", \"0\"", code, StringComparison.Ordinal);
        Assert.Contains("AddBuildingChainResourceFactorDataEffectEditor", code, StringComparison.Ordinal);
    }

    [Fact]
    public void GodPowerUsesPowerAndPositiveCooldown()
    {
        var code = ReadTechnologyEditor();

        Assert.Contains("AddGodPowerDataEffectEditor", code, StringComparison.Ordinal);
        Assert.Contains("GetCaseInsensitiveAttribute(effect, \"power\")", code, StringComparison.Ordinal);
        Assert.Contains("CreatePositiveFloatEffectBox(effect, \"cooldown\"", code, StringComparison.Ordinal);
        Assert.Contains("CreateRestrictedDataRelativityCombo(effect, [\"Absolute\", \"Assign\"])", code, StringComparison.Ordinal);
    }

    [Fact]
    public void GoalFlagAndSpawnLocationsUseFixedAssignLayouts()
    {
        var code = ReadTechnologyEditor().Replace("\r\n", "\n");

        Assert.Contains("AddSetGoalFlagDataEffectEditor", code, StringComparison.Ordinal);
        Assert.Contains("\"AgeRestricted\", \"ArchaicSpawn\", \"CultureRestricted\", \"CurrentAgeOnly\"", code, StringComparison.Ordinal);
        Assert.Contains("GetCaseInsensitiveAttribute(effect, \"flagname\")", code, StringComparison.Ordinal);
        Assert.Contains("AddSetGoalSpawnLocationDataEffectEditor", code, StringComparison.Ordinal);
        Assert.Contains("GetCaseInsensitiveAttribute(effect, \"locationprotoid\")", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ResourceByKbQueryUsesRequestedFieldsAndPositiveOptionalCap()
    {
        var code = ReadTechnologyEditor();

        Assert.Contains("AddResourceByKbQueryDataEffectEditor", code, StringComparison.Ordinal);
        Assert.Contains("GetCaseInsensitiveAttribute(effect, \"queryunittype\")", code, StringComparison.Ordinal);
        Assert.Contains("effect, \"querystate\", [\"Building\", \"Alive\", \"Dead\", \"Queued\", \"Any\"]", code, StringComparison.Ordinal);
        Assert.Contains("row, effect, \"Resource cap\", \"resourcecap\", \"1\"", code, StringComparison.Ordinal);
        Assert.Contains("ProtoUnitNumericKind.PositiveInteger, requirePositive: true", code, StringComparison.Ordinal);
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
