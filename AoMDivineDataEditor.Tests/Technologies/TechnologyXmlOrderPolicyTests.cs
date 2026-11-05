using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class TechnologyXmlOrderPolicyTests
{
    [Fact]
    public void AllKnownPropertiesFollowTheFormWithoutChangingNestedSequences()
    {
        string[] expected = ["displaynameid", "rollovertextid", "advancedrollovertextoverrideid", "icon", "status",
            "researchpoints", "devotioncost", .. TechnologyXmlOrderPolicy.OptionalFields, "cost", "techtype", "flag", "prereqs", "effects"];
        var tech = new XElement("tech", expected.Reverse().Select(tag => new XElement(tag)));
        tech.Element("effects")!.Add(new XElement("effect", "Second"), new XElement("effect", "First"));
        tech.Element("prereqs")!.Add(new XElement("or", new XElement("techstatus", "B"), new XElement("techstatus", "A")));
        var payload = tech.Element("effects")!.ToString();
        TechnologyXmlOrderPolicy.NormalizeForEditor(tech);
        Assert.Equal(expected, tech.Elements().Select(e => e.Name.LocalName));
        Assert.Equal(payload, tech.Element("effects")!.ToString());
    }

    [Fact]
    public void LateIconAndRepeatedCostsJoinTheCorrectSection()
    {
        var tech = XElement.Parse("<tech><displaynameid>ID</displaynameid><cost resourcetype='Gold'>2</cost><prereqs/><effects/></tech>");
        TechnologyXmlOrderPolicy.InsertNewField(tech, new XElement("icon", "x.png"));
        TechnologyXmlOrderPolicy.InsertNewField(tech, XElement.Parse("<cost resourcetype='Food'>1</cost>"));
        Assert.Equal(new[] { "displaynameid", "icon", "cost", "cost", "prereqs", "effects" }, tech.Elements().Select(e => e.Name.LocalName));
        Assert.Equal(new[] { "Gold", "Food" }, tech.Elements("cost").Select(e => e.Attribute("resourcetype")!.Value));
    }

    [Fact]
    public void DirectAndWrappedEffectOrderIsStableAndUnknownBoundariesAreProtected()
    {
        var tech = XElement.Parse("<tech><effects><effect type='First'/></effects><effect type='Second'/><status>x</status><future/><cost>2</cost><icon>x</icon></tech>");
        TechnologyXmlOrderPolicy.NormalizeForEditor(tech);
        Assert.Equal(new[] { "First", "Second" }, TechnologyXmlShapePolicy.EnumerateEffectsInDocumentOrder(tech).Select(e => e.Attribute("type")!.Value));
        Assert.Equal(new[] { "status", "effects", "effect", "future", "icon", "cost" }, tech.Elements().Select(e => e.Name.LocalName));
    }

    [Fact]
    public void TransformSerializationIsOrderedOnlyWhenExplicitlyPreparingAnEditedRecord()
    {
        var original = XElement.Parse("<tech name='FromToTarget'><cost resourcetype='Food'>5</cost><flag>HideAllNotifications</flag><researchpoints>2</researchpoints><status>UNOBTAINABLE</status></tech>");
        var definition = TransformAssociatedTechnologyDefinition.FromElement(original);
        Assert.Equal(original.ToString(), definition.ToElement().ToString());
        var edited = definition.ToElement();
        TechnologyXmlOrderPolicy.NormalizeForEditor(edited);
        Assert.Equal(new[] { "status", "researchpoints", "cost", "flag" }, edited.Elements().Select(e => e.Name.LocalName));
    }

    [Fact]
    public void AttachedTransformTechnologyHonorsInheritedPreservedSpace()
    {
        var source = XElement.Parse("<tech name='FromToTarget'><cost resourcetype='Food'>5</cost><researchpoints>2</researchpoints><status>UNOBTAINABLE</status></tech>");
        var copy = TransformAssociatedTechnologyDefinition.FromElement(source).ToElement();
        _ = new XElement("techtreemods", new XAttribute(XNamespace.Xml + "space", "preserve"), copy);
        TechnologyXmlOrderPolicy.NormalizeForEditor(copy);
        Assert.Equal(source.Elements().Select(e => e.Name.LocalName), copy.Elements().Select(e => e.Name.LocalName));
    }
}
