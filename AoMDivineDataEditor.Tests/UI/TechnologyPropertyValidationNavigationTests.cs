using System.Reflection;
using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed partial class TechnologyEditorWorkflowTests
{
    [Theory]
    [InlineData("", "@orderhint")]
    [InlineData("<orderhint>bad</orderhint>", "orderhint")]
    [InlineData("<researchlimit>-1</researchlimit>", "researchlimit")]
    [InlineData("<delay>bad</delay>", "delay")]
    [InlineData("<initialdelay>bad</initialdelay>", "initialdelay")]
    [InlineData("<combatxptier>bad</combatxptier>", "combatxptier")]
    [InlineData("<cost resourcetype='Food'>-1</cost>", "cost")]
    [InlineData("<cost resourcetype='Wood'>bad</cost>", "cost")]
    public void TechnologyPropertyFailureRevealsExactFieldAndCorrectionSaves(string body, string path)
        => WithTechnologyPresentation(body, async (editor, window, fixture) =>
        {
            var source = GetPrivateField<XElement>(editor, "_current");
            if (path.StartsWith('@'))
            {
                source.SetAttributeValue("orderhint", "bad");
                await (Task)editor.GetType().GetMethod("BuildEditorAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor, null)!;
                await SettleTechnologyPresentation(window);
            }
            TechnologyTab(editor, 2); await SettleTechnologyPresentation(window);
            var before = File.ReadAllBytes(fixture.TechtreePath);
            var saving = editor.SaveAsync();
            await ValidationPopupTest.Dismiss(window, saving, "Technology");
            Assert.False(await saving); await SettleTechnologyPresentation(window);
            Assert.Equal(before, File.ReadAllBytes(fixture.TechtreePath));
            var panel = GetPrivateField<StackPanel>(editor, "_propertiesPanel");
            var field = Assert.Single(panel.GetLogicalDescendants().OfType<TextBox>().Distinct(), control => control.Classes.Contains("invalid-field"));
            Assert.True(field.IsKeyboardFocusWithin);
            Assert.Same(field, EditorValidationBindings.Find(editor, new LocatedValidationIssue("typed source", ValidationSourceLocation.From(source, path))));
            var tabs = GetPrivateField<Control>(editor, "_technologyForm").GetLogicalDescendants().OfType<TabControl>().Distinct().Single();
            Assert.Equal(0, tabs.SelectedIndex);
            field.Text = "2"; await SettleTechnologyPresentation(window);
            Assert.DoesNotContain("invalid-field", field.Classes);
            Assert.True(await editor.SaveAsync().WaitAsync(TimeSpan.FromSeconds(5)));
            editor.SelectTechnology("B"); await AwaitTechnologyBuild(editor); await SettleTechnologyPresentation(window);
            Assert.DoesNotContain(editor.GetLogicalDescendants().OfType<Control>(), control => control.Classes.Contains("invalid-field"));
        });

    [Fact]
    public void TechnologyTypeChipFailureRevealsPickerAndRemovalAllowsReplacementAndSave()
        => WithTechnologyPresentation("<techtype>MissingType</techtype>", async (editor, window, fixture) =>
        {
            var before = File.ReadAllBytes(fixture.TechtreePath);
            TechnologyTab(editor, 1); await SettleTechnologyPresentation(window);
            var saving = editor.SaveAsync();
            await ValidationPopupTest.Dismiss(window, saving, "tech type");
            Assert.False(await saving); await SettleTechnologyPresentation(window);
            Assert.Equal(before, File.ReadAllBytes(fixture.TechtreePath));
            var panel = GetPrivateField<StackPanel>(editor, "_propertiesPanel");
            var picker = Assert.Single(panel.GetLogicalDescendants().OfType<AutoCompleteBox>().Distinct(), control => control.Classes.Contains("invalid-field"));
            Assert.True(picker.IsKeyboardFocusWithin);
            var remove = Assert.Single(panel.GetLogicalDescendants().OfType<Button>().Distinct(), control => control.Classes.Contains("chip-remove-button"));
            remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.DoesNotContain("invalid-field", picker.Classes);
            picker.SelectedItem = "CustomType"; await SettleTechnologyPresentation(window);
            Assert.True(await editor.SaveAsync().WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Contains("<techtype>CustomType</techtype>", File.ReadAllText(fixture.TechtreePath));
        });

    [Theory]
    [InlineData("<cost resourcetype='Food'>1</cost><cost resourcetype='Food'>bad</cost>")]
    [InlineData("<cost resourcetype='UnknownResource'>1</cost>")]
    public void PreservedCostFailureDoesNotHighlightAnUnrelatedEditableCost(string body)
        => WithTechnologyPresentation(body, async (editor, window, fixture) =>
        {
            var before = File.ReadAllBytes(fixture.TechtreePath);
            if (body.Contains("bad", StringComparison.Ordinal))
            {
                var warning = Assert.Single(window.OwnedWindows.OfType<AoMDivineDataEditor.Windows.Prompt>());
                Assert.Contains("at most one cost per resource", warning.PromptText);
                warning.Close(); await SettleTechnologyPresentation(window);
            }
            var saving = editor.SaveAsync();
            await ValidationPopupTest.Dismiss(window, saving, "cost");
            Assert.False(await saving); await SettleTechnologyPresentation(window);
            Assert.Equal(before, File.ReadAllBytes(fixture.TechtreePath));
            Assert.DoesNotContain(editor.GetLogicalDescendants().OfType<Control>(), control => control.Classes.Contains("invalid-field"));
        });
}
