using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Windows;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Xunit;

namespace AoMDivineDataEditor.Tests;

internal static class ValidationPopupTest
{
    internal static async Task CompleteWithoutPopup(Window owner, Task<bool> saving)
    {
        for (var i = 0; i < 500 && !saving.IsCompleted && !owner.OwnedWindows.OfType<Prompt>().Any(); i++) await Task.Delay(10);
        if (owner.OwnedWindows.OfType<Prompt>().FirstOrDefault() is { } prompt)
        {
            var message = prompt.PromptText; prompt.Close(); await saving.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Fail(message);
        }
        Assert.True(await saving.WaitAsync(TimeSpan.FromSeconds(5)));
    }
    internal static async Task Dismiss(Window owner, Task operation, string expected)
    {
        for (var attempt = 0; attempt < 300 && !owner.OwnedWindows.OfType<Prompt>().Any() && !operation.IsCompleted; attempt++) await Task.Delay(10);
        var popup = Assert.Single(owner.OwnedWindows.OfType<Prompt>());
        Assert.Contains(expected, popup.PromptText, StringComparison.OrdinalIgnoreCase);
        popup.Close();
        await operation.WaitAsync(TimeSpan.FromSeconds(5));
    }
}

public sealed partial class GodFieldPresentationTests
{
    [Fact]
    public void MinorGodUnknownTechFocusesItsSelectorAndDoesNotCallSaveUntilCorrected()
        => AvaloniaTestHost.RunAsync(async () =>
        {
            var calls = 0;
            var definition = new MinorGodDefinition("A", "ClassicalAgeA", false, XElement.Parse("<ClassicalAgeA portrait='icon.png' icon='icon.png'/>") );
            var window = new MinorGodEditorWindow(definition, ["ClassicalAgeA"], ["icon.png"],
                new IconPreviewService(() => null, () => null), (_, _) => { calls++; return Task.FromResult(true); });
            try
            {
                window.Show(); await Settle(window);
                var tech = Field<AutoCompleteBox>(window, "_techBox"); tech.Text = "UnknownTech";
                var saving = (Task)window.GetType().GetMethod("SaveAsync", Private)!.Invoke(window, null)!;
                await ValidationPopupTest.Dismiss(window, saving, "existing technology");
                Assert.Contains("invalid-field", tech.Classes); Assert.True(tech.IsKeyboardFocusWithin); Assert.Equal(0, calls);
                tech.Text = "ClassicalAgeA";
                Assert.DoesNotContain("invalid-field", tech.Classes);
                await ((Task)window.GetType().GetMethod("SaveAsync", Private)!.Invoke(window, null)!).WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Equal(1, calls);
            }
            finally { foreach (var popup in window.OwnedWindows.ToArray()) popup.Close(); window.Close(); }
        });

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void MajorGodErrorsRevealTheMappedFieldAndAllowCorrectionOrSwitch(bool shades)
    {
        using var workspace = new TestWorkspace();
        var source = Definition(shades ? "Shades" : "Properties");
        if (shades) source.Element("shades")!.Element("protounit")!.Remove();
        else source.Element("culture")!.Value = "UnknownCulture";
        var path = workspace.WriteText("game/data/gameplay/major_gods_mods.xml", new XElement("civsmods", source, new XElement("civ", new XElement("name", "B"))).ToString());
        var before = File.ReadAllBytes(path);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var view = new MajorGodEditorView([], path, cultureNames: ["Greek"], protoUnitNames: ["UnitA"], unitTypeNames: ["TypeA"]);
            var window = new Window { Content = view, Width = 1400, Height = 900 };
            try
            {
                window.Show(); view.SetModifiedMode(true); view.SelectMajorGod("A"); await Settle(window);
                var saving = view.CommitCurrentMajorGodAsync();
                await ValidationPopupTest.Dismiss(window, saving, shades ? "select a Unit" : "supported culture");
                Assert.False(await saving);
                await Settle(window);
                var invalid = Assert.Single(view.GetLogicalDescendants().OfType<Control>().Distinct(), control => control.Classes.Contains("invalid-field"));
                Assert.True(invalid.IsKeyboardFocusWithin);
                Assert.Equal(shades ? 2 : 0, view.FindControl<TabControl>("_majorGodTabs")!.SelectedIndex);
                Assert.Equal(before, File.ReadAllBytes(path));
                if (shades) Assert.IsType<AutoCompleteBox>(invalid).Text = "UnitA";
                else Assert.IsType<AutoCompleteBox>(invalid).Text = "Greek";
                await Settle(window);
                Assert.DoesNotContain("invalid-field", invalid.Classes);
                Assert.True(await view.CommitCurrentMajorGodAsync().WaitAsync(TimeSpan.FromSeconds(5)));
                view.SelectMajorGod("B"); await Settle(window);
                Assert.DoesNotContain(view.GetLogicalDescendants().OfType<Control>(), control => control.Classes.Contains("invalid-field"));
                Assert.Empty(window.OwnedWindows.OfType<Prompt>());
            }
            finally { foreach (var popup in window.OwnedWindows.ToArray()) popup.Close(); window.Close(); view.ReleaseMajorGodXmlEditing(); }
        });
    }
}

public sealed partial class CommandXmlEditingTests
{
    [Fact]
    public void MissingCommandNameRemainsEditableAndCanBeCorrectedAfterThePopup()
        => WithCommand(async (window, path, control) =>
        {
            var before = File.ReadAllBytes(path);
            var name = Field<TextBox>(window, "_nameBox");
            name.Text = "";
            var errors = await Save(window);
            Assert.Contains(errors, message => message.Contains("internal name", StringComparison.OrdinalIgnoreCase));
            Assert.Contains("invalid-field", name.Classes); Assert.True(name.IsKeyboardFocusWithin);
            Assert.Equal(0, control.Calls); Assert.Equal(before, File.ReadAllBytes(path));
            name.Text = "CorrectedCommand";
            Assert.DoesNotContain("invalid-field", name.Classes);
            Assert.Empty(await Save(window)); Assert.Equal(1, control.Calls);
        });
}

public sealed partial class PowerFieldPresentationTests
{
    [Fact]
    public void PowerNumericGuardRevealsPropertiesAndCorrectionSavesWithoutChangingItsRule()
        => WithPower("Bolt", "<activetime>bad</activetime>", false, async (view, window, path) =>
        {
            var before = File.ReadAllBytes(path);
            Field<TabControl>(view, "_powerTabs").SelectedIndex = 1;
            var saving = view.SaveAsync();
            await ValidationPopupTest.Dismiss(window, saving, "activetime");
            Assert.False(await saving); await Settle(window);
            var field = Field<TextBox>(view, "_activeTimeBox");
            Assert.Contains("invalid-field", field.Classes); Assert.True(field.IsKeyboardFocusWithin);
            Assert.Equal(0, Field<TabControl>(view, "_powerTabs").SelectedIndex);
            Assert.Equal(before, File.ReadAllBytes(path));
            field.Text = "2"; await Settle(window);
            Assert.DoesNotContain("invalid-field", field.Classes);
            Assert.True(await view.SaveAsync().WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal("2", XDocument.Load(path).Root!.Elements("power").Single(power => (string?)power.Attribute("name") == "A").Element("activetime")!.Value);
        });
}

public sealed partial class TacticsStartupTests
{
    [Fact]
    public void MissingTacticNameRevealsTheTacticTabAndCorrectionSaves()
        => WithStandaloneXml(false, async (_, _, child, path, control) =>
        {
            var before = File.ReadAllBytes(path);
            var tabs = child.GetLogicalDescendants().OfType<TabControl>().Distinct().Single(tabs =>
                tabs.Items.OfType<TabItem>().Any(tab => Equals(tab.Header, "Tactic")));
            tabs.SelectedIndex = 1;
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle); child.UpdateLayout();
            var name = Assert.Single(child.GetLogicalDescendants().OfType<TextBox>().Distinct(), field => field.Text == "Default");
            name.Text = "";
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
            tabs.SelectedIndex = 0;
            var errors = await SaveStandaloneChecked(child, false);
            Assert.Contains(errors, error => error.Contains("Every tactic requires a name", StringComparison.Ordinal));
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
            child.UpdateLayout(); await Task.Delay(25);
            Assert.Equal(1, tabs.SelectedIndex); Assert.True(name.IsKeyboardFocusWithin);
            Assert.Contains("invalid-field", name.Classes);
            Assert.Equal(before, File.ReadAllBytes(path)); Assert.Equal(0, control.Calls);
            name.Text = "CorrectedTactic";
            Assert.DoesNotContain("invalid-field", name.Classes);
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
            await Task.Delay(25);
            Assert.Empty(await SaveStandaloneChecked(child, false)); Assert.Equal(1, control.Calls);
        });
}
