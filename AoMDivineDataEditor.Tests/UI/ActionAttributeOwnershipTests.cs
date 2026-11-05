using System.Collections;
using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed partial class UnitXmlEditingTests
{
    private static string[] FullActionAttributeTags(BaselineHost host, object action)
        => ((IEnumerable)host.Call("GetAvailableProtoActionAttributePickerOptions", action)!).Cast<object>()
            .Select(option => (string)option.GetType().GetField("Item1")!.GetValue(option)!).ToArray();

    [Theory]
    [InlineData("accuracy")]
    [InlineData("trackrating")]
    [InlineData("maxspread")]
    [InlineData("spreadfactor")]
    public void FullActionPickerAdvancedScalarAppearsSavesReopensAndRemovesWithoutChangingAttackMode(string tag)
        => WithHost(async (fixture, host) =>
        {
            PrepareSnippetActions(host); Field<TabControl>(host, "_editorTabs").SelectedIndex = 1; await RoleSettle(host);
            var action = RemovalAction(host);
            Assert.Contains(tag, FullActionAttributeTags(host, action));
            Assert.DoesNotContain("damagebonus", FullActionAttributeTags(host, action));
            Assert.False(Field<bool>(host, "_isDirty"));
            var optional = Assignment<StackPanel>(action, "OptionalFieldsContainer");
            optional.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, "Add Attribute From Full List"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var label = ProtoActionMetadataCatalog.GetFieldDefinition(tag).Label;
            var picker = optional.GetLogicalDescendants().OfType<AutoCompleteBox>()
                .Single(box => box.ItemsSource?.Cast<object>().Contains(label) == true);
            picker.SelectedItem = label; await RoleSettle(host);
            var fields = Assignment<Dictionary<string, Control>>(action, "AdditionalFieldControls");
            var input = Assert.IsType<TextBox>(fields[tag]); Assert.True(input.IsEffectivelyVisible);
            input.Text = "0.75"; await RoleSettle(host);
            Assert.DoesNotContain(tag, FullActionAttributeTags(host, action));
            Assert.Empty(await Save(host));
            var saved = XDocument.Load(fixture.ProtoPath).Descendants("protoaction").Single();
            Assert.Equal("0.75", saved.Element(tag)!.Value); Assert.NotNull(saved.Element("handlogic"));
            Assert.Null(saved.Element("rangedlogic")); Assert.Equal("yes", (string?)saved.Element("future")!.Attribute("keep"));
            PrepareSnippetActions(host); await RoleSettle(host); action = RemovalAction(host);
            fields = Assignment<Dictionary<string, Control>>(action, "AdditionalFieldControls");
            input = Assert.IsType<TextBox>(fields[tag]); Assert.Equal("0.75", input.Text);
            Assert.False(Field<bool>(host, "_isDirty"));
            Assert.Single(Assert.IsType<Grid>(input.Parent).Children.OfType<Button>())
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await RoleSettle(host); Assert.Contains(tag, FullActionAttributeTags(host, action));
            Assert.Empty(await Save(host));
            Assert.Null(XDocument.Load(fixture.ProtoPath).Descendants("protoaction").Single().Element(tag));
        }, RoleDocument("<protoaction><name>HandAttack</name><type>Attack</type><handlogic>1</handlogic><damagebonus type='Unit'>2</damagebonus><future keep='yes'/></protoaction>"), RoleFixture);

    [Theory]
    [InlineData("Attack", "<rangedlogic>1</rangedlogic>", "accuracy")]
    [InlineData("Throw", "", "throwvelocity")]
    [InlineData("Lure", "", "anim")]
    [InlineData("Heal", "", "rate")]
    [InlineData("AreaRestrict", "", "rate")]
    [InlineData("AutoRangedModify", "", "damagebonus")]
    public void FullActionPickerDefersToExistingDedicatedOwners(string type, string extra, string ownedTag)
        => WithHost(async (_, host) =>
        {
            PrepareSnippetActions(host); Field<TabControl>(host, "_editorTabs").SelectedIndex = 1; await RoleSettle(host);
            var action = RemovalAction(host);
            Assert.DoesNotContain(ownedTag, FullActionAttributeTags(host, action));
            if (type == "Attack")
            {
                var body = Assignment<Panel>(action, "Container");
                body.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, "Accuracy"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await RoleSettle(host);
                Assert.True(Assignment<Dictionary<string, Control>>(action, "AdditionalFieldControls")["accuracy"].IsEffectivelyVisible);
            }
            else Assert.False(Field<bool>(host, "_isDirty"));
        }, RoleDocument($"<protoaction><name>OwnershipAction</name><type>{type}</type>{extra}</protoaction>"), RoleFixture);
}
