using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Controls;
using AoMDivineDataEditor.Windows;
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

public sealed partial class PowerFieldPresentationTests : IDisposable
{
    private const string LongUnit = "CustomUnitWithAnExtremelyLongReferenceNameForPowerPresentation";
    private static T Field<T>(PowerEditorView view, string name) where T : Control => view.FindControl<T>(name)!;
    public void Dispose() => AvaloniaTestHost.CollectUiGarbage();

    private static async Task Settle(Window window)
    {
        await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(); window.UpdateLayout(); await Task.Delay(25);
    }

    private static void WithPower(string type, string body, bool original, Func<PowerEditorView, Window, string, Task> check)
    {
        using var workspace = new TestWorkspace(nameof(PowerFieldPresentationTests));
        var power = $"<power name='A' type='{type}' godpower=''>{body}</power>";
        var path = workspace.WriteText("TestMod/game/data/gameplay/powers_mods.xml",
            $"<powersmod>{power}<power name='B' type='Bolt'/></powersmod>");
        var strings = workspace.WriteText("TestMod/game/data/strings/stringmods.txt", "");
        AvaloniaTestHost.RunAsync(async () =>
        {
            var view = new PowerEditorView(original ? [new PowerDefinition("A", true, XElement.Parse(power))] : [], path,
                protoUnitNames: [LongUnit, "OtherUnit"], unitTypeNames: ["Unit", "CustomType"],
                techNames: ["CustomTech"], protoActionNames: ["Gather", "HandAttack"],
                tacticNames: ["custom.tactics"], iconPaths: [@"resources\custom\icon.png"],
                modStringsPath: strings, resolveStringAsync: id => Task.FromResult<string?>(id));
            var window = new Window { Content = view, Width = 1440, Height = 900 };
            try
            {
                window.Show(); view.SetModifiedMode(!original); await view.SelectPowerAsync("A"); await Settle(window);
                await check(view, window, path);
            }
            finally { foreach (var prompt in window.OwnedWindows.ToArray()) prompt.Close(); window.Close(); }
        });
    }

    public static IEnumerable<object[]> FixedPowerFamilies =>
        new[] { "Bolt", "Stampede", "Tempest", "Meteor", "Implode", "Rain", "UnitModify", "SwitchingEffects" }
            .SelectMany(type => new[] { new object[] { type, false }, new object[] { type, true } });

    [Theory]
    [MemberData(nameof(FixedPowerFamilies))]
    public void EveryFixedFieldHasAnExplicitStableRoleWithoutDirtyingOrWriting(string type, bool original)
        => WithPower(type, "<activetime>10</activetime><icon>resources\\custom\\icon.png</icon>", original, async (view, window, path) =>
        {
            var before = File.ReadAllBytes(path);
            var schema = XDocument.Load(Path.Combine(ProjectSource.RootPath, "Windows", "PowerEditorView.axaml"));
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            var fields = schema.Descendants().Where(node => node.Name.LocalName is "TextBox" or "AutoCompleteBox" or "ComboBox" or "AssetPathEditor").ToArray();
            Assert.Equal(502, fields.Length);
            var checkedFields = 0;
            foreach (var node in fields)
            {
                var field = Field<Control>(view, (string)node.Attribute(x + "Name")!); Assert.NotNull(field);
                if (field is TextBox { AcceptsReturn: true }) continue;
                Assert.True(EditorFormLayout.TryGetWidthRole(field, out var role), field.Name);
                Assert.Equal(EditorFormLayout.WidthFor(role), field.Width);
                Assert.Equal(field.Width, field.MinWidth); Assert.Equal(field.Width, field.MaxWidth);
                Assert.True(EditorFieldInteraction.IsConfigured(field), field.Name);
                checkedFields++;
            }
            Assert.Equal(497, checkedFields);
            Assert.Equal(300, Field<TextBox>(view, "_nameBox").Width);
            Assert.Equal(90, Field<TextBox>(view, "_activeTimeBox").Width);
            Assert.Equal(60, Field<TextBox>(view, "_orderHintBox").Width);
            Assert.Equal(50, Field<AutoCompleteBox>(view, "_targetTypePicker").Width);
            Assert.Equal(180, Field<AutoCompleteBox>(view, "_powerTypeBox").Width);
            Assert.Equal(180, Field<AutoCompleteBox>(view, "_otherSpecificAttributePicker").Width);
            Assert.Equal(240, Field<AutoCompleteBox>(view, "_meteorStrikeSoundBox").Width);
            Assert.Equal(240, Field<AssetPathEditor>(view, "_iconEditor").Width);
            Field<TabControl>(view, "_powerTabs").SelectedIndex = 1; await Settle(window);
            Assert.False(view.IsDirty); Assert.Equal(before, File.ReadAllBytes(path));
            await view.SelectPowerAsync(original ? "A" : "B"); Assert.False(view.IsDirty);
        });

    [Fact]
    public void FixedReferenceKeepsPrefixHoverAndFirstEntrySelection()
        => WithPower("Bolt", $"<placement losprotounit='{LongUnit}'>Full</placement>", false, async (view, window, _) =>
        {
            var picker = Field<AutoCompleteBox>(view, "_losProtoUnitBox");
            Assert.Equal(200, picker.Width); Assert.Contains(LongUnit, picker.ItemsSource!.Cast<string>());
            var input = picker.GetVisualDescendants().OfType<TextBox>().First();
            input.BringIntoView(); Field<TextBox>(view, "_nameBox").Focus(); await Settle(window);
            var scroll = input.GetVisualDescendants().OfType<ScrollViewer>().First(); Assert.InRange(scroll.Offset.X, 0, .5);
            var tip = new CancelRoutedEventArgs(ToolTip.ToolTipOpeningEvent); picker.RaiseEvent(tip);
            Assert.False(tip.Cancel);
            Assert.Contains(Assert.IsType<ScrollViewer>(ToolTip.GetTip(picker)).GetLogicalDescendants().OfType<TextBlock>(), text => text.Text == LongUnit);
            var point = input.TranslatePoint(new Point(25, 15), window)!.Value;
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); await Settle(window);
            Assert.Equal(LongUnit.Length, Math.Abs(input.SelectionEnd - input.SelectionStart)); Assert.False(view.IsDirty);
        });

    [Fact]
    public void FixedNumericEditStillSavesAndReopensThroughThePowerPipeline()
        => WithPower("Bolt", "<activetime>10</activetime>", false, async (view, window, path) =>
        {
            Field<TextBox>(view, "_activeTimeBox").Text = "12"; await Settle(window); Assert.True(view.IsDirty);
            var saving = view.SaveAsync(); string? error = null;
            for (var i = 0; !saving.IsCompleted && i < 500; i++)
            {
                if (window.OwnedWindows.OfType<Prompt>().FirstOrDefault() is { } prompt)
                { error = prompt.PromptText; prompt.Close(); }
                await Task.Delay(10);
            }
            Assert.True(await saving.WaitAsync(TimeSpan.FromSeconds(3)), error);
            Assert.Equal("12", XDocument.Load(path).Descendants("power").First().Element("activetime")!.Value);
            await view.SelectPowerAsync("B"); await view.SelectPowerAsync("A");
            Assert.Equal("12", Field<TextBox>(view, "_activeTimeBox").Text); Assert.False(view.IsDirty);
        });

    [Fact]
    public void SharedRainFactoryUsesDistinctSchemaActionAndReferenceRoles()
        => WithPower("Rain", $"<effect type='WorkRate' action='Gather' unittype='CustomType' relativity='BasePercent' amount='2'><target type='ProtoUnit'>{LongUnit}</target></effect>", false, async (view, window, _) =>
        {
            Field<TabControl>(view, "_powerTabs").SelectedIndex = 1; await Settle(window);
            var fields = view.GetLogicalDescendants().OfType<AutoCompleteBox>().Distinct().Where(box => box.IsEffectivelyVisible).ToArray();
            Assert.Equal(180, fields.Single(box => Equals(box.Tag, "type")).Width);
            Assert.Equal(150, fields.Single(box => Equals(box.Tag, "action")).Width);
            Assert.Equal(200, fields.Single(box => Equals(box.Tag, "target")).Width);
            Assert.False(view.IsDirty);
        });
}
