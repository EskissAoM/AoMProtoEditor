using AoMDivineDataEditor.Classes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed partial class TechnologyEditorWorkflowTests
{
    [Fact]
    public void PrerequisiteReferenceGroupsAndAddButtonsStayCenteredAfterRebuild()
        => WithTechnologyPresentation("<prereqs><civilization><civName>Zeus</civName></civilization><typecount unit='Unit' operator='ne' count='12' state='aliveState'/></prereqs>", async (editor, window, fixture) =>
        {
            TechnologyTab(editor, 1); await SettleTechnologyPresentation(window);
            var panel = GetPrivateField<StackPanel>(editor, "_prereqsPanel");
            var before = File.ReadAllBytes(fixture.TechtreePath);
            for (var pass = 0; pass < 2; pass++)
            {
                foreach (var width in new[] { 950d, 520d })
                {
                    panel.Width = width; await SettleTechnologyPresentation(window);
                    FormLayoutAssertions.NoOverlaps(panel, "Prerequisites review");
                    var reference = panel.GetLogicalDescendants().OfType<AutoCompleteBox>().Single(box => box.Text == "Zeus");
                    var pair = Assert.IsType<StackPanel>(reference.Parent);
                    var cross = Assert.Single(pair.Children.OfType<Button>());
                    FormLayoutAssertions.CrossGap(reference, cross, panel);
                    var add = panel.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, "Add major god"));
                    if (width > 900)
                    {
                        double Center(Control control) => control.TranslatePoint(default, panel)!.Value.Y + control.Bounds.Height / 2;
                        Assert.InRange(Math.Abs(Center(reference) - Center(add)), 0, .5);
                    }
                    var comparison = panel.GetLogicalDescendants().OfType<ComboBox>().Single(box => Equals(box.SelectedItem, "≠"));
                    Assert.Equal(EditorFormLayout.ComparisonOperatorWidth, comparison.Bounds.Width);
                    Assert.Equal(6, comparison.Items.Count);
                    comparison.IsDropDownOpen = true; await SettleTechnologyPresentation(window);
                    var popup = comparison.GetTemplateDescendants().OfType<Popup>().Single();
                    Assert.InRange(popup.Child!.Bounds.Width, 62, 90);
                    comparison.IsDropDownOpen = false; await SettleTechnologyPresentation(window);
                }
                if (pass == 0) { editor.SelectTechnology("B"); editor.SelectTechnology("A"); await AwaitTechnologyBuild(editor); TechnologyTab(editor, 1); await SettleTechnologyPresentation(window); }
            }
            Assert.False(editor.IsDirty); Assert.Equal(before, File.ReadAllBytes(fixture.TechtreePath));
        });
}
