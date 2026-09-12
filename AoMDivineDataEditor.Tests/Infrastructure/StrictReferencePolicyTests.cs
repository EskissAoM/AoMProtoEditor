using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class StrictReferencePolicyTests
{
    private static readonly string[] Options = ["Building", "AbstractArcher", "CamelRider"];

    [Fact]
    public void CatalogMatch_IsAcceptedAndCanonicalizedCaseInsensitively()
    {
        var result = StrictReferencePolicy.Validate("camelrider", "", Options, true, "Unit Type");

        Assert.True(result.IsValid);
        Assert.Equal("CamelRider", result.CanonicalValue);
    }

    [Fact]
    public void UnchangedMissingLegacyValue_IsPreservedExactly()
    {
        var result = StrictReferencePolicy.Validate("Legacy.Type", "Legacy.Type", Options, true, "Unit Type");

        Assert.True(result.IsValid);
        Assert.Equal("Legacy.Type", result.CanonicalValue);
    }

    [Fact]
    public void CasingChangeToMissingLegacyValue_IsRejected()
    {
        var result = StrictReferencePolicy.Validate("legacy.type", "Legacy.Type", Options, true, "Unit Type");

        Assert.False(result.IsValid);
    }

    [Fact]
    public void NewMissingValue_IsRejected()
    {
        var result = StrictReferencePolicy.Validate("MadeUpType", "Legacy.Type", Options, true, "Unit Type");

        Assert.False(result.IsValid);
        Assert.Contains("Unit Type", result.ErrorMessage);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void EmptyValue_RespectsOptionality(bool allowEmpty, bool expected)
    {
        var result = StrictReferencePolicy.Validate("", "", Options, allowEmpty, "Reference");

        Assert.Equal(expected, result.IsValid);
    }
}
