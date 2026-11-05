using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed partial class TechnologyEditorWorkflowTests
{
    [Theory]
    [InlineData("Hitpoints", "amount='bad' relativity='Absolute'", "ProtoUnit", "BaseUnit", "amount", "2")]
    [InlineData("Hitpoints", "relativity='Absolute'", "ProtoUnit", "BaseUnit", "amount", "0")]
    [InlineData("Hitpoints", "amount='1'", "ProtoUnit", "BaseUnit", "relativity", "Multiply base")]
    [InlineData("MaximumResourceTrickleRate", "amount='1' resource='Food'", "Player", "0", "relativity", "Set to")]
    [InlineData("AddGoal", "amount='1' relativity='Assign'", "Player", "0", "goalname", "GoalA")]
    public void DataValueFailureRevealsExactFieldAndCorrectionSaves(string subtype, string attributes, string targetType, string target, string attribute, string correction)
        => WithTechnologyPresentation($"<effects><effect type='Data' subtype='{subtype}' {attributes}><target type='{targetType}'>{target}</target></effect></effects>", async (editor, window, fixture) =>
        {
            var before = File.ReadAllBytes(fixture.TechtreePath);
            var saving = editor.SaveAsync();
            await ValidationPopupTest.Dismiss(window, saving, "effect");
            Assert.False(await saving); await SettleTechnologyPresentation(window);
            Assert.Equal(before, File.ReadAllBytes(fixture.TechtreePath));
            var panel = GetPrivateField<StackPanel>(editor, "_effectsPanel");
            var field = Assert.Single(panel.GetLogicalDescendants().OfType<Control>().Distinct(), control => control.Classes.Contains("invalid-field"));
            Assert.True(field.IsKeyboardFocusWithin);
            var source = XDocument.Load(fixture.TechtreePath).Descendants("effect").Single();
            Assert.Same(field, EditorValidationBindings.Find(editor, new LocatedValidationIssue("typed source", ValidationSourceLocation.From(source, "@" + attribute))));
            if (field is ComboBox combo)
            {
                Assert.Null(combo.SelectedItem);
                combo.SelectedItem = correction;
            }
            else
            {
                var text = Assert.IsType<TextBox>(field);
                if (attribute == "amount" && !attributes.Contains("amount=", StringComparison.Ordinal)) Assert.Equal("", text.Text);
                text.Text = correction;
            }
            await SettleTechnologyPresentation(window);
            Assert.DoesNotContain("invalid-field", field.Classes);
            Assert.True(await editor.SaveAsync().WaitAsync(TimeSpan.FromSeconds(5)));
            var saved = XDocument.Load(fixture.TechtreePath).Descendants("effect").Single();
            var expected = attribute == "relativity" ? (correction == "Set to" ? "Assign" : "BasePercent") : correction;
            Assert.Equal(expected, (string?)saved.Attribute(attribute));
            editor.SelectTechnology("B"); await AwaitTechnologyBuild(editor); await SettleTechnologyPresentation(window);
            Assert.DoesNotContain(editor.GetLogicalDescendants().OfType<Control>(), control => control.Classes.Contains("invalid-field"));
        });
}
