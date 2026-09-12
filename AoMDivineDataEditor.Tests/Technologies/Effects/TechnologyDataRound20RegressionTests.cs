using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class TechnologyDataRound20RegressionTests
{
    [Fact]
    public void SignedIntegerNumericKindAcceptsNegativeIntegersButRejectsFractions()
    {
        var rule = new ProtoUnitNumericRule("Amount", ProtoUnitNumericKind.SignedInteger, AllowEmpty: false);

        Assert.True(ProtoUnitStatsNumericRules.Validate("-7", rule).IsValid);
        Assert.False(ProtoUnitStatsNumericRules.Validate("-7.5", rule).IsValid);
        Assert.True(ProtoUnitStatsNumericRules.IsIntegerKind(ProtoUnitNumericKind.SignedInteger));
        Assert.True(ProtoUnitStatsNumericRules.AllowsNegativeInput(ProtoUnitNumericKind.SignedInteger));
    }

    [Fact]
    public void FixedPlayerAndOverrideSubtypesUseRequestedLayouts()
    {
        var code = ReadTechnologyEditor();

        Assert.Contains("AddTimeShiftingAddDataEffectEditor", code, StringComparison.Ordinal);
        Assert.Contains("CreateUnsignedFloatEffectBox(effect, \"timeratio\"", code, StringComparison.Ordinal);
        Assert.Contains("AddFixedPlayerUnitTypeDataEffectEditor(effect, content, _prereqUnitNames)", code, StringComparison.Ordinal);
        Assert.Contains("AddPlayerRelativityAmountDataEffectEditor(effect, content, allowOverride: true)", code, StringComparison.Ordinal);
        Assert.Contains("subtype.Equals(\"PopulationLimit\"", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ConditionalResourceAndUnitCountSubtypesUseRequestedAttributes()
    {
        var code = ReadTechnologyEditor();

        Assert.Contains("AddResourceIfTechActiveDataEffectEditor", code, StringComparison.Ordinal);
        Assert.Contains("GetCaseInsensitiveAttribute(effect, \"tech\")", code, StringComparison.Ordinal);
        Assert.Contains("MigrateDataAttribute(effect, \"active\", \"tech\")", code, StringComparison.Ordinal);
        Assert.Contains("_original.Keys.Concat(_modified.Keys)", code, StringComparison.Ordinal);
        Assert.Contains("AddResourceByUnitCountDataEffectEditor", code, StringComparison.Ordinal);
        Assert.Contains("SetCaseInsensitiveAttribute(effect, \"includedead\", \"true\")", code, StringComparison.Ordinal);
        Assert.Contains("AddPartisanUnitDataEffectEditor", code, StringComparison.Ordinal);
        Assert.Contains("CreateLabeledEffectSegment(\"Partisan\", CreateStrictEffectSelector(\n            _protoUnitNames", code.Replace("\r\n", "\n"), StringComparison.Ordinal);
    }

    [Fact]
    public void TrickleLimitsUseRestrictedRelativityAndSignedIntegerAmount()
    {
        var code = ReadTechnologyEditor();

        Assert.Contains("AddResourceTrickleLimitDataEffectEditor", code, StringComparison.Ordinal);
        Assert.Contains("CreateRestrictedDataRelativityCombo(effect, [\"Absolute\", \"Assign\"])", code, StringComparison.Ordinal);
        Assert.Contains("CreateSignedIntegerEffectBox(effect, \"amount\"", code, StringComparison.Ordinal);
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
