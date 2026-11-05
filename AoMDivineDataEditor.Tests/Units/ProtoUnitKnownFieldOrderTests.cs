using System.Reflection;
using System.Text;
using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Windows;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class ProtoUnitKnownFieldOrderTests
{
    private static XElement Parse(string body) => XElement.Parse("<unit name='U'>" + body + "</unit>", LoadOptions.PreserveWhitespace);
    private static string Shape(XElement element) => element.ToString(SaveOptions.DisableFormatting);
    private static string[] Tags(XElement unit) => unit.Elements().Select(element => element.Name.LocalName).ToArray();

    [Fact]
    public void MissingFieldsJoinTheirGroupBeforeOpaqueTrailingContent()
    {
        var unit = Parse("<displaynameid>ID</displaynameid><obstructionradiusx>2</obstructionradiusx>" +
            "<!--HP note--><maxhitpoints>10</maxhitpoints><flag>F</flag><future xml:space='preserve'> <x/> </future>");
        var original = new XElement(unit);
        ProtoXmlHandler.SetSimpleField(unit, "editornameid", "EDITOR");
        ProtoXmlHandler.SetSimpleField(unit, "turnrate", "1");
        ProtoUnitStatsXmlPreserver.PreserveUnmanagedContent(original, unit);
        ProtoUnitXmlOrderPolicy.NormalizeForEditor(unit);
        Assert.Equal(new[] { "displaynameid", "editornameid", "obstructionradiusx", "turnrate", "maxhitpoints", "flag", "future" }, Tags(unit));
        Assert.Equal(Shape(original.Element("future")!), Shape(unit.Element("future")!));
        Assert.Same(unit.Nodes().OfType<XComment>().Single(), unit.Element("maxhitpoints")!.PreviousNode);
    }

    [Fact]
    public void AddedCultureVariantJoinsExistingAssetsWithoutMovingUnknownContent()
    {
        var unit = Parse("<icon>default</icon><animfile>model</animfile><future/>");
        var original = new XElement(unit);
        ProtoXmlHandler.SetCultureAwareSimpleFields(unit, "icon", new[]
        {
            new ProtoCultureFieldEntry { Value = "default" },
            new ProtoCultureFieldEntry { Value = "greek", Culture = "Greek" }
        });
        ProtoUnitStatsXmlPreserver.PreserveUnmanagedContent(original, unit);
        ProtoUnitXmlOrderPolicy.NormalizeForEditor(unit);
        Assert.Equal(new[] { "icon", "icon", "animfile", "future" }, Tags(unit));
        Assert.Equal(new[] { "default", "greek" }, unit.Elements("icon").Select(e => e.Value));
    }

    [Fact]
    public void ApprovedSectionsAndMainPropertyRowsFollowTheForm()
    {
        string[] expected = ["displaynameid", "editornameid", "rollovertextid", "shortrollovertextid",
            "icon", "animfile", "soundsetfile", "obstructionradiusx", "obstructionradiusz", "turnrate",
            "maxhitpoints", "initialhitpoints", "unitregen", "maxshieldpoints", "initialshieldpoints", "unitshieldregen",
            "buildpoints", "populationcount", "los", "unitaitype", "impacttype", "weightclass",
            "movementtype", "maxvelocity", "maxrunvelocity", "buildlimit", "sharedbuildlimitunittypes",
            "maxcontained", "contain", "notcontain", "containedhitpointbonus", "containedspeedbonus", "containedregenrate",
            "selectionradiusx", "selectionradiusz", "hotkeycontext", "veterancyranks", "veterancybonus",
            "cost", "armor", "unittype", "flag", "train", "tech", "command", "tactics", "protoaction"];
        var unit = new XElement("unit", expected.Reverse().Select(tag => new XElement(tag, new XAttribute("keep", "yes"))));
        ProtoUnitXmlOrderPolicy.OrderKnownFieldsForEditor(unit);
        Assert.Equal(expected, Tags(unit));
        Assert.All(unit.Elements(), field => Assert.Equal("yes", (string?)field.Attribute("keep")));
    }

    [Fact]
    public void LateEditorNameAndTurnRateDoNotDragTypesBelowTraining()
    {
        var unit = Parse("<displaynameid>ID</displaynameid><icon>x</icon><obstructionradiusx>2</obstructionradiusx>" +
            "<obstructionradiusz>3</obstructionradiusz><buildpoints>10</buildpoints><cost resourcetype='Wood'>5</cost>" +
            "<armor type='Hack' value='.1'/><unittype>Building</unittype><hotkeycontext>Build</hotkeycontext>" +
            "<train>Worker</train><tech>Upgrade</tech><flag>F</flag><command>Transform</command><editornameid>ID</editornameid><turnrate>1</turnrate>");
        ProtoUnitXmlOrderPolicy.OrderKnownFieldsForEditor(unit);
        Assert.Equal(new[] { "displaynameid", "editornameid", "icon", "obstructionradiusx", "obstructionradiusz", "turnrate",
            "buildpoints", "hotkeycontext", "cost", "armor", "unittype", "flag", "train", "tech", "command" }, Tags(unit));
    }

    [Fact]
    public void RepeatedValuesAndCommandKindsKeepTheirExactSequenceAndSubtrees()
    {
        var unit = Parse("<protoaction><name>Z</name><rate type='Z'>2</rate><rate type='A'>1</rate></protoaction>" +
            "<command id='2'>B</command><optionalcommand>A</optionalcommand><command id='1'>C</command><transformcommand>X</transformcommand>" +
            "<icon culture='Zulu'>z</icon><icon>default</icon><icon culture='Alpha'>a</icon>" +
            "<cost resourcetype='Gold'>1</cost><cost resourcetype='Food'>2</cost><unittype>Z</unittype><unittype>A</unittype>" +
            "<protoaction><name>A</name><!--inside--><unknown xml:space='preserve'>  <child/> </unknown></protoaction>");
        var before = unit.Elements().ToDictionary(element => element, Shape);
        ProtoUnitXmlOrderPolicy.OrderKnownFieldsForEditor(unit);
        Assert.Equal(new[] { "z", "default", "a" }, unit.Elements("icon").Select(element => element.Value));
        Assert.Equal(new[] { "B", "A", "C", "X" }, unit.Elements().Where(e => e.Name.LocalName.EndsWith("command", StringComparison.Ordinal)).Select(e => e.Value));
        Assert.Equal(new[] { "Gold", "Food" }, unit.Elements("cost").Select(e => (string?)e.Attribute("resourcetype")));
        Assert.Equal(new[] { "Z", "A" }, unit.Elements("unittype").Select(e => e.Value));
        Assert.Equal(new[] { "Z", "A" }, unit.Elements("protoaction").Select(e => e.Element("name")!.Value));
        foreach (var element in unit.Elements()) Assert.Equal(before[element], Shape(element));
    }

    [Fact]
    public void LeadingFieldCommentTravelsWithItsField()
    {
        var unit = Parse("<protoaction><name>A</name></protoaction>\r\n  <!--HP note-->\r\n  <maxhitpoints>10</maxhitpoints>");
        var hp = unit.Element("maxhitpoints")!;
        var comment = unit.Nodes().OfType<XComment>().Single();
        ProtoUnitXmlOrderPolicy.OrderKnownFieldsForEditor(unit);
        Assert.Same(hp, unit.Elements().First());
        Assert.Same(comment, hp.PreviousNode!.PreviousNode);
        Assert.True(unit.Nodes().ToList().IndexOf(comment) < unit.Nodes().ToList().IndexOf(hp));
    }

    [Theory]
    [InlineData("<future arbitrary='yes'><child/></future>")]
    [InlineData("<ext:command xmlns:ext='urn:mod'>opaque</ext:command>")]
    [InlineData("<?keep placement?>")]
    [InlineData("\n\n<!--standalone section-->\n")]
    public void OpaqueOrAmbiguousBlocksAnchorSeparateOrderingRuns(string barrier)
    {
        var unit = Parse("<flag>A</flag><displaynameid>ID</displaynameid>" + barrier + "<tactics>x</tactics><command>C</command>");
        var opaque = unit.Nodes().First(node => node is XElement element && element.Name.NamespaceName.Length > 0 ||
            node is XElement unknown && unknown.Name == "future" || node is XProcessingInstruction || node is XComment);
        var shape = opaque.ToString(SaveOptions.DisableFormatting);
        ProtoUnitXmlOrderPolicy.OrderKnownFieldsForEditor(unit);
        var nodes = unit.Nodes().ToList();
        Assert.True(nodes.IndexOf(unit.Element("displaynameid")!) < nodes.IndexOf(unit.Element("flag")!));
        Assert.True(nodes.IndexOf(unit.Element("flag")!) < nodes.IndexOf(opaque));
        Assert.True(nodes.IndexOf(opaque) < nodes.IndexOf(unit.Element("command")!));
        Assert.True(nodes.IndexOf(unit.Element("command")!) < nodes.IndexOf(unit.Element("tactics")!));
        Assert.Equal(shape, opaque.ToString(SaveOptions.DisableFormatting));
    }

    [Theory]
    [InlineData("text")]
    [InlineData("<![CDATA[ ]]>")]
    [InlineData("<![CDATA[meaningful]]>")]
    public void MixedRootContentIsNotRearranged(string content)
    {
        var unit = Parse("<flag>F</flag>" + content + "<displaynameid>ID</displaynameid>");
        var before = Shape(unit);
        ProtoUnitXmlOrderPolicy.OrderKnownFieldsForEditor(unit);
        Assert.Equal(before, Shape(unit));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void RootOrInheritedPreservedSpaceRemainsByteForByteInMemory(bool inherited)
    {
        var unit = Parse("  <flag>F</flag>  <displaynameid>ID</displaynameid>  ");
        var root = inherited ? new XElement("root", new XAttribute(XNamespace.Xml + "space", "preserve"), unit) : unit;
        if (!inherited) unit.SetAttributeValue(XNamespace.Xml + "space", "preserve");
        var before = Shape(root);
        ProtoUnitXmlOrderPolicy.OrderKnownFieldsForEditor(unit);
        Assert.Equal(before, Shape(root));
    }

    [Fact]
    public void ExplicitSpaceDefaultResumesOrderingWithoutTouchingPreservedChildContents()
    {
        var unit = Parse("<protoaction xml:space='preserve'>  <name>A</name>  </protoaction><displaynameid>ID</displaynameid>");
        unit.SetAttributeValue(XNamespace.Xml + "space", "default");
        var parent = new XElement("root", new XAttribute(XNamespace.Xml + "space", "preserve"), unit);
        var action = Shape(unit.Element("protoaction")!);
        ProtoUnitXmlOrderPolicy.OrderKnownFieldsForEditor(unit);
        Assert.Equal(new[] { "displaynameid", "protoaction" }, Tags(unit));
        Assert.Equal(action, Shape(unit.Element("protoaction")!));
        Assert.Same(unit, parent.Element("unit"));
    }

    [Fact]
    public void SecondPassDoesNotMutateNodesOrRaiseChangeEvents()
    {
        var unit = Parse("<flag>F</flag><icon>i</icon><displaynameid>ID</displaynameid><unittype>Unit</unittype>");
        ProtoUnitXmlOrderPolicy.OrderKnownFieldsForEditor(unit);
        var nodes = unit.Nodes().ToArray(); var before = Shape(unit);
        unit.Changing += (_, _) => throw new InvalidOperationException("Already ordered XML must not change.");
        ProtoUnitXmlOrderPolicy.OrderKnownFieldsForEditor(unit);
        Assert.Equal(before, Shape(unit)); Assert.Equal(nodes, unit.Nodes().ToArray());
    }

    [Fact]
    public void RegistryCoversTheActualFormFieldsAndSharesItsOptionalModuleOrder()
    {
        foreach (var field in ProtoConstants.SimpleFields) Assert.Contains(field.Tag, ProtoUnitFieldOrder.Ranks.Keys);
        foreach (var fieldName in new[] { "OtherSpecificSimpleNumberTags", "OtherSpecificSimpleTextTags", "OtherSpecificSimpleSuggestionTags" })
            foreach (var tag in (IEnumerable<string>)typeof(ProtoEditorWindow).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!)
                Assert.Contains(tag, ProtoUnitFieldOrder.Ranks.Keys, StringComparer.OrdinalIgnoreCase);
        var formOrder = (string[])typeof(ProtoEditorWindow).GetField("OtherSpecificAttributeChoiceKeys", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        Assert.Equal(ProtoUnitFieldOrder.OptionalGroups.Select(group => group.Key), formOrder);
        Assert.Equal(formOrder.Length, formOrder.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Theory]
    [InlineData("\r\n")] [InlineData("\n")]
    public void SharedWriterRetainsEncodingAndProducesStableRepeatedSaves(string newline)
    {
        using var workspace = new TestWorkspace("unit-order-foundation");
        var path = workspace.GetPath("proto_mods.xml");
        File.WriteAllText(path, "<protomods>" + newline + "  <unit name='U'><flag>F</flag><displaynameid>ID</displaynameid></unit>" + newline + "</protomods>", new UnicodeEncoding(false, true));
        var document = XDocument.Load(path, LoadOptions.PreserveWhitespace);
        ProtoUnitXmlOrderPolicy.OrderKnownFieldsForEditor(document.Root!.Element("unit")!);
        ProtoXmlHandler.SaveProtoXml(document, path);
        var bytes = File.ReadAllBytes(path);
        Assert.Equal(new byte[] { 0xff, 0xfe }, bytes.Take(2));
        var text = Encoding.Unicode.GetString(bytes.AsSpan(2));
        Assert.Contains(newline + "    <displaynameid>", text);
        if (newline == "\n") Assert.DoesNotContain("\r", text, StringComparison.Ordinal);
        ProtoXmlHandler.SaveProtoXml(XDocument.Load(path, LoadOptions.PreserveWhitespace), path);
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }
}
