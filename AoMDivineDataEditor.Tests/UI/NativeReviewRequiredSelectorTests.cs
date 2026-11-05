using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Avalonia.Controls;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed partial class TechnologyEditorWorkflowTests
{
    [Fact]
    public void NativeReviewClearingSavedTypeCountUnitIsDirtyAndCannotSave()
        => WithTechnologyPresentation("<prereqs><typecount unit='Unit' operator='gte' count='1' state='aliveState'/></prereqs>", async (editor, window, fixture) =>
        {
            var before = File.ReadAllBytes(fixture.TechtreePath);
            var source = XDocument.Load(fixture.TechtreePath).Descendants("typecount").Single();
            var field = Assert.IsType<AutoCompleteBox>(EditorValidationBindings.Find(editor,
                new LocatedValidationIssue("Unit", ValidationSourceLocation.From(source, "@unit"))));
            Assert.False(editor.IsDirty);
            field.Text = "";
            Assert.True(editor.IsDirty);
            await SettleTechnologyPresentation(window);
            var saving = editor.SaveAsync();
            await ValidationPopupTest.Dismiss(window, saving, "prerequisite");
            Assert.False(await saving);
            Assert.Equal(before, File.ReadAllBytes(fixture.TechtreePath));
            Assert.Contains("invalid-field", field.Classes);
            field.SelectedItem = "Unit";
            await AwaitTechnologyBuild(editor);
            Assert.True(await editor.SaveAsync().WaitAsync(TimeSpan.FromSeconds(5)));
        });
}

public sealed partial class PowerFieldPresentationTests
{
    [Fact]
    public void NativeReviewClearingSavedPowerTypeSurvivesTabChangeAndCannotSave()
        => WithPower("Bolt", "<activetime>10</activetime>", false, async (view, window, path) =>
        {
            var before = File.ReadAllBytes(path);
            Field<TabControl>(view, "_powerTabs").SelectedIndex = 1;
            await Settle(window);
            var field = Field<AutoCompleteBox>(view, "_powerTypeBox");
            field.Text = "";
            Assert.True(view.IsDirty);
            Field<TabControl>(view, "_powerTabs").SelectedIndex = 0;
            await Settle(window);
            Assert.True(string.IsNullOrEmpty(field.Text));
            var saving = view.SaveAsync();
            await ValidationPopupTest.Dismiss(window, saving, "Power Type");
            Assert.False(await saving);
            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.Contains("invalid-field", field.Classes);
            field.SelectedItem = "Bolt";
            await Settle(window);
            Assert.True(await view.SaveAsync().WaitAsync(TimeSpan.FromSeconds(5)));
            await view.SelectPowerAsync("B"); await view.SelectPowerAsync("A");
            Assert.Equal("Bolt", field.Text); Assert.False(view.IsDirty);
        });
}
