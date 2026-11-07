using System.Xml.Linq;
using AoMDivineDataEditor.Windows;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed partial class TechnologyEditorWorkflowTests
{
    [Theory]
    [InlineData("civilization", "civName", "Zeus", "Hades", "Add major god")]
    [InlineData("culture", "cultureName", "Greek", "Egyptian", "Add culture")]
    public void MultiPrerequisiteExclusionsAndAddAvailabilityFollowEditsWithoutRebuilding(string tag, string child, string first, string second, string addLabel)
        => WithTechnologyPresentation($"<prereqs><{tag}><{child}>{first}</{child}><{child}>{second}</{child}></{tag}></prereqs>", async (editor, window, fixture) =>
        {
            SupplyPrerequisiteEditingCatalogs(editor); TechnologyTab(editor, 1); await SettleTechnologyPresentation(window);
            var rows = MultiPrerequisiteSelectors(editor); var left = rows[0]; var right = rows[1];
            var add = GetPrivateField<StackPanel>(editor, "_prereqsPanel").GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, addLabel));
            Assert.False(add.IsEnabled); Assert.DoesNotContain(second, left.ItemsSource!.Cast<string>());
            await DeletePrerequisiteText(window, right); Assert.True(add.IsEnabled); Assert.Contains(second, left.ItemsSource!.Cast<string>());
            left.SelectedItem = second; await SettleTechnologyPresentation(window);
            Assert.DoesNotContain(second, right.ItemsSource!.Cast<string>()); Assert.Contains(first, right.ItemsSource!.Cast<string>());
            right.SelectedItem = first; await SettleTechnologyPresentation(window); Assert.False(add.IsEnabled);
            Assert.Same(left, MultiPrerequisiteSelectors(editor)[0]); Assert.Same(right, MultiPrerequisiteSelectors(editor)[1]);
            Assert.True(await SaveXmlChecked(editor));
            Assert.Equal(new[] { second, first }, FindTechnology(XDocument.Load(fixture.TechtreePath), "A").Element("prereqs")!.Element(tag)!.Elements(child).Select(element => element.Value));
        });

    [Theory]
    [InlineData("civilization", "civName", "Zeus", "Hades", "Add major god")]
    [InlineData("culture", "cultureName", "Greek", "Egyptian", "Add culture")]
    public void MultiPrerequisiteAddingToLegacyTextPreservesItsValueAndComment(string tag, string child, string first, string second, string addLabel)
        => WithTechnologyPresentation($"<prereqs><{tag}><!-- keep legacy comment -->{first}</{tag}></prereqs>", async (editor, window, fixture) =>
        {
            SupplyPrerequisiteEditingCatalogs(editor); TechnologyTab(editor, 1); await SettleTechnologyPresentation(window);
            GetPrivateField<StackPanel>(editor, "_prereqsPanel").GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, addLabel)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await AwaitTechnologyBuild(editor); await SettleTechnologyPresentation(window);
            var rows = MultiPrerequisiteSelectors(editor); Assert.Equal(2, rows.Length); Assert.Equal(first, rows[0].Text); Assert.Equal("", rows[1].Text);
            rows[1].SelectedItem = second; await SettleTechnologyPresentation(window); Assert.True(await SaveXmlChecked(editor));
            var saved = FindTechnology(XDocument.Load(fixture.TechtreePath), "A").Element("prereqs")!.Element(tag)!;
            Assert.Equal(new[] { first, second }, saved.Elements(child).Select(element => element.Value));
            Assert.Contains(saved.Nodes().OfType<XComment>(), comment => comment.Value.Contains("keep legacy comment", StringComparison.Ordinal));
        });

    private static void SupplyPrerequisiteEditingCatalogs(TechnologyEditorView editor)
        => editor.UpdateReferenceCatalogs(["BaseUnit", PresentationUnit], ["Unit", "BaseUnit", PresentationUnit],
            ["Greek", "Egyptian"], ["Zeus", "Hades"], ["CustomType"], ["HandAttack"],
            ["custom.tactics"], ["CustomCommand"], ["CustomPower"]);

    private static AutoCompleteBox[] MultiPrerequisiteSelectors(TechnologyEditorView editor)
        => GetPrivateField<StackPanel>(editor, "_prereqsPanel").GetLogicalDescendants()
            .OfType<AutoCompleteBox>().Distinct().ToArray();

    private static async Task DeletePrerequisiteText(Window window, AutoCompleteBox selector)
    {
        var input = selector.GetVisualDescendants().OfType<TextBox>().First();
        input.BringIntoView(); await SettleTechnologyPresentation(window);
        var point = input.TranslatePoint(new Point(input.Bounds.Width / 2, input.Bounds.Height / 2), window)!.Value;
        window.MouseMove(point); window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
        await SettleTechnologyPresentation(window);
        Assert.Equal(selector.Text!.Length, Math.Abs(input.SelectionEnd - input.SelectionStart));
        var changes = new List<string> { $"before: input={input.Text}; focused={input.IsFocused}; readonly={input.IsReadOnly}; selected={selector.SelectedItem}" };
        void Observe(object? sender, AvaloniaPropertyChangedEventArgs args)
        {
            if (args.Property == AutoCompleteBox.TextProperty || args.Property == AutoCompleteBox.SelectedItemProperty || args.Property == TextBox.TextProperty)
                changes.Add($"{sender?.GetType().Name}/{args.Property.Name}: {args.OldValue} -> {args.NewValue}");
        }
        input.PropertyChanged += Observe; selector.PropertyChanged += Observe;
        window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.None, null);
        window.KeyRelease(Key.Delete, RawInputModifiers.None, PhysicalKey.None, null);
        await SettleTechnologyPresentation(window);
        input.PropertyChanged -= Observe; selector.PropertyChanged -= Observe;
        Assert.True(string.IsNullOrEmpty(selector.Text), string.Join("\n", changes) + $"\nafter: input={input.Text}; focused={input.IsFocused}; selected={selector.SelectedItem}");
    }

    [Theory]
    [InlineData("Civilization", "civilization", "civName", "Zeus", "Hades", "Add major god")]
    [InlineData("Culture", "culture", "cultureName", "Greek", "Egyptian", "Add culture")]
    public void MultiPrerequisiteNewAndAdditionalRowsStayEmptyUntilChosen(string type, string tag, string child, string first, string second, string addLabel)
        => WithTechnologyPresentation("<prereqs><techstatus status='Active'>B</techstatus></prereqs>", async (editor, window, fixture) =>
        {
            SupplyPrerequisiteEditingCatalogs(editor); TechnologyTab(editor, 1); await SettleTechnologyPresentation(window);
            var before = File.ReadAllBytes(fixture.TechtreePath);
            var panel = GetPrivateField<StackPanel>(editor, "_prereqsPanel");
            panel.GetLogicalDescendants().OfType<ComboBox>().Single(box => Equals(box.SelectedItem, "TechStatus")).SelectedItem = type;
            await AwaitTechnologyBuild(editor); await SettleTechnologyPresentation(window);
            var initial = Assert.Single(MultiPrerequisiteSelectors(editor)); Assert.Equal("", initial.Text);
            var saving = editor.SaveAsync(); await ValidationPopupTest.Dismiss(window, saving, "prerequisite"); Assert.False(await saving);
            Assert.Equal(before, File.ReadAllBytes(fixture.TechtreePath));
            initial.SelectedItem = first; await SettleTechnologyPresentation(window);
            Assert.Same(initial, Assert.Single(MultiPrerequisiteSelectors(editor)));
            panel.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, addLabel)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await AwaitTechnologyBuild(editor); await SettleTechnologyPresentation(window);
            var rows = MultiPrerequisiteSelectors(editor); Assert.Equal(2, rows.Length);
            var blank = Assert.Single(rows, box => string.IsNullOrEmpty(box.Text));
            Assert.DoesNotContain(first, blank.ItemsSource!.Cast<string>()); Assert.Contains(second, blank.ItemsSource!.Cast<string>());
            saving = editor.SaveAsync(); await ValidationPopupTest.Dismiss(window, saving, "prerequisite"); Assert.False(await saving);
            Assert.Equal(before, File.ReadAllBytes(fixture.TechtreePath));
            blank.SelectedItem = second; await SettleTechnologyPresentation(window); Assert.True(await SaveXmlChecked(editor));
            Assert.Equal(new[] { first, second }, FindTechnology(XDocument.Load(fixture.TechtreePath), "A").Element("prereqs")!.Element(tag)!.Elements(child).Select(element => element.Value));
            editor.SelectTechnology("B"); editor.SelectTechnology("A"); await AwaitTechnologyBuild(editor); TechnologyTab(editor, 1); await SettleTechnologyPresentation(window);
            Assert.Equal(new[] { first, second }, MultiPrerequisiteSelectors(editor).Select(box => box.Text)); Assert.False(editor.IsDirty);
        });

    [Theory]
    [InlineData("civilization", "civName", "Zeus", "Hades", false)]
    [InlineData("civilization", "civName", "Zeus", "Hades", true)]
    [InlineData("culture", "cultureName", "Greek", "Egyptian", false)]
    [InlineData("culture", "cultureName", "Greek", "Egyptian", true)]
    public void MultiPrerequisiteDeleteAndRetypeKeepsFocusAndEmptyValueBlocksSave(string tag, string child, string first, string second, bool legacy)
        => WithTechnologyPresentation($"<prereqs><{tag}>{(legacy ? first : $"<{child}>{first}</{child}>")}</{tag}></prereqs>", async (editor, window, fixture) =>
        {
            SupplyPrerequisiteEditingCatalogs(editor); TechnologyTab(editor, 1); await SettleTechnologyPresentation(window);
            var selector = Assert.Single(MultiPrerequisiteSelectors(editor));
            GetPrivateField<TextBox>(editor, "_techNameBox").Focus(); await SettleTechnologyPresentation(window);
            await DeletePrerequisiteText(window, selector);
            Assert.Same(selector, Assert.Single(MultiPrerequisiteSelectors(editor))); Assert.Equal("", selector.Text);
            Assert.True(selector.IsKeyboardFocusWithin); Assert.True(editor.IsDirty);
            window.KeyTextInput(second[..1]); await SettleTechnologyPresentation(window); Assert.Equal(second[..1], selector.Text);
            var input = selector.GetVisualDescendants().OfType<TextBox>().First(); Assert.Equal(1, input.CaretIndex);
            window.KeyTextInput(second[1..]); await SettleTechnologyPresentation(window);
            selector.SelectedItem = second; await SettleTechnologyPresentation(window);
            Assert.Same(selector, Assert.Single(MultiPrerequisiteSelectors(editor))); Assert.True(await SaveXmlChecked(editor));
            Assert.Equal(second, FindTechnology(XDocument.Load(fixture.TechtreePath), "A").Element("prereqs")!.Element(tag)!.Element(child)!.Value);
            selector = Assert.Single(MultiPrerequisiteSelectors(editor)); GetPrivateField<TextBox>(editor, "_techNameBox").Focus(); await SettleTechnologyPresentation(window);
            var saved = File.ReadAllBytes(fixture.TechtreePath); await DeletePrerequisiteText(window, selector);
            Assert.Equal("", selector.Text); Assert.True(editor.IsDirty);
            var saving = editor.SaveAsync(); await ValidationPopupTest.Dismiss(window, saving, "prerequisite"); Assert.False(await saving);
            Assert.Equal(saved, File.ReadAllBytes(fixture.TechtreePath)); Assert.Equal("", Assert.Single(MultiPrerequisiteSelectors(editor)).Text);
        });
}
