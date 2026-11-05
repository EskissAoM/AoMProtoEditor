using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class PowerXmlFieldOrderTests
{
    [Fact]
    public void EveryRegisteredTypeUsesTheSameGroupOwnershipAsItsForm()
    {
        foreach (var schema in PowerSchemaRegistry.Schemas.Values)
        {
            var ranks = PowerXmlFieldOrder.GetRanks(schema.Name);
            foreach (var group in schema.SpecificGroups)
            {
                var tags = PowerXmlFieldOrder.GetGroupTags(group);
                Assert.NotEmpty(tags);
                Assert.All(tags, tag => Assert.True(ranks.ContainsKey(tag), $"{schema.Name}: {group}/{tag}"));
            }
        }
    }

    [Theory]
    [InlineData("Bolt", "strikeproto", "sparksvfxproto", "strikeheight")]
    [InlineData("TempUnit", "createunit", "attachproto", "ageadjustment")]
    [InlineData("CageUnit", "targetfloatheight", "killsimilartargetsinradius", "vfxarea")]
    [InlineData("UnitModify", "vfxproto", "birthvfx", "unitmodify")]
    public void CommonPropertiesPrecedeTypeSpecificGroupsInFormOrder(string type, string first, string second, string third)
    {
        var entry = XElement.Parse($"<power name='A' type='{type}'><{third}/><{second}/><placement>Full</placement><activetime>1</activetime><{first}/><rolloverid>R</rolloverid><displaynameid>N</displaynameid></power>");
        PowerXmlFieldOrder.NormalizeForEditor(entry);
        Assert.Equal(new[] { "displaynameid", "rolloverid", "activetime", "placement", first, second, third }, entry.Elements().Select(e => e.Name.LocalName));
    }

    [Fact]
    public void MissingCommonFieldsJoinTheirSectionWithoutReorderingRepeatedRecords()
    {
        var entry = XElement.Parse("<power name='A' type='UnitModify'><placement>Full</placement><unitmodify id='B'/><unitmodify id='A'/></power>");
        PowerCatalog.SetField(entry, "displaynameid", "NAME");
        PowerCatalog.SetField(entry, "icon", "x.png");
        PowerXmlFieldOrder.NormalizeForEditor(entry);
        Assert.Equal(new[] { "displaynameid", "icon", "placement", "unitmodify", "unitmodify" }, entry.Elements().Select(e => e.Name.LocalName));
        Assert.Equal(new[] { "B", "A" }, entry.Elements("unitmodify").Select(e => e.Attribute("id")!.Value));
    }

    [Fact]
    public void SavePreparationOrdersOnlyDirtyStructuredPowerAndRetainsRawAndSiblingOrder()
    {
        var document = XDocument.Parse("<powersmod><power name='Structured' type='Bolt'><placement>Full</placement><activetime>1</activetime></power><power name='Raw' type='Bolt'><placement>Full</placement><activetime>1</activetime></power><power name='Untouched' type='Bolt'><placement>Full</placement><activetime>1</activetime></power></powersmod>");
        PowerSavePreparation.Prepare(document, ["Structured", "Raw"], [], new Dictionary<string, string>(), [], new HashSet<string> { "Raw" });
        Assert.Equal("activetime", PowerCatalog.Find(document, "Structured")!.Elements().First().Name.LocalName);
        Assert.Equal("placement", PowerCatalog.Find(document, "Raw")!.Elements().First().Name.LocalName);
        Assert.Equal("placement", PowerCatalog.Find(document, "Untouched")!.Elements().First().Name.LocalName);
    }

    [Theory]
    [InlineData("<effect xml:space='preserve'>prefix<child/>suffix</effect>")]
    [InlineData("<effect>prefix<child/>suffix</effect>")]
    public void NestedProtectedContentIsUnchangedByFormattingAndPreview(string xml)
    {
        var effect = XElement.Parse(xml, LoadOptions.PreserveWhitespace);
        var before = effect.ToString(SaveOptions.DisableFormatting);
        PowerCatalog.NormalizeNestedElementIndentation(effect);
        Assert.Equal(before, effect.ToString(SaveOptions.DisableFormatting));
        var root = new XElement("power", new XAttribute("name", "A"), new XAttribute("type", "GeneralEffect"), new XAttribute("godpower", ""), effect);
        var preview = XElement.Parse(PowerCatalog.FormatPreview(root), LoadOptions.PreserveWhitespace);
        Assert.Equal(before, preview.Element("effect")!.ToString(SaveOptions.DisableFormatting));
    }

    [Fact]
    public void AbilityRecordsDoNotAcquirePowerOrdering()
    {
        var entry = XElement.Parse("<power name='A' type='UnitAction'><placement>Unit</placement><displaynameid>N</displaynameid></power>");
        var before = entry.ToString();
        PowerXmlFieldOrder.NormalizeForEditor(entry);
        Assert.Equal(before, entry.ToString());
    }
}
