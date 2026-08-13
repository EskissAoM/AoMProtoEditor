using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class InternalNamePolicyTests
{
    [Theory]
    [InlineData("Ajax")]
    [InlineData("Ajax_2")]
    [InlineData("Ajax-2")]
    [InlineData("A0_z-Z9")]
    public void ValidInternalNames_AreAccepted(string name)
        => Assert.True(InternalNamePolicy.IsValid(name));

    [Theory]
    [InlineData("")]
    [InlineData("Has Space")]
    [InlineData("Has.Dot")]
    [InlineData("path/name")]
    [InlineData("name:tactics")]
    [InlineData("éclair")]
    public void InvalidInternalNames_AreRejected(string name)
        => Assert.False(InternalNamePolicy.IsValid(name));

    [Fact]
    public void UnchangedLegacyName_IsPreservedButCannotBeUsedAsANewName()
    {
        Assert.True(InternalNamePolicy.IsValidOrUnchangedLegacy("Legacy.Name", "Legacy.Name"));
        Assert.False(InternalNamePolicy.IsValidOrUnchangedLegacy("Another.Name", "Legacy.Name"));
        Assert.True(InternalNamePolicy.IsValidOrUnchangedLegacy("Hardened_Name", "Legacy.Name"));
    }

    [Theory]
    [InlineData("aggressive.tactics", true)]
    [InlineData("aggressive", true)]
    [InlineData("bad name.tactics", false)]
    [InlineData("bad.name.tactics", false)]
    public void TacticsFileNames_ValidateTheStem(string name, bool expected)
        => Assert.Equal(expected, InternalNamePolicy.IsValidFileName(name, ".tactics"));
}
