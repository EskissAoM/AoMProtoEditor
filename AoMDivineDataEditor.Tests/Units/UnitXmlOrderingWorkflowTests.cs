using System.Collections;
using System.Reflection;
using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.GameData;
using AoMDivineDataEditor.Windows;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed partial class UnitXmlEditingTests
{
    private static string OrderedShape(XElement element) => element.ToString(SaveOptions.DisableFormatting);
    private static string SourceRecordShape(XElement element)
    {
        // Existing writers deliberately expand empty armor/transform markers.
        // Compare every value, attribute, child and comment in order, allowing
        // only the semantically equivalent <x/> versus <x></x> spelling.
        var copy = new XElement(element);
        foreach (var child in copy.DescendantsAndSelf().Where(e => !e.Nodes().Any()).ToArray()) child.Add(new XText(""));
        return OrderedShape(copy);
    }
    private static void AssertUnitOrder(XElement unit, params string[] expected)
    {
        var wanted = expected.ToHashSet(StringComparer.Ordinal);
        Assert.Equal(expected, unit.Elements().Select(e => e.Name.LocalName).Where(wanted.Contains).Distinct());
    }

    [Theory]
    [InlineData("Unit")] [InlineData("Building")]
    public void NewlyCreatedUnitsUseTheSameOrderThroughTheRealCreationDialogs(string kind)
        => WithHost(async (fixture, host) =>
        {
            await RoleSettle(host);
            var original = XDocument.Load(fixture.ProtoPath).Root!.Elements("unit").First();
            var creating = (Task)host.Call("AddUnitAsync", false)!;
            var input = Assert.Single(host.OwnedWindows.OfType<InputPromptWindow>());
            typeof(InputPromptWindow).GetProperty(nameof(InputPromptWindow.InputText))!.SetValue(input, "CreatedOrderUnit"); input.Close();
            for (var i = 0; i < 300 && !host.OwnedWindows.OfType<NewUnitTypeWindow>().Any(); i++) await Task.Delay(10);
            var type = Assert.Single(host.OwnedWindows.OfType<NewUnitTypeWindow>());
            var typeBox = type.FindControl<ComboBox>("_typeComboBox")!;
            typeBox.SelectedItem = typeBox.Items.OfType<ComboBoxItem>().Single(item => Equals(item.Content, kind));
            typeof(NewUnitTypeWindow).GetMethod("OK_Click", Private)!.Invoke(type, new object?[] { null, new RoutedEventArgs() });
            await creating.WaitAsync(TimeSpan.FromSeconds(10)); await RoleSettle(host);
            var fields = Field<Dictionary<string, Control>>(host, "_fieldControls");
            Assert.IsType<TextBox>(fields["maxhitpoints"]).Text = "40";
            Assert.IsType<TextBox>(fields["initialhitpoints"]).Text = "40";
            Assert.IsType<TextBox>(fields["turnrate"]).Text = "1";
            var flag = Assert.Single(Field<StackPanel>(host, "_statsEditorPanel").GetLogicalDescendants().OfType<AutoCompleteBox>(),
                picker => picker.ItemsSource?.Cast<object>().Contains("CollidesWithProjectiles") == true && picker.Width == 50);
            flag.SelectedItem = "CollidesWithProjectiles"; await RoleSettle(host);
            Assert.Empty(await Save(host));
            var document = XDocument.Load(fixture.ProtoPath);
            var created = ProtoXmlHandler.GetUnitElement(document.Root!, "CreatedOrderUnit")!;
            AssertUnitOrder(created, "displaynameid", "editornameid", "rollovertextid", "shortrollovertextid",
                "turnrate", "maxhitpoints", "initialhitpoints", "unittype", "flag");
            Assert.Equal(kind, created.Element("unittype")!.Value);
            Assert.True(XNode.DeepEquals(original, document.Root!.Elements("unit").First()));
            var once = File.ReadAllBytes(fixture.ProtoPath);
            host.Call("BuildEditorPanel", "A", true); host.Call("BuildEditorPanel", "CreatedOrderUnit", true); await RoleSettle(host);
            Assert.Empty(await Save(host)); Assert.Equal(once, File.ReadAllBytes(fixture.ProtoPath));
        });

    [Fact]
    public void StructuredUnitOrderSavePreviewReopenAndNoOpSaveUseTheApprovedOrder()
        => WithHost(async (fixture, host) =>
        {
            await RoleSettle(host);
            var before = File.ReadAllBytes(fixture.ProtoPath);
            Assert.Empty(await Save(host)); Assert.Equal(before, File.ReadAllBytes(fixture.ProtoPath));
            await RoleSettle(host);
            var original = XDocument.Load(fixture.ProtoPath, LoadOptions.PreserveWhitespace);
            var sibling = OrderedShape(original.Root!.Elements("unit").Last());
            var future = OrderedShape(original.Descendants("future").First());
            var fields = Field<Dictionary<string, Control>>(host, "_fieldControls");
            Assert.IsType<TextBox>(fields["maxhitpoints"]).Text = "17";
            Assert.IsType<TextBox>(fields["turnrate"]).Text = "1";
            await RoleSettle(host);
            Assert.Equal("1", Assert.IsType<TextBox>(fields["turnrate"]).Text);
            Assert.True(Field<bool>(host, "_isDirty"));
            host.Call("UpdateXmlPreview", true);
            string[] order = ["displaynameid", "icon", "animfile", "soundsetfile", "obstructionradiusx", "obstructionradiusz",
                "turnrate", "maxhitpoints", "cost", "armor", "unittype", "flag"];
            AssertUnitOrder(XElement.Parse(Raw(host).Text), order);
            Assert.Empty(await Save(host));
            var saved = XDocument.Load(fixture.ProtoPath, LoadOptions.PreserveWhitespace);
            var unit = saved.Root!.Elements("unit").First();
            AssertUnitOrder(unit, order);
            Assert.Equal("17", unit.Element("maxhitpoints")!.Value);
            Assert.Equal("1", unit.Element("turnrate")!.Value);
            Assert.Equal(new[] { "default.png", "greek.png" }, unit.Elements("icon").Select(e => e.Value));
            Assert.Equal("HP note", unit.Nodes().OfType<XComment>().Single().Value);
            Assert.Same(unit.Nodes().OfType<XComment>().Single(), unit.Element("maxhitpoints")!.PreviousNode!.PreviousNode);
            Assert.Equal(future, OrderedShape(unit.Element("future")!));
            Assert.Equal(sibling, OrderedShape(saved.Root.Elements("unit").Last()));
            var once = File.ReadAllBytes(fixture.ProtoPath);
            host.Call("BuildEditorPanel", "B", true); host.Call("BuildEditorPanel", "A", true); await RoleSettle(host);
            Assert.False(Field<bool>(host, "_isDirty"));
            Assert.Empty(await Save(host)); Assert.Equal(once, File.ReadAllBytes(fixture.ProtoPath));
        }, """
        <protomods>
          <unit name="A"><flag>CollidesWithProjectiles</flag><unittype>Unit</unittype><cost resourcetype="Wood">5</cost><armor type="Hack" value=".1"/><!--HP note--><maxhitpoints>10</maxhitpoints><soundsetfile>custom_sound.xml</soundsetfile><animfile>custom_model.xml</animfile><icon>default.png</icon><icon culture="Greek">greek.png</icon><obstructionradiusx>2</obstructionradiusx><obstructionradiusz>3</obstructionradiusz><displaynameid>STR_UNIT_A_NAME</displaynameid><future xml:space="preserve">  <child/>  </future></unit>
          <unit name="B"><maxhitpoints>20</maxhitpoints></unit>
        </protomods>
        """);

    [Fact]
    public void RawUnitXmlKeepsItsManualOrderAndOpaqueContentAcrossSaveAndReopen()
        => WithHost(async (fixture, host) =>
        {
            await RoleSettle(host); await Unlock(host);
            var manual = XElement.Parse("<unit name='A'><flag>CollidesWithProjectiles</flag><maxhitpoints>21</maxhitpoints>" +
                "<unittype>Unit</unittype><future xml:space='preserve'>  <child/>  </future>" +
                "<ext:command xmlns:ext='urn:mod'><![CDATA[opaque]]></ext:command></unit>", LoadOptions.PreserveWhitespace);
            Raw(host).Text = manual.ToString(SaveOptions.DisableFormatting); await RoleSettle(host);
            Assert.Empty(await Save(host));
            var saved = XDocument.Load(fixture.ProtoPath).Root!.Elements("unit").First();
            Assert.Equal(manual.Elements().Select(e => e.Name), saved.Elements().Select(e => e.Name));
            Assert.True(XNode.DeepEquals(manual, saved));
            var once = File.ReadAllBytes(fixture.ProtoPath);
            host.Call("BuildEditorPanel", "B", true); host.Call("BuildEditorPanel", "A", true); await RoleSettle(host);
            Assert.Empty(await Save(host)); Assert.Equal(once, File.ReadAllBytes(fixture.ProtoPath));
        });

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void StructuredUnitSaveHonorsRootAndInheritedPreservedSpace(bool inherited)
        => WithHost(async (fixture, host) =>
        {
            await RoleSettle(host);
            var before = XDocument.Load(fixture.ProtoPath, LoadOptions.PreserveWhitespace);
            var unitBefore = before.Root!.Elements("unit").First();
            Assert.IsType<TextBox>(Field<Dictionary<string, Control>>(host, "_fieldControls")["maxhitpoints"]).Text = "18";
            await RoleSettle(host); host.Call("UpdateXmlPreview", true);
            var preview = XElement.Parse(Raw(host).Text, LoadOptions.PreserveWhitespace);
            Assert.Equal(unitBefore.Elements().Select(e => e.Name), preview.Elements().Select(e => e.Name));
            Assert.Equal(OrderedShape(unitBefore.Element("future")!), OrderedShape(preview.Element("future")!));
            Assert.Empty(await Save(host));
            var saved = XDocument.Load(fixture.ProtoPath, LoadOptions.PreserveWhitespace).Root!.Elements("unit").First();
            Assert.Equal(unitBefore.Elements().Select(e => e.Name), saved.Elements().Select(e => e.Name));
            Assert.Equal("18", saved.Element("maxhitpoints")!.Value);
            Assert.Equal(OrderedShape(unitBefore.Element("future")!), OrderedShape(saved.Element("future")!));
        }, "<protomods" + (inherited ? " xml:space='preserve'" : "") + "><unit name='A'" +
            (inherited ? "" : " xml:space='preserve'") + "><flag>CollidesWithProjectiles</flag>  <maxhitpoints>10</maxhitpoints>  " +
            "<initialhitpoints>10</initialhitpoints><unittype>Unit</unittype><future>  <child/>  </future></unit><unit name='B'><maxhitpoints>20</maxhitpoints></unit></protomods>");

    [Theory]
    [InlineData(true, false)] [InlineData(false, false)] [InlineData(false, true)]
    public void MilitaryCampDuplicationOrdersOnlyTheCopyOrExplicitlyDirtySourceAndKeepsTransformBundles(bool originalSource, bool dirtySource)
    {
        const string sourceName = "MilitaryCamp";
        const string copyName = "MilitaryCampCopy";
        const string sourceXml = "<unit name='MilitaryCamp'><displaynameid>STR_SOURCE</displaynameid><rollovertextid>STR_SOURCE_LR</rollovertextid>" +
            "<shortrollovertextid>STR_SOURCE_SR</shortrollovertextid><icon>camp.png</icon><obstructionradiusx>4</obstructionradiusx>" +
            "<obstructionradiusz>4</obstructionradiusz><initialhitpoints>1200</initialhitpoints><maxhitpoints>1200</maxhitpoints>" +
            "<buildpoints>30</buildpoints><los>16</los><cost resourcetype='Wood'>100</cost><armor type='Hack' value='0.1'/>" +
            "<unittype>Building</unittype><hotkeycontext>Build</hotkeycontext><train row='0' column='1'>MilitaryTower</train>" +
            "<tech row='0' column='2'>MilitaryCampToTower</tech><flag>CollidesWithProjectiles</flag>" +
            "<command row='0' column='3'>MilitaryCampToTower</command></unit>";
        WithHost(async (fixture, host) =>
        {
            host.Set("_barData", new ProtoBarData { UnitNames = originalSource ? [sourceName, "MilitaryTower"] : ["MilitaryTower"] });
            host.Set("_barXmlRoot", XElement.Parse("<proto>" + (originalSource ? sourceXml : "") + "<unit name='MilitaryTower'/></proto>"));
            host.Call("BuildEditorPanel", sourceName, true); await RoleSettle(host);
            host.Set("_suppressEditorChangeTracking", false);
            var sourceTree = originalSource ? Field<XElement>(host, "_barXmlRoot") : Field<XElement>(host, "_modXmlRoot");
            var sourceBefore = OrderedShape(ProtoXmlHandler.GetUnitElement(sourceTree, sourceName)!);
            var protoBefore = XDocument.Load(fixture.ProtoPath);
            var paths = new[] { "proto_unit_command_mods.xml", "unit_transform_mods.xml", "techtree_mods.xml" }
                .Select(name => Path.Combine(Path.GetDirectoryName(fixture.ProtoPath)!, name)).ToArray();
            var sourceRecords = paths.Select(path => XDocument.Load(path).Root!.Elements().First()).Select(e => new XElement(e)).ToArray();
            if (dirtySource)
            {
                Assert.IsType<TextBox>(Field<Dictionary<string, Control>>(host, "_fieldControls")["maxhitpoints"]).Text = "1400";
                await RoleSettle(host); Assert.Contains(sourceName, Field<HashSet<string>>(host, "_dirtyUnitNames"));
            }
            var duplicating = (Task)host.Call("AddUnitAsync", true)!;
            for (var i = 0; i < 300 && !host.OwnedWindows.OfType<InputPromptWindow>().Any(); i++) await Task.Delay(10);
            var prompt = Assert.Single(host.OwnedWindows.OfType<InputPromptWindow>());
            typeof(InputPromptWindow).GetProperty(nameof(InputPromptWindow.InputText))!.SetValue(prompt, copyName); prompt.Close();
            await duplicating.WaitAsync(TimeSpan.FromSeconds(10)); await RoleSettle(host);
            if (!dirtySource)
            {
                Assert.Equal(sourceBefore, OrderedShape(ProtoXmlHandler.GetUnitElement(sourceTree, sourceName)!));
                Assert.DoesNotContain(sourceName, Field<HashSet<string>>(host, "_dirtyUnitNames"));
            }
            Assert.Empty(await Save(host));
            var saved = XDocument.Load(fixture.ProtoPath);
            var copy = ProtoXmlHandler.GetUnitElement(saved.Root!, copyName)!;
            Assert.Equal(dirtySource ? "1400" : "1200", copy.Element("maxhitpoints")!.Value);
            AssertUnitOrder(copy, "displaynameid", "editornameid", "rollovertextid", "shortrollovertextid", "icon",
                "obstructionradiusx", "obstructionradiusz", "maxhitpoints", "initialhitpoints", "buildpoints", "los", "hotkeycontext",
                "cost", "armor", "unittype", "flag", "train", "tech", "command");
            if (originalSource) Assert.Null(ProtoXmlHandler.GetUnitElement(saved.Root!, sourceName));
            else if (!dirtySource) Assert.Equal(SourceRecordShape(ProtoXmlHandler.GetUnitElement(protoBefore.Root!, sourceName)!), SourceRecordShape(ProtoXmlHandler.GetUnitElement(saved.Root!, sourceName)!));
            else Assert.Equal("1400", ProtoXmlHandler.GetUnitElement(saved.Root!, sourceName)!.Element("maxhitpoints")!.Value);
            for (var i = 0; i < paths.Length; i++)
            {
                static string? Identity(XElement e) => (string?)e.Attribute("name") ?? e.Element("name")?.Value ?? e.Element("command")?.Value;
                var sourceRecord = XDocument.Load(paths[i]).Root!.Elements().Single(e => Identity(e) == Identity(sourceRecords[i]));
                Assert.Equal(SourceRecordShape(sourceRecords[i]), SourceRecordShape(sourceRecord));
            }
            var commandName = copy.Element("command")!.Value;
            Assert.NotEqual("MilitaryCampToTower", commandName);
            var command = XDocument.Load(paths[0]).Root!.Elements().Single(e => e.Element("name")?.Value == commandName);
            var associatedTech = command.Element("associatedtech")!.Value;
            Assert.Contains(XDocument.Load(paths[2]).Root!.Elements("tech"), tech => (string?)tech.Attribute("name") == associatedTech);
            var transform = XDocument.Load(paths[1]).Root!.Elements().Single(e => e.Element("command")?.Value == commandName);
            Assert.Equal(copyName, transform.Element("from")!.Value); Assert.Equal("MilitaryTower", transform.Element("to")!.Value);
            var once = File.ReadAllBytes(fixture.ProtoPath);
            host.Call("BuildEditorPanel", "A", true); host.Call("BuildEditorPanel", copyName, true); await RoleSettle(host);
            Assert.False(Field<bool>(host, "_isDirty")); Assert.Empty(await Save(host));
            Assert.Equal(once, File.ReadAllBytes(fixture.ProtoPath));
        }, "<protomods>\n  <unit name='A'><maxhitpoints>10</maxhitpoints></unit>\n  " +
            (originalSource ? "" : sourceXml + "\n  ") + "<unit name='MilitaryTower'><maxhitpoints>100</maxhitpoints></unit>\n</protomods>", fixture =>
        {
            fixture.Workspace.WriteText("TestMod/game/data/gameplay/proto_unit_command_mods.xml", "<protounitcommandmods><protounitcommand><name>MilitaryCampToTower</name><associatedtech>MilitaryCampToTower</associatedtech><transform/><future keep='command'/></protounitcommand></protounitcommandmods>");
            fixture.Workspace.WriteText("TestMod/game/data/gameplay/unit_transform_mods.xml", "<unittransformmods><transform><command>MilitaryCampToTower</command><from>MilitaryCamp</from><to>MilitaryTower</to><future keep='transform'/></transform></unittransformmods>");
            fixture.Workspace.WriteText("TestMod/game/data/gameplay/techtree_mods.xml", "<techtreemods><tech name='MilitaryCampToTower'><cost resourcetype='Wood'>5</cost><researchpoints>0</researchpoints><status>UNOBTAINABLE</status><future keep='tech'/></tech></techtreemods>");
        });
    }
}
