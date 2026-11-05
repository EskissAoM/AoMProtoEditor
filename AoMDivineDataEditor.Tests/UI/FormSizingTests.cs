using System.Reflection;
using System.Xml.Linq;
using AoMDivineDataEditor.Windows;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed partial class TacticsStartupTests
{
    [Fact]
    public void AbilityOptionalCrossesHaveFourPixelRenderedGapsIncludingCompoundFields()
        => WithStandaloneXml(true, async (_, _, child, _, _) =>
        {
            var crosses = child.GetVisualDescendants().OfType<Button>()
                .Where(button => button.IsEffectivelyVisible && button.Classes.Contains("remove-field")).ToArray();
            Assert.Equal(6, crosses.Length);
            foreach (var cross in crosses)
            {
                var pair = Assert.IsType<StackPanel>(cross.Parent);
                var field = pair.Children[0];
                Assert.Equal(2, pair.Children.Count);
                var fieldPoint = field.TranslatePoint(default, child)!.Value;
                var crossPoint = cross.TranslatePoint(default, child)!.Value;
                Assert.InRange(crossPoint.X - fieldPoint.X - field.Bounds.Width, 3.9, 4.1);
                Assert.InRange(Math.Abs(crossPoint.Y + cross.Bounds.Height / 2 - fieldPoint.Y - field.Bounds.Height / 2), 0, .5);
            }
            Assert.False((bool)Field(child, "_isDirty")!);
            Assert.Empty(await SaveStandaloneChecked(child, true));
        }, abilitySource: AbilityXml.Replace("<radius>",
            "<rangeindicator range='10' width='2'>Arrow</rangeindicator><cursortype>Heal</cursortype><activetime>3</activetime><replacerolloverid>STR_KEEP</replacerolloverid><replaceicon>resources/test.png</replaceicon><radius>", StringComparison.Ordinal));

    [Fact]
    public void AbilityChipInputsAreCompactWithoutNarrowingTheirSuggestionPopupOrDirtyingTheRecord()
        => WithStandaloneXml(true, async (_, _, child, _, _) =>
        {
            var pickers = child.GetLogicalDescendants().OfType<AutoCompleteBox>()
                .Where(box => box.Classes.Contains("compact-chip-picker")).ToArray();
            Assert.Equal(2, pickers.Length);
            Assert.All(pickers, box => Assert.Equal(50, box.Width));
            var shown = pickers.First(box => box.IsEffectivelyVisible);
            Assert.Equal(50, shown.Bounds.Width); Assert.Equal(30, shown.Bounds.Height);
            var popup = typeof(AutoCompleteBox).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                .Where(field => typeof(Popup).IsAssignableFrom(field.FieldType))
                .Select(field => field.GetValue(shown)).OfType<Popup>().Single();
            var suggestions = popup.Child!.GetLogicalDescendants().OfType<ListBox>().Single();
            Assert.True(suggestions.MinWidth >= 250);
            // Already-selected BaseUnit is deliberately omitted by this chip picker.
            Assert.Contains("CustomUnit", shown.ItemsSource!.Cast<string>());
            Assert.False((bool)Field(child, "_isDirty")!);
            Assert.Empty(await SaveStandaloneChecked(child, true));
        }, abilitySource: AbilityXml.Replace("<radius>", "<abstractplacementtargettype>BaseUnit</abstractplacementtargettype><radius>", StringComparison.Ordinal));
}

public sealed class BountyFieldAlignmentTests
{
    [Fact]
    public void SpawnedAsSelectorAndBothRemovalButtonsHaveCentredBoundsAndFourPixelFieldGap()
    {
        using var workspace = new TestWorkspace();
        var path = workspace.WriteText("major_gods_mods.xml", "<civsmods><civ><name>A</name><bountyresourceearning><active>true</active><bountyreward condition='Damage' unittype='All' resource='Favor' asspawnedunit='BaseUnit'>1</bountyreward></bountyresourceearning></civ></civsmods>");
        var before = File.ReadAllBytes(path);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var view = new MajorGodEditorView([], path, protoUnitNames: ["BaseUnit"]);
            var window = new Window { Width = 1100, Height = 850, Content = view };
            try
            {
                view.SetModifiedMode(true); view.SelectMajorGod("A");
                window.Show(); view.GetLogicalDescendants().OfType<TabControl>().Single().SelectedIndex = 2;
                await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                var selector = view.GetVisualDescendants().OfType<AutoCompleteBox>().Single(box =>
                    box.IsEffectivelyVisible && box.Text == "BaseUnit" && box.Parent is StackPanel parent &&
                    parent.Children.OfType<TextBlock>().Any(label => label.Text == "Spawned as"));
                var pair = Assert.IsType<StackPanel>(selector.Parent);
                // Shared grouping keeps both removal roles with the final field
                // so neither cross wraps away from the selector.
                var crosses = pair.Children.OfType<Button>().ToArray();
                Assert.Equal(2, crosses.Length);
                var fieldCross = Assert.IsType<Button>(pair.Children[pair.Children.IndexOf(selector) + 1]);
                var rowCross = Assert.IsType<Button>(pair.Children[^1]);
                Assert.NotSame(fieldCross, rowCross);
                Assert.All(crosses, cross => Assert.Contains("remove-button", cross.Classes));
                Assert.IsType<WrapPanel>(pair.Parent);
                var point = selector.TranslatePoint(default, window)!.Value;
                var fieldPoint = fieldCross.TranslatePoint(default, window)!.Value;
                var rowPoint = rowCross.TranslatePoint(default, window)!.Value;
                Assert.InRange(fieldPoint.X - point.X - selector.Bounds.Width, 3.9, 4.1);
                Assert.InRange(Math.Abs(fieldPoint.Y + fieldCross.Bounds.Height / 2 - point.Y - selector.Bounds.Height / 2), 0, .5);
                Assert.InRange(Math.Abs(rowPoint.Y + rowCross.Bounds.Height / 2 - point.Y - selector.Bounds.Height / 2), 0, .5);
                Assert.InRange(rowPoint.X - fieldPoint.X - fieldCross.Bounds.Width, 3.9, 4.1);
                Assert.Equal(before, File.ReadAllBytes(path)); Assert.False(view.IsDirty);
            }
            finally { window.Close(); view.ReleaseMajorGodXmlEditing(); }
        });
    }
}
