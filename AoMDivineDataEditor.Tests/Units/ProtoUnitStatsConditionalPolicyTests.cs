using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class ProtoUnitStatsConditionalPolicyTests
{
    [Theory]
    [InlineData("ArmorSpecific")]
    [InlineData("DamageSpecific")]
    [InlineData("aRmOrSpEcIfIc")]
    public void SpecificModifyTypes_RequireDamageType(string modifyType)
        => Assert.True(ProtoUnitStatsConditionalPolicy.RequiresDamageType(modifyType));

    [Theory]
    [InlineData("")]
    [InlineData("Speed")]
    [InlineData("Hitpoints")]
    public void OtherModifyTypes_DoNotRequireDamageType(string modifyType)
        => Assert.False(ProtoUnitStatsConditionalPolicy.RequiresDamageType(modifyType));

    [Fact]
    public void BuildingAttributeAvailability_IsCaseInsensitive()
    {
        Assert.True(ProtoUnitStatsConditionalPolicy.CanOfferBuildingOnlyAttribute(["Hero", "bUiLdInG"]));
        Assert.False(ProtoUnitStatsConditionalPolicy.CanOfferBuildingOnlyAttribute(["Hero", "AbstractArcher"]));
    }

    [Theory]
    [InlineData(false, false, false, false, "trainpoints")]
    [InlineData(true, false, false, true, "trainpoints")]
    [InlineData(false, true, true, false, "buildpoints")]
    [InlineData(true, true, true, false, "trainpoints")]
    [InlineData(true, true, false, true, "buildpoints")]
    [InlineData(true, true, false, false, "trainpoints")]
    public void PointsTag_FollowsUnitBuildingAndLegacyRules(
        bool hasUnit,
        bool hasBuilding,
        bool hadTrainPoints,
        bool hadBuildPoints,
        string expected)
    {
        var unitTypes = new List<string>();
        if (hasUnit) unitTypes.Add("Unit");
        if (hasBuilding) unitTypes.Add("Building");

        Assert.Equal(expected, ProtoUnitStatsConditionalPolicy.ResolvePointsTag(
            unitTypes,
            hadTrainPoints,
            hadBuildPoints));
    }

    [Fact]
    public void UnknownLegacyModifyType_IsPreservedOnlyWhileUnchanged()
    {
        Assert.Equal("FutureModify", ProtoUnitStatsConditionalPolicy.ResolveModifyTypeOrLegacy(
            "FutureModify", "FutureModify"));
        Assert.Equal("ArmorSpecific", ProtoUnitStatsConditionalPolicy.ResolveModifyTypeOrLegacy(
            "armorspecific", "FutureModify"));
        Assert.Empty(ProtoUnitStatsConditionalPolicy.ResolveModifyTypeOrLegacy(
            "AnotherFutureModify", "FutureModify"));
    }
}
