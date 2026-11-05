using System.Collections;
using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed partial class UnitXmlEditingTests
{
    public static IEnumerable<object[]> ResourceFamilies =>
    [
        ["carrycapacity", "resourcetype"],
        ["initialresource", "resourcetype"],
        ["killreward", "resourcetype"],
        ["ResourceReturn", "resourceType"],
        ["ResourceReturnRate", "resourceType"],
        ["respawnrates", ""]
    ];

    private static string ResourceDocument(string fragment = "")
    {
        var root = XElement.Parse(RoleDocument());
        var unit = root.Elements("unit").First();
        unit.Elements().Where(element => new[] { "carrycapacity", "initialresource", "killreward", "ResourceReturn", "ResourceReturnRate", "respawntraindata" }.Contains(element.Name.LocalName)).Remove();
        unit.Add(XElement.Parse("<entries>" + fragment + "</entries>").Elements());
        return root.ToString(SaveOptions.DisableFormatting);
    }

    private static string ResourceFragment(string tag, string attribute)
    {
        if (tag == "respawnrates") return "<respawntraindata><respawntypes><unittype>CheckpointType</unittype></respawntypes><respawnrates future='wrapper'><food future='row'>0<extra keep='yes'/></food><mana>4</mana></respawnrates><respawnlimit>2</respawnlimit></respawntraindata>";
        var value = tag == "ResourceReturnRate" ? "0.5" : "0";
        return $"<{tag} {attribute}='Food' future='row'>{value}<extra keep='yes'/></{tag}><{tag} {attribute}='Mana'>4</{tag}>";
    }

    private static ResourceRowsEditor ResourceEditor(BaselineHost host, string tag)
        => Assignment<ResourceRowsEditor>(Field<IDictionary>(host, "_resourceRowEditors")[tag]!, "Editor");
    private static void RemoveResource(ResourceRowsEditor.Row row)
        => Assert.Single(row.Panel.Children.OfType<Button>()).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static IEnumerable<XElement> SavedResources(XElement unit, string tag)
        => tag == "respawnrates" ? unit.Element("respawntraindata")!.Element(tag)?.Elements() ?? [] : unit.Elements(tag);

    [Theory]
    [MemberData(nameof(ResourceFamilies))]
    public void ResourceAddFlowsAfterLastEntryAndWrapsOnlyWhenNeeded(string tag, string attribute)
        => WithHost(async (_, host) =>
        {
            host.Width = 1900; await RoleSettle(host);
            var editor = ResourceEditor(host, tag);
            editor.AddButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await RoleSettle(host);
            var last = editor.Rows[^1].Panel;
            var flow = Assert.IsType<WrapPanel>(last.Parent);
            Assert.Same(flow, editor.AddButton.Parent); Assert.Same(editor.AddButton, flow.Children[^1]);
            var rowPoint = last.TranslatePoint(default, host)!.Value;
            var addPoint = editor.AddButton.TranslatePoint(default, host)!.Value;
            Assert.InRange(Math.Abs(rowPoint.Y - addPoint.Y), 0, .5);
            Assert.InRange(addPoint.X - rowPoint.X - last.Bounds.Width, 11.9, 12.1);
            flow.Width = last.Bounds.Width + EditorFormLayout.GroupGap + 5; await RoleSettle(host);
            Assert.True(editor.AddButton.Bounds.Y > last.Bounds.Y);
            Assert.True(editor.AddButton.Bounds.Right <= flow.Bounds.Width + .5);
            RemoveResource(editor.Rows[^1]); await RoleSettle(host);
            Assert.Same(editor.AddButton, flow.Children[^1]);
        }, ResourceDocument(ResourceFragment(tag, attribute)), RoleFixture);

    [Theory]
    [MemberData(nameof(ResourceFamilies))]
    public void ResourceRowsShareCatalogLifecycleAndPreserveZeroUnknownDataAndXmlShape(string tag, string attribute)
        => WithHost(async (fixture, host) =>
        {
            host.Width = 1600; await RoleSettle(host);
            var editor = ResourceEditor(host, tag);
            var row = Assert.Single(editor.Rows);
            Assert.False(row.Type.IsEditable); AssertRole(row.Type, EditorFieldWidthRole.ShortEnum);
            AssertRole(row.Value, EditorFieldWidthRole.Numeric);
            var remove = Assert.Single(row.Panel.Children.OfType<Button>());
            var point = row.Value.TranslatePoint(default, host)!.Value;
            var cross = remove.TranslatePoint(default, host)!.Value;
            Assert.InRange(cross.X - point.X - row.Value.Bounds.Width, 3.9, 4.1);
            Assert.InRange(Math.Abs(cross.Y + remove.Bounds.Height / 2 - point.Y - row.Value.Bounds.Height / 2), 0, .5);
            Assert.Equal("Food", row.Resource); Assert.False(Field<bool>(host, "_isDirty"));
            row.Type.SelectedItem = "Wood"; await RoleSettle(host);
            row.Value.Text = "7"; await RoleSettle(host);
            for (var i = 0; i < 3; i++)
            { editor.AddButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await RoleSettle(host); }
            Assert.Equal(4, editor.Rows.Count); Assert.False(editor.AddButton.IsVisible);
            foreach (var item in editor.Rows) Assert.Equal(new[] { item.Resource }, item.Type.ItemsSource!.Cast<string>());
            RemoveResource(editor.Rows.Single(item => item.Resource == "Food")); await RoleSettle(host);
            Assert.True(editor.AddButton.IsVisible); Assert.Contains("Food", row.Type.ItemsSource!.Cast<string>());
            editor.AddButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await RoleSettle(host);
            Assert.Equal(4, editor.Rows.Count); Assert.False(editor.AddButton.IsVisible);
            RemoveResource(editor.Rows.Single(item => item.Resource == "Food")); await RoleSettle(host);
            Assert.Empty(await Save(host));
            var unit = XDocument.Load(fixture.ProtoPath).Root!.Element("unit")!;
            var entries = SavedResources(unit, tag).ToArray();
            string Key(XElement element) => attribute.Length == 0 ? element.Name.LocalName : (string?)element.Attribute(attribute) ?? "";
            var wood = entries.Single(element => Key(element).Equals("Wood", StringComparison.OrdinalIgnoreCase));
            Assert.Equal("7", string.Concat(wood.Nodes().OfType<XText>().Select(text => text.Value)));
            Assert.Equal("row", (string?)wood.Attribute("future")); Assert.Single(wood.Elements("extra"));
            Assert.Contains(entries, element => Key(element).Equals("Mana", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(entries, element => Key(element).Equals("Food", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(tag == "ResourceReturnRate" ? "1" : "0", entries.Single(element => Key(element).Equals("Gold", StringComparison.OrdinalIgnoreCase)).Value);
            Assert.Equal("yes", (string?)unit.Element("future")!.Attribute("keep"));
            if (tag == "respawnrates")
            {
                Assert.Equal("wrapper", (string?)unit.Element("respawntraindata")!.Element("respawnrates")!.Attribute("future"));
                Assert.Equal("2", unit.Element("respawntraindata")!.Element("respawnlimit")!.Value);
            }
            host.Call("BuildEditorPanel", "A", true); await RoleSettle(host);
            editor = ResourceEditor(host, tag); Assert.Equal(3, editor.Rows.Count); Assert.False(Field<bool>(host, "_isDirty"));
            while (editor.Rows.Count > 0) { RemoveResource(editor.Rows[0]); await RoleSettle(host); }
            Assert.True(editor.AddButton.IsVisible); Assert.Empty(await Save(host));
            entries = SavedResources(XDocument.Load(fixture.ProtoPath).Root!.Element("unit")!, tag).ToArray();
            Assert.Single(entries); Assert.Equal("mana", Key(entries[0]).ToLowerInvariant());
        }, ResourceDocument(ResourceFragment(tag, attribute)), RoleFixture);

    [Theory]
    [MemberData(nameof(ResourceFamilies))]
    public void ResourceRowsKeepOriginalRecordsReadOnly(string tag, string attribute)
        => WithHost(async (fixture, host) =>
        {
            var before = File.ReadAllBytes(fixture.ProtoPath);
            host.Set("_barXmlRoot", XElement.Parse(ResourceDocument(ResourceFragment(tag, attribute))));
            host.Set("_modXmlRoot", XElement.Parse("<protomods><unit name='B'/></protomods>"));
            host.Call("BuildEditorPanel", "A", true); await RoleSettle(host);
            var editor = ResourceEditor(host, tag);
            var row = Assert.Single(editor.Rows); Assert.False(row.Type.IsEnabled); Assert.False(row.Value.IsEnabled);
            Assert.Empty(row.Panel.Children.OfType<Button>()); Assert.False(editor.AddButton.IsVisible);
            Assert.False(Field<bool>(host, "_isDirty")); Assert.Equal(before, File.ReadAllBytes(fixture.ProtoPath));
        }, ResourceDocument(), RoleFixture);

    [Theory]
    [MemberData(nameof(ResourceFamilies))]
    public void ResourceRowsPreserveMetadataAddedToTheCurrentDocumentAfterTheFormLoaded(string tag, string attribute)
        => WithHost(async (fixture, host) =>
        {
            await RoleSettle(host);
            var unit = Field<XElement>(host, "_modXmlRoot").Elements("unit").First();
            var entries = SavedResources(unit, tag).ToArray();
            entries[0].SetAttributeValue("late", "preserve");
            entries[0].Add(new XElement("latechild", new XAttribute("keep", "yes")));
            ResourceEditor(host, tag).Rows[0].Value.Text = "3"; await RoleSettle(host);
            Assert.Empty(await Save(host));
            var saved = SavedResources(XDocument.Load(fixture.ProtoPath).Root!.Element("unit")!, tag).First();
            Assert.Equal("preserve", (string?)saved.Attribute("late"));
            Assert.Single(saved.Elements("extra")); Assert.Single(saved.Elements("latechild"));
        }, ResourceDocument(ResourceFragment(tag, attribute)), RoleFixture);

    [Theory]
    [InlineData("carrycapacity", "Carry Capacity")]
    [InlineData("initialresource", "Initial Resource")]
    [InlineData("killreward", "Kill Reward")]
    [InlineData("ResourceReturn", "Resource Return")]
    [InlineData("ResourceReturnRate", "Resource Return Rate")]
    public void NewResourceSectionsOfferOnlyAddResourceUntilAResourceIsAdded(string tag, string label)
        => WithHost(async (fixture, host) =>
        {
            await RoleSettle(host); await AddUnitOptional(host, label);
            var editor = ResourceEditor(host, tag); Assert.Empty(editor.Rows); Assert.True(editor.AddButton.IsVisible);
            editor.AddButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await RoleSettle(host);
            var row = Assert.Single(editor.Rows); Assert.Equal("Food", row.Resource);
            Assert.Equal(tag == "ResourceReturnRate" ? "1" : "0", row.Value.Text);
            Assert.Empty(await Save(host));
            Assert.Single(XDocument.Load(fixture.ProtoPath).Root!.Element("unit")!.Elements(tag));
        }, ResourceDocument(), RoleFixture);

    [Fact]
    public void ResourceReturnRateKeepsPositiveFloatValidationForDynamicRows()
        => WithHost(async (fixture, host) =>
        {
            await RoleSettle(host); var before = File.ReadAllBytes(fixture.ProtoPath);
            var row = ResourceEditor(host, "ResourceReturnRate").Rows[0];
            row.Value.Text = "0"; await RoleSettle(host);
            Assert.Contains(await Save(host), error => error.Contains("Resource Return Rate", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(before, File.ReadAllBytes(fixture.ProtoPath));
            row.Value.Text = "1.25"; await RoleSettle(host); Assert.Empty(await Save(host));
            Assert.Equal("1.25", XDocument.Load(fixture.ProtoPath).Root!.Element("unit")!.Element("ResourceReturnRate")!.Value);
        }, ResourceDocument("<ResourceReturnRate resourceType='Food'>2</ResourceReturnRate>"), RoleFixture);

    [Fact]
    public void ResourceRowsDiscardRestoresValuesAndMetadataAndRejectsDetachedMutations()
        => WithHost(async (fixture, host) =>
        {
            await RoleSettle(host);
            var before = File.ReadAllBytes(fixture.ProtoPath);
            var original = XDocument.Load(fixture.ProtoPath).Root!.Element("unit")!;
            var kind = Enum.Parse(typeof(AoMDivineDataEditor.Windows.ProtoEditorWindow).GetNestedType("EditorEntityKind", System.Reflection.BindingFlags.NonPublic)!, "Units");
            var tab = host.Call("CreateDocumentTab", true, kind, true, "A")!;
            tab.GetType().GetProperty("SavedElement")!.SetValue(tab, new XElement(original));
            var previous = ResourceEditor(host, "carrycapacity");
            previous.Rows[0].Type.SelectedItem = "Gold"; await RoleSettle(host);
            previous.AddButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await RoleSettle(host);
            Assert.True(Field<bool>(host, "_isDirty"));
            host.Call("DiscardUnitDocumentChanges", tab); host.Call("BuildEditorPanel", "A", true); await RoleSettle(host);
            var current = ResourceEditor(host, "carrycapacity"); var row = Assert.Single(current.Rows);
            Assert.Equal("Food", row.Resource); Assert.Equal("3", row.Value.Text);
            Assert.Equal("1.5", ((TextBox)Field<Dictionary<string, Control>>(host, "_fieldControls")["carrycapacity:dropoffmultiplier:Food"]).Text);
            previous.AddButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await RoleSettle(host);
            Assert.Single(current.Rows); Assert.False(Field<bool>(host, "_isDirty"));
            Assert.Equal(before, File.ReadAllBytes(fixture.ProtoPath));
        }, ResourceDocument("<carrycapacity resourcetype='Food' dropoffmultiplier='1.5'>3</carrycapacity>"), RoleFixture);

    [Fact]
    public void CarryCapacityKeepsZeroAndMovesDropoffMetadataWithItsResourceWithoutAffectingInitialResource()
        => WithHost(async (fixture, host) =>
        {
            await RoleSettle(host);
            var fields = Field<Dictionary<string, Control>>(host, "_fieldControls");
            ((TextBox)fields["maxhitpoints"]).Text = "11"; await RoleSettle(host); Assert.Empty(await Save(host));
            var unit = XDocument.Load(fixture.ProtoPath).Root!.Element("unit")!;
            Assert.Equal("0", unit.Element("carrycapacity")!.Value);
            Assert.Equal("1.5", (string?)unit.Element("carrycapacity")!.Attribute("dropoffmultiplier"));
            host.Call("BuildEditorPanel", "A", true); await RoleSettle(host);
            ResourceEditor(host, "carrycapacity").Rows[0].Type.SelectedItem = "Wood"; await RoleSettle(host);
            var initialModule = StatsModule(host, "initialresource");
            Assert.Single(Assert.IsType<StackPanel>(initialModule).Children.OfType<Grid>().First().Children.OfType<Button>())
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await RoleSettle(host); Assert.Empty(await Save(host));
            unit = XDocument.Load(fixture.ProtoPath).Root!.Element("unit")!;
            Assert.Null(unit.Element("initialresource"));
            var capacity = Assert.Single(unit.Elements("carrycapacity"));
            Assert.Equal("Wood", (string?)capacity.Attribute("resourcetype"));
            Assert.Equal("1.5", (string?)capacity.Attribute("dropoffmultiplier")); Assert.Equal("keep", (string?)capacity.Attribute("future"));
        }, ResourceDocument("<carrycapacity resourcetype='Food' dropOffMultiplier='1.5' future='keep'>0</carrycapacity><initialresource resourcetype='Wood'>3</initialresource>"), RoleFixture);

    [Theory]
    [InlineData("ResourceReturn", "resourcereturn", "Resource Return")]
    [InlineData("ResourceReturnRate", "resourcereturnrate", "Resource Return Rate")]
    public void ResourceReturnFlagsSurviveRemovingTheLastRowAndReopening(string tag, string key, string label)
        => WithHost(async (fixture, host) =>
        {
            await RoleSettle(host); RemoveResource(ResourceEditor(host, tag).Rows[0]); await RoleSettle(host);
            Assert.Empty(await Save(host));
            host.Call("BuildEditorPanel", "A", true); await RoleSettle(host);
            ((TextBox)Field<Dictionary<string, Control>>(host, "_fieldControls")["maxhitpoints"]).Text = "11";
            await RoleSettle(host); Assert.Empty(await Save(host));
            var flags = XDocument.Load(fixture.ProtoPath).Root!.Element("unit")!.Elements("flag").Select(element => element.Value).ToArray();
            Assert.Contains("ReturnResourcesOnConstruction", flags); Assert.Contains("DoNotApplyResourceReturnIfDeleted", flags);
            if (tag == "ResourceReturnRate") Assert.Contains("ResourceReturnRateTotalCost", flags);
            await AddUnitOptional(host, label);
            var module = Assert.IsType<StackPanel>(StatsModule(host, key));
            Assert.Single(module.Children.OfType<Grid>().First().Children.OfType<Button>())
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await RoleSettle(host); Assert.Empty(await Save(host));
            flags = XDocument.Load(fixture.ProtoPath).Root!.Element("unit")!.Elements("flag").Select(element => element.Value).ToArray();
            Assert.DoesNotContain("ReturnResourcesOnConstruction", flags); Assert.DoesNotContain("DoNotApplyResourceReturnIfDeleted", flags);
        }, ResourceDocument($"<{tag} resourceType='Food'>2</{tag}><flag>ReturnResourcesOnConstruction</flag><flag>DoNotApplyResourceReturnIfDeleted</flag>" + (tag == "ResourceReturnRate" ? "<flag>ResourceReturnRateTotalCost</flag>" : "")), RoleFixture);
}
