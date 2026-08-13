using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class ProtoUnitStatsDuplicatePolicyTests
{
    [Fact]
    public void CasingVariants_AreTheSameValue()
    {
        var issues = ProtoUnitStatsDuplicatePolicy.FindDuplicates(
            "unittypes",
            "Unit Types",
            ["Hopplite", "hOpPlItE", "Building"]);

        var issue = Assert.Single(issues);
        Assert.Equal("Hopplite", issue.Value);
        Assert.Equal(2, issue.Count);
    }

    [Fact]
    public void DifferentValues_AreNotDuplicates()
        => Assert.Empty(ProtoUnitStatsDuplicatePolicy.FindDuplicates(
            "mixed",
            "Mixed references",
            ["Building", "Ajax", "AbstractArcher"]));

    [Fact]
    public void XmlAudit_CoversDirectAndNestedCollectionsIndependently()
    {
        var unit = XElement.Parse("""
            <unit name="Test">
              <unittype>Hopplite</unittype>
              <unittype>HOPPLITE</unittype>
              <flag>Hero</flag>
              <flag>hero</flag>
              <respawntraindata>
                <respawntypes><unittype>Ajax</unittype><unittype>ajax</unittype></respawntypes>
                <excludetypes><unittype>Ajax</unittype></excludetypes>
              </respawntraindata>
            </unit>
            """);

        var issues = ProtoUnitStatsDuplicatePolicy.FindXmlDuplicates(unit);

        Assert.Equal(3, issues.Count);
        Assert.Contains(issues, issue => issue.Key == "unittypes");
        Assert.Contains(issues, issue => issue.Key == "flags");
        Assert.Contains(issues, issue => issue.Key == "respawntypes");
        Assert.DoesNotContain(issues, issue => issue.Key == "respawnexcludetypes");
    }
}
