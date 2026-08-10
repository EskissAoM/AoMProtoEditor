using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class ProtoUnitCommandTransformRulesTests
{
    [Fact]
    public void UniqueDefaults_AddOnlyUniqueStructuralAndDefaultFlags()
    {
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "transform", "researchonselected", "customflag" };

        ProtoUnitCommandTransformRules.ApplyModeDefaults(flags, ProtoUnitCommandTransformKind.Unique);

        Assert.Contains("transformselected", flags);
        Assert.Contains("displayontarget", flags);
        Assert.Contains("unitcommand", flags);
        Assert.Contains("customflag", flags);
        Assert.DoesNotContain("transform", flags);
        Assert.DoesNotContain("transformvillager", flags);
        Assert.DoesNotContain("researchonselected", flags);
    }

    [Fact]
    public void MultipleDefaults_AddRequiredAndMultipleDefaults()
    {
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "transformselected" };

        ProtoUnitCommandTransformRules.ApplyModeDefaults(flags, ProtoUnitCommandTransformKind.Multiple);

        Assert.Contains("transform", flags);
        Assert.Contains("displayontarget", flags);
        Assert.Contains("researchonselected", flags);
        Assert.Contains("unitcommand", flags);
        Assert.DoesNotContain("transformselected", flags);
        Assert.DoesNotContain("transformvillager", flags);
    }

    [Fact]
    public void EnsureStructuralFlag_DoesNotRestoreRemovedOptionalDefaults()
    {
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "displayontarget" };

        ProtoUnitCommandTransformRules.EnsureStructuralFlag(flags, ProtoUnitCommandTransformKind.Multiple);

        Assert.Contains("transform", flags);
        Assert.Contains("displayontarget", flags);
        Assert.DoesNotContain("researchonselected", flags);
        Assert.DoesNotContain("unitcommand", flags);
    }

    [Fact]
    public void Validation_RequiresFourFieldsAndCanEnforceInlineOwner()
    {
        var transform = new ProtoUnitTransformDefinition
        {
            From = "Hoplite",
            To = "Wolf",
            Tech = "ArchaicAgeGreek"
        };
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["associatedtech"] = "SomeTech"
        };
        var protoUnits = new[] { "Hoplite", "Wolf" };
        var techs = new[] { "ArchaicAgeGreek", "SomeTech" };

        var valid = ProtoUnitCommandTransformRules.ValidateRequired(transform, values, protoUnits, techs, "Hoplite");
        var wrongOwner = ProtoUnitCommandTransformRules.ValidateRequired(transform, values, protoUnits, techs, "VillagerGreek");

        Assert.True(valid.IsValid);
        Assert.False(wrongOwner.IsValid);
        Assert.False(wrongOwner.FromValid);
        Assert.True(wrongOwner.ToValid);
        Assert.True(wrongOwner.PrereqTechValid);
        Assert.True(wrongOwner.AssociatedTechValid);
    }

    [Fact]
    public void Validation_RejectsMissingOrUnknownRequiredValues()
    {
        var transform = new ProtoUnitTransformDefinition { From = "Hoplite", To = "MissingUnit", Tech = "" };
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["associatedtech"] = "MissingTech" };

        var result = ProtoUnitCommandTransformRules.ValidateRequired(
            transform,
            values,
            new[] { "Hoplite", "Wolf" },
            new[] { "ArchaicAgeGreek" });

        Assert.True(result.FromValid);
        Assert.False(result.ToValid);
        Assert.False(result.PrereqTechValid);
        Assert.False(result.AssociatedTechValid);
        Assert.False(result.IsValid);
    }
}
