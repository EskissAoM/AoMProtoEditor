using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class TechnologyPass3StringSafetyTests
{
    [Fact]
    public void Generated_string_namespace_collision_is_detected()
    {
        var owner = TechnologyStringIdPolicy.FindNamespaceCollision(
            "A_B",
            new[] { "OtherTech", "A-B" });

        Assert.Equal("A-B", owner);
        Assert.Equal(
            TechnologyStringIdPolicy.BuildId("A-B", "displaynameid"),
            TechnologyStringIdPolicy.BuildId("A_B", "displaynameid"));
    }

    [Fact]
    public void Generated_string_namespace_check_ignores_technology_being_renamed()
    {
        var owner = TechnologyStringIdPolicy.FindNamespaceCollision(
            "RenamedTech",
            new[] { "OldTech", "OtherTech" },
            excludedTechnologyName: "OldTech");

        Assert.Null(owner);
    }

    [Fact]
    public void String_removal_ownership_checks_elements_and_attributes_globally()
    {
        var technologies = new[]
        {
            XElement.Parse("<tech name=\"One\"><displaynameid>STR_SHARED</displaynameid></tech>"),
            XElement.Parse("<tech name=\"Two\"><effect tooltipID=\"STR_TOOLTIP\" /></tech>")
        };

        Assert.True(TechnologyStringIdPolicy.IsReferencedByTechnologies("str_shared", technologies));
        Assert.True(TechnologyStringIdPolicy.IsReferencedByTechnologies("str_tooltip", technologies));
        Assert.False(TechnologyStringIdPolicy.IsReferencedByTechnologies("STR_UNUSED", technologies));
    }
}
