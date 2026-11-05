using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed partial class TechnologyEditorWorkflowTests
{
    [Theory]
    [InlineData("Hitpoints", "", "<target type='ProtoUnit'>Missing</target>", false, "BaseUnit")]
    [InlineData("Hitpoints", "", "<target type='Unit'></target>", false, "BaseUnit")]
    [InlineData("Damage", "action='Missing'", "<target type='ProtoUnit'>BaseUnit</target>", true, "HandAttack")]
    [InlineData("Damage", "", "<target type='ProtoUnit'>BaseUnit</target>", true, "All")]
    [InlineData("Damage", "allactions='0'", "<target type='ProtoUnit'>BaseUnit</target>", true, "HandAttack")]
    public void DataReferenceFailureRevealsExactSelectorAndCorrectionSaves(string subtype, string attributes, string target, bool action, string correction)
        => WithTechnologyPresentation($"<effects><effect type='Data' subtype='{subtype}' amount='1' relativity='Absolute' {attributes}>{target}</effect></effects>", async (editor, window, fixture) =>
        {
            var before = File.ReadAllBytes(fixture.TechtreePath);
            var saving = editor.SaveAsync();
            await ValidationPopupTest.Dismiss(window, saving, "effect");
            Assert.False(await saving); await SettleTechnologyPresentation(window);
            Assert.Equal(before, File.ReadAllBytes(fixture.TechtreePath));
            var panel = GetPrivateField<StackPanel>(editor, "_effectsPanel");
            var selector = Assert.Single(panel.GetLogicalDescendants().OfType<AutoCompleteBox>().Distinct(), control => control.Classes.Contains("invalid-field"));
            Assert.True(selector.IsKeyboardFocusWithin);
            var effect = XDocument.Load(fixture.TechtreePath).Descendants("effect").Single();
            Assert.Same(selector, EditorValidationBindings.Find(editor, new LocatedValidationIssue("typed source", action ? ValidationSourceLocation.From(effect, "@action") : ValidationSourceLocation.From(effect.Element("target")!))));
            if (action && attributes != "action='Missing'") Assert.True(string.IsNullOrEmpty(selector.Text));
            selector.SelectedItem = correction; await SettleTechnologyPresentation(window);
            Assert.DoesNotContain("invalid-field", selector.Classes);
            Assert.True(await editor.SaveAsync().WaitAsync(TimeSpan.FromSeconds(5)));
            var saved = XDocument.Load(fixture.TechtreePath).Descendants("effect").Single();
            Assert.Equal(correction == "All" ? "1" : correction,
                !action ? saved.Element("target")!.Value : (string?)saved.Attribute(correction == "All" ? "allactions" : "action"));
            editor.SelectTechnology("B"); await AwaitTechnologyBuild(editor); await SettleTechnologyPresentation(window);
            Assert.DoesNotContain(editor.GetLogicalDescendants().OfType<Control>(), control => control.Classes.Contains("invalid-field"));
        });

    [Theory]
    [InlineData("")]
    [InlineData("<target type='ProtoUnit'>BaseUnit</target><target type='ProtoUnit'>Missing</target>")]
    [InlineData("<target type='Tech'>Missing</target>")]
    [InlineData("<target>Missing</target>")]
    public void UnrepresentedDataTargetFailureKeepsPopupWithoutHighlightingUnitSelector(string target)
        => WithTechnologyPresentation($"<effects><effect type='Data' subtype='Hitpoints' amount='1' relativity='Absolute'>{target}</effect></effects>", async (editor, window, fixture) =>
        {
            var before = File.ReadAllBytes(fixture.TechtreePath);
            var saving = editor.SaveAsync();
            await ValidationPopupTest.Dismiss(window, saving, "target");
            Assert.False(await saving); await SettleTechnologyPresentation(window);
            Assert.Equal(before, File.ReadAllBytes(fixture.TechtreePath));
            Assert.DoesNotContain(editor.GetLogicalDescendants().OfType<Control>(), control => control.Classes.Contains("invalid-field"));
        });
}
