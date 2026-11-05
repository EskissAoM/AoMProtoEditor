using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed partial class TechnologyEditorWorkflowTests
{
    [Theory]
    [InlineData("<effect type='ModifyProtoUnit' proto='Missing'/>", "@proto", "BaseUnit")]
    [InlineData("<effect type='TransformUnit' fromProtoID='Missing' toProtoID='BaseUnit'/>", "@fromProtoID", "BaseUnit")]
    [InlineData("<effect type='TransformUnit' fromProtoID='BaseUnit' toProtoID='Missing'/>", "@toProtoID", "BaseUnit")]
    [InlineData("<effect type='CreatePower' protoPower='Missing'/>", "@protoPower", "CustomPower")]
    [InlineData("<effect type='CreatePower'/>", "@protoPower", "CustomPower")]
    [InlineData("<effect type='SetOnTechResearchedTech' techType='Missing'>B</effect>", "@techType", "CustomType")]
    [InlineData("<effect type='SetOnTechResearchedTech' techType='CustomType'>Missing</effect>", "", "B")]
    public void StructuredEffectReferenceFailureRevealsExactSelectorAndCorrectionSaves(string effectXml, string field, string correction)
        => WithTechnologyPresentation("<effects>" + effectXml + "</effects>", async (editor, window, fixture) =>
        {
            var before = File.ReadAllBytes(fixture.TechtreePath);
            var saving = editor.SaveAsync();
            await ValidationPopupTest.Dismiss(window, saving, "effect");
            Assert.False(await saving); await SettleTechnologyPresentation(window);
            Assert.Equal(before, File.ReadAllBytes(fixture.TechtreePath));
            var panel = GetPrivateField<StackPanel>(editor, "_effectsPanel");
            var selector = Assert.Single(panel.GetLogicalDescendants().OfType<AutoCompleteBox>().Distinct(), control => control.Classes.Contains("invalid-field"));
            Assert.True(selector.IsKeyboardFocusWithin);
            var source = XDocument.Load(fixture.TechtreePath).Descendants("effect").Single();
            Assert.Same(selector, EditorValidationBindings.Find(editor, new LocatedValidationIssue("typed source", ValidationSourceLocation.From(source, field.Length == 0 ? null : field))));
            var tabs = GetPrivateField<Control>(editor, "_technologyForm").GetLogicalDescendants().OfType<TabControl>().Distinct().Single();
            Assert.Equal(2, tabs.SelectedIndex);
            selector.SelectedItem = correction; await SettleTechnologyPresentation(window);
            Assert.DoesNotContain("invalid-field", selector.Classes);
            Assert.True(await editor.SaveAsync().WaitAsync(TimeSpan.FromSeconds(5)));
            var saved = XDocument.Load(fixture.TechtreePath).Descendants("effect").Single();
            Assert.Equal(correction, field.Length == 0 ? saved.Value : (string?)saved.Attribute(field[1..]));
            editor.SelectTechnology("B"); await AwaitTechnologyBuild(editor); await SettleTechnologyPresentation(window);
            Assert.DoesNotContain(editor.GetLogicalDescendants().OfType<Control>(), control => control.Classes.Contains("invalid-field"));
        });
}
