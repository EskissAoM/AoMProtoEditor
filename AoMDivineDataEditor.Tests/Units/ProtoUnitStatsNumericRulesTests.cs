using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class ProtoUnitStatsNumericRulesTests
{
    [Theory]
    [InlineData("populationcount", "12", "12")]
    [InlineData("cost:Food", "12", "12")]
    [InlineData("cost:Food", "-12", "0")]
    [InlineData("buildlimit", "1", "1")]
    [InlineData("buildlimit", "0", "1")]
    [InlineData("resourcepriority", "0.25", "0.25")]
    [InlineData("bloodscalemodify", "-1.5", "-1.5")]
    [InlineData("allowedheightvariance", "-2", "-2")]
    [InlineData("damageshading.threshold", "1.5", "1")]
    [InlineData("directionalarmor.angle", "999", "360")]
    [InlineData("directionalarmor.value", "-2", "0")]
    [InlineData("minimapcolor.red", "300", "255")]
    public void RegisteredRule_ValidatesAndNormalizes(string key, string input, string expected)
    {
        Assert.True(ProtoUnitStatsNumericRules.TryGetRule(key, out var rule));

        var result = ProtoUnitStatsNumericRules.Validate(input, rule);

        Assert.True(result.IsValid, result.ErrorMessage);
        Assert.Equal(expected, result.NormalizedValue);
    }

    [Theory]
    [InlineData("populationcount", "1.5")]
    [InlineData("cost:Food", "1.5")]
    [InlineData("resourcepriority", "0")]
    [InlineData("conversionresistance", "-1")]
    [InlineData("damageshading.time", "1.5")]
    [InlineData("directionalarmor.angle", "1.5")]
    [InlineData("minimapcolor.blue", "blue")]
    [InlineData("maxhitpoints", "NaN")]
    [InlineData("maxhitpoints", "Infinity")]
    [InlineData("maxhitpoints", "1,5")]
    public void RegisteredRule_RejectsInvalidInput(string key, string input)
    {
        Assert.True(ProtoUnitStatsNumericRules.TryGetRule(key, out var rule));

        Assert.False(ProtoUnitStatsNumericRules.Validate(input, rule).IsValid);
    }

    [Fact]
    public void EmptyOptionalField_IsValidAndNormalizesToEmpty()
    {
        Assert.True(ProtoUnitStatsNumericRules.TryGetRule("maxhitpoints", out var rule));

        var result = ProtoUnitStatsNumericRules.Validate("  ", rule);

        Assert.True(result.IsValid);
        Assert.Equal("", result.NormalizedValue);
    }

    [Fact]
    public void EmptyOpenedRequiredField_IsRejected()
    {
        Assert.True(ProtoUnitStatsNumericRules.TryGetRule("unitregen", out var rule));

        Assert.False(ProtoUnitStatsNumericRules.Validate("", rule).IsValid);
    }

    [Theory]
    [InlineData("carrycapacity:dropoffmultiplier:Food")]
    [InlineData("initialresource:Favor")]
    [InlineData("killreward:Gold")]
    [InlineData("resourcereturn:Wood")]
    [InlineData("resourcereturnrate:Food")]
    [InlineData("respawntraindata.food")]
    public void DynamicNumericFamilies_AreRegistered(string key)
    {
        Assert.True(ProtoUnitStatsNumericRules.TryGetRule(key, out _));
    }

    [Theory]
    [InlineData("maxhitpoints")]
    [InlineData("initialhitpoints")]
    [InlineData("maxshieldpoints")]
    [InlineData("initialshieldpoints")]
    public void HitpointAndShieldCapacityFields_AreUnsignedIntegers(string key)
    {
        Assert.True(ProtoUnitStatsNumericRules.TryGetRule(key, out var rule));
        Assert.Equal(ProtoUnitNumericKind.UnsignedInteger, rule.Kind);
        Assert.False(ProtoUnitStatsNumericRules.Validate("1.7", rule).IsValid);
    }
}
