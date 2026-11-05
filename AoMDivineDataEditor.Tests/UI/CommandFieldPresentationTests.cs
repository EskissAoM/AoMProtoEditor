using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class CommandFieldPresentationTests : IDisposable
{
    private const string LongUnit = "CustomUnitWithAnExtremelyLongTransformReferenceName";
    public void Dispose() => AvaloniaTestHost.CollectUiGarbage();
    private static async Task Settle(Window window)
    {
        await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(); window.UpdateLayout(); await Task.Delay(30);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public void SharedTransformFieldsWrapAtBothWidthsAndDoNotEditOnOpening(bool readOnly, bool lockFrom)
    {
        AvaloniaTestHost.RunAsync(async () =>
        {
            var changes = 0;
            var definition = new ProtoUnitTransformDefinition { From = "UnitA", To = LongUnit, Tech = "TechA", Command = "C", RevertOthersTo = "UnitA" };
            var values = new Dictionary<string, string> { ["associatedtech"] = "TechB", ["forbidtech"] = "LegacyTech", ["associatedpower"] = "PowerA" };
            var original = definition.ToElement().ToString(); var originalValues = values.ToArray();
            var editor = new ProtoUnitCommandTransformEditor(definition, values, ["UnitA", LongUnit], ["TechA", "TechB"], ["PowerA"], !readOnly, lockFrom, () => changes++);
            var duration = ProtoUnitCommandFieldFactory.CreateTextBox("2.5", EditorFieldWidthRole.Numeric, !readOnly);
            editor.AddAssociatedTechField("Duration", duration);
            var root = new StackPanel { Children = { editor, new ProtoUnitCommandFlagsEditor(new HashSet<string>(["transformselected", "unitcommand"]), !readOnly, () => ProtoUnitCommandTransformKind.Unique, () => changes++) } };
            var window = new Window { Content = root, Width = 800, Height = 900 };
            try
            {
                window.Show();
                foreach (var width in new[] { 760d, 420d, 760d })
                { root.Width = width; await Settle(window); FormLayoutAssertions.NoOverlaps(root, $"transform/{readOnly}/{lockFrom}/{width}"); }
                Assert.Equal(!readOnly && !lockFrom, editor.FromEditor.IsEnabled);
                Assert.Equal(!readOnly, editor.ToEditor.IsEnabled);
                foreach (var field in new[] { editor.FromEditor, editor.ToEditor, editor.PrereqTechEditor, editor.AssociatedTechEditor, editor.RevertOthersEditor! }.Concat(editor.OptionalValueEditors.Values.OfType<AutoCompleteBox>()))
                {
                    Assert.Equal(200, field.Width); Assert.Equal(200, field.MinWidth); Assert.Equal(200, field.MaxWidth);
                    Assert.True(EditorFieldInteraction.IsConfigured(field));
                    if (!readOnly && field.Parent is StackPanel pair && pair.Children.OfType<Button>().FirstOrDefault(button => button.Classes.Contains("remove-field")) is { } cross)
                        FormLayoutAssertions.CrossGap(field, cross, window);
                }
                Assert.Equal(90, duration.Width); Assert.Equal(0, changes);
                Assert.Equal(original, definition.ToElement().ToString()); Assert.Equal(originalValues, values.ToArray());
                if (!readOnly)
                {
                    var input = editor.ToEditor.GetVisualDescendants().OfType<TextBox>().First();
                    duration.Focus(); await Settle(window);
                    Assert.InRange(input.GetVisualDescendants().OfType<ScrollViewer>().First().Offset.X, 0, .5);
                    var point = input.TranslatePoint(new Point(25, 15), window)!.Value;
                    window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); await Settle(window);
                    Assert.Equal(LongUnit.Length, Math.Abs(input.SelectionEnd - input.SelectionStart)); Assert.Equal(0, changes);
                    editor.ToEditor.SelectedItem = "UnitA"; await Settle(window);
                    Assert.Equal("UnitA", definition.To); Assert.True(changes > 0);
                    var power = editor.OptionalValueEditors["associatedpower"];
                    var group = Assert.IsType<StackPanel>(power.Parent);
                    group.Children.OfType<Button>().Single(button => button.Classes.Contains("remove-field")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.False(values.ContainsKey("associatedpower"));
                    group.Children.OfType<Button>().Single(button => Equals(button.Content, "Associated Power")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    ((AutoCompleteBox)power).SelectedItem = "PowerA"; await Settle(window);
                    Assert.Equal("PowerA", values["associatedpower"]); FormLayoutAssertions.NoOverlaps(root, "reopened optional Power");
                }
            }
            finally { window.Close(); }
        });
    }
}

public sealed partial class CommandXmlEditingTests
{
    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public void StandaloneCommandRolesAndWrappingPreserveNoTouchAndSavePolicies(bool transform, bool readOnly)
        => WithCommand(async (window, path, control) =>
        {
            var before = File.ReadAllBytes(path);
            var fields = Field<Dictionary<string, Control>>(window, "_valueEditors");
            Assert.Equal(300, Field<TextBox>(window, "_nameBox").Width);
            Assert.Equal(180, Field<ComboBox>(window, "_commandModeBox").Width);
            Assert.Equal(!readOnly, fields.ContainsKey("displaynameid"));
            Assert.Equal(!readOnly, fields.ContainsKey("icon"));
            if (!readOnly) { Assert.Equal(300, fields["displaynameid"].Width); Assert.Equal(240, fields["icon"].Width); }
            if (fields.TryGetValue("rollovertextid", out var rollover))
                RolloverAssertions.Configured(Assert.IsType<TextBox>(rollover));
            foreach (var width in new[] { 1400d, window.MinWidth })
            {
                window.Width = width; await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle); window.UpdateLayout();
                var form = Field<StackPanel>(window, "_structuredForm");
                FormLayoutAssertions.NoOverlaps(form.Children[0], "command name/type");
                FormLayoutAssertions.NoOverlaps(Field<StackPanel>(window, "_commandFieldsHost"), $"command mode/{transform}/{readOnly}/{width}");
            }
            Assert.False((bool)Call(window, "HasUnsavedChanges")!); Assert.Equal(before, File.ReadAllBytes(path)); Assert.Equal(0, control.Calls);
            if (!readOnly && !transform)
            {
                Field<ComboBox>(window, "_commandModeBox").SelectedItem = "Spawn";
                await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
                Assert.Equal(90, fields["amount"].Width); Assert.Equal(200, fields["protounit"].Width);
                ((AutoCompleteBox)fields["protounit"]).SelectedItem = "CustomUnit";
                ((TextBox)fields["amount"]).Text = "2";
                ((AutoCompleteBox)fields["associatedtech"]).SelectedItem = "Tech";
                Assert.Empty(await Save(window));
                var saved = XDocument.Load(path).Descendants("protounitcommand").Single();
                Assert.Equal("CustomUnit", saved.Element("protounit")!.Value); Assert.Equal("2", saved.Element("amount")!.Value);
                Assert.Equal("yes", (string?)saved.Element("future")!.Attribute("keep"));
            }
        }, transform, readOnly);
}
