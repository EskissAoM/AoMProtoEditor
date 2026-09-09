using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class GameIdentifierReferencePolicyTests
{
    [Fact]
    public void ProtoUnitRename_MigratesKnownTechnologyAndProtoReferences_WithoutRenamingDefinition()
    {
        var document = XDocument.Parse(
            """
            <root>
              <unit name="OldUnit">
                <deadreplacement>OldUnit</deadreplacement>
                <protoaction><name>Attack</name><projectile>OldUnit</projectile></protoaction>
              </unit>
              <unit name="OtherUnit"><train>OldUnit</train><spawn type="dead">OldUnit</spawn></unit>
              <tech name="TestTech">
                <effects>
                  <effect type="Data" subtype="Hitpoints" unittype="OldUnit">
                    <target type="ProtoUnit">OldUnit</target>
                  </effect>
                  <effect type="TransformUnit" fromProtoID="OldUnit" toProtoID="OtherUnit" />
                </effects>
              </tech>
            </root>
            """);

        var changed = GameIdentifierReferencePolicy.RenameReferences(
            document, GameIdentifierKind.ProtoUnit, "OldUnit", "NewUnit");

        Assert.Equal(7, changed);
        Assert.Equal("OldUnit", (string?)document.Descendants("unit").First().Attribute("name"));
        Assert.DoesNotContain(document.Descendants().Attributes(), attribute =>
            attribute.Name.LocalName != "name" && attribute.Value == "OldUnit");
        Assert.DoesNotContain(document.Descendants().Where(element => !element.HasElements), element =>
            element.Value == "OldUnit");
    }

    [Fact]
    public void UnitTypeRename_MigratesAssignments_ActionFilters_AndTechnologyTargets()
    {
        var document = XDocument.Parse(
            """
            <root>
              <unit name="UnitA">
                <unittype>OldType</unittype>
                <protoaction>
                  <name>Attack</name>
                  <damagebonus type="OldType">2</damagebonus>
                  <modifyabstracttype>OldType</modifyabstracttype>
                  <castpowertargettype>OldType</castpowertargettype>
                  <modifydamagetargettype>OldType</modifydamagetargettype>
                  <rate type="OldType">1</rate>
                  <onhiteffect type="Boost">
                    <target>OldType</target>
                    <target attacktype="OldType" />
                    <target ignoretype="OldType" />
                  </onhiteffect>
                  <empowerdata><OldType><active>1</active></OldType></empowerdata>
                </protoaction>
              </unit>
              <tech name="TestTech">
                <effects>
                  <effect type="Data" subtype="Hitpoints" unittype="OldType">
                    <target type="ProtoUnit">OldType</target>
                  </effect>
                </effects>
              </tech>
            </root>
            """);

        var changed = GameIdentifierReferencePolicy.RenameReferences(
            document, GameIdentifierKind.UnitType, "OldType", "NewType");

        Assert.Equal(12, changed);
        Assert.DoesNotContain(document.Descendants().Attributes(), attribute => attribute.Value == "OldType");
        Assert.DoesNotContain(document.Descendants().Where(element => !element.HasElements), element => element.Value == "OldType");
        Assert.NotNull(document.Descendants("empowerdata").Single().Element("NewType"));
    }

    [Fact]
    public void ProtoActionRename_MigratesEveryManagedLocalReference()
    {
        var unit = XElement.Parse(
            """
            <unit name="UnitA">
              <selfdestructprotoaction>OldAction</selfdestructprotoaction>
              <birthprotoaction>OldAction</birthprotoaction>
              <stackprotoaction>OldAction</stackprotoaction>
              <protoaction>
                <name>NewAction</name>
                <areaprotoaction>OldAction</areaprotoaction>
                <stackcontrol>
                  <stackaddaction>OldAction</stackaddaction>
                  <stacksubaction>OldAction</stacksubaction>
                </stackcontrol>
                <onhiteffect type="Infect" protoaction="OldAction" />
              </protoaction>
            </unit>
            """);

        var changed = GameIdentifierReferencePolicy.RenameProtoActionReferences(
            unit, "OldAction", "NewAction");

        Assert.Equal(7, changed);
        Assert.DoesNotContain(unit.DescendantsAndSelf().Attributes(), attribute => attribute.Value == "OldAction");
        Assert.DoesNotContain(unit.Descendants().Where(element => !element.HasElements), element => element.Value == "OldAction");
    }

    [Fact]
    public void TechnologyProtoActionRename_IsScopedToTheOwningProtoUnit()
    {
        var document = XDocument.Parse(
            """
            <techtreemods>
              <tech name="ForOwner"><effects><effect type="Data" subtype="ActionEnable" action="OldAction"><target type="ProtoUnit">OwnerUnit</target></effect></effects></tech>
              <tech name="ForOther"><effects><effect type="Data" subtype="ActionEnable" action="OldAction"><target type="ProtoUnit">OtherUnit</target></effect></effects></tech>
            </techtreemods>
            """);

        var changed = GameIdentifierReferencePolicy.RenameTechnologyProtoActionReferences(
            document,
            new ProtoActionRenameOperation("OwnerUnit", "OldAction", "NewAction"));

        Assert.Equal(1, changed);
        Assert.Equal("NewAction", (string?)document.Descendants("effect").First().Attribute("action"));
        Assert.Equal("OldAction", (string?)document.Descendants("effect").Last().Attribute("action"));
    }

    [Fact]
    public void NamedAttributeRename_UpdatesCommandReferencesWithoutTouchingOtherAttributes()
    {
        var document = XDocument.Parse(
            """
            <techtreemods>
              <effect type="Data" subtype="CommandAdd" command="OldCommand" proto="OldCommand" />
            </techtreemods>
            """);

        var changed = GameIdentifierReferencePolicy.RenameAttributeReferences(
            document, "command", "OldCommand", "NewCommand");

        Assert.Equal(1, changed);
        var effect = Assert.Single(document.Descendants("effect"));
        Assert.Equal("NewCommand", (string?)effect.Attribute("command"));
        Assert.Equal("OldCommand", (string?)effect.Attribute("proto"));
    }

    [Fact]
    public void UnknownIdentifierShapedField_IsReportedInsteadOfSilentlyLeftDangling()
    {
        var document = XDocument.Parse(
            """
            <root>
              <unit name="OldUnit"><futureprotounit>OldUnit</futureprotounit></unit>
            </root>
            """);

        var unknown = GameIdentifierReferencePolicy.FindUnrecognizedReferences(
            document, GameIdentifierKind.ProtoUnit, "OldUnit");

        Assert.Equal(["<futureprotounit>"], unknown);
    }

    [Fact]
    public void UnknownActionReference_IsReportedInsteadOfSilentlyLeftDangling()
    {
        var unit = XElement.Parse(
            """
            <unit name="UnitA">
              <protoaction><name>NewAction</name><futurelinkedaction>OldAction</futurelinkedaction></protoaction>
            </unit>
            """);

        var unknown = GameIdentifierReferencePolicy.FindUnrecognizedProtoActionReferences(unit, "OldAction");

        Assert.Equal(["<futurelinkedaction>"], unknown);
    }

    [Fact]
    public void UnknownTechTypeReference_IsReportedWithoutFlaggingSupportedShapes()
    {
        var document = XDocument.Parse(
            """
            <techtreemods>
              <tech name="TechA">
                <techtype>OldType</techtype>
                <effects>
                  <effect type="Data" subtype="ResearchPoints" futuretechtype="OldType">
                    <target type="TechType">OldType</target>
                  </effect>
                </effects>
              </tech>
            </techtreemods>
            """);

        var unknown = GameIdentifierReferencePolicy.FindUnrecognizedTechTypeReferences(document, "OldType");

        Assert.Equal(["@futuretechtype"], unknown);
    }
}
