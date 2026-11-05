using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed partial class TechnologyEditorWorkflowTests
{
    [Theory]
    [InlineData("<techstatus status='Active'>Missing</techstatus>", "techstatus", "B")]
    [InlineData("<techstatus status='Wrong'>B</techstatus>", "techstatus/@status", "Active")]
    [InlineData("<specificage>Wrong</specificage>", "specificage", "HeroicAge")]
    [InlineData("<typecount unit='Missing' operator='gte' count='1' state='aliveState'/>", "typecount/@unit", "Unit")]
    [InlineData("<typecount unit='Unit' operator='gte' count='-1' state='aliveState'/>", "typecount/@count", "2")]
    [InlineData("<typecount unit='Unit' operator='wrong' count='1' state='aliveState'/>", "typecount/@operator", "=")]
    [InlineData("<typecount unit='Unit' operator='gte' count='1' state='wrong'/>", "typecount/@state", "alive")]
    [InlineData("<typecount unit='Unit' operator='gte' count='1' state='alive'/>", "typecount/@state", "alive")]
    [InlineData("<KBStat kbStat='Wrong' operator='gte' value='1'/>", "KBStat/@kbStat", "unitsLost")]
    [InlineData("<KBStat kbStat='unitsLost' operator='gte' value='bad'/>", "KBStat/@value", "2")]
    [InlineData("<KBStat kbStat='tradeProfit' operator='gte' value='1'/>", "KBStat/@kbParam", "Food")]
    [InlineData("<culture><cultureName>Missing</cultureName></culture>", "culture/cultureName", "Greek")]
    [InlineData("<civilization>Missing</civilization>", "civilization", "Zeus")]
    public void PrerequisiteFailureRevealsItsExactFieldAndCorrectionSaves(string prereqXml, string sourcePath, string correction)
        => WithTechnologyPresentation("<prereqs>" + prereqXml + "</prereqs>", async (editor, window, fixture) =>
        {
            var before = File.ReadAllBytes(fixture.TechtreePath);
            var saving = editor.SaveAsync();
            await ValidationPopupTest.Dismiss(window, saving, "prerequisite");
            Assert.False(await saving); await SettleTechnologyPresentation(window);
            Assert.Equal(before, File.ReadAllBytes(fixture.TechtreePath));
            var panel = GetPrivateField<StackPanel>(editor, "_prereqsPanel");
            var field = Assert.Single(panel.GetLogicalDescendants().OfType<Control>().Distinct(), control => control.Classes.Contains("invalid-field"));
            Assert.True(field.IsKeyboardFocusWithin);
            var tabs = GetPrivateField<Control>(editor, "_technologyForm").GetLogicalDescendants().OfType<TabControl>().Distinct().Single();
            Assert.Equal(1, tabs.SelectedIndex);

            var source = XDocument.Load(fixture.TechtreePath).Root!.Elements("tech").First();
            Assert.Same(field, EditorValidationBindings.Find(editor, new LocatedValidationIssue("typed source", ValidationSourceLocation.From(source.Element("prereqs")!, sourcePath))));
            switch (field)
            {
                case AutoCompleteBox selector: selector.SelectedItem = correction; break;
                case ComboBox combo: combo.SelectedItem = correction; break;
                case TextBox number: number.Text = correction; break;
                default: Assert.Fail("Unexpected prerequisite field: " + field.GetType().Name); break;
            }
            await AwaitTechnologyBuild(editor); await SettleTechnologyPresentation(window);
            Assert.DoesNotContain("invalid-field", field.Classes);
            Assert.True(await editor.SaveAsync().WaitAsync(TimeSpan.FromSeconds(5)));
            editor.SelectTechnology("B"); await AwaitTechnologyBuild(editor); await SettleTechnologyPresentation(window);
            Assert.DoesNotContain(editor.GetLogicalDescendants().OfType<Control>(), control => control.Classes.Contains("invalid-field"));
        });
}
