using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class ValidationSourceTests
{
    [Fact]
    public void TechnologyLocationsIdentifyTheExactRepeatedChipAcrossClonedDocuments()
    {
        var document = XDocument.Parse("<techtreemods><tech name='A'><effects><effect type='RandomTech' select='1'><Tech>Known</Tech><Tech>Missing</Tech></effect></effects></tech></techtreemods>");
        var before = document.ToString(SaveOptions.DisableFormatting);
        var catalogs = new TechnologySaveCatalogs([], ["RandomTech"], ["A", "Known"], [], [], [], [], [], [], [], [], [], []);
        var result = TechnologySaveValidator.Validate(document, catalogs);
        Assert.False(result.IsValid);
        var issue = Assert.Single(result.Issues);
        Assert.Equal(result.Errors[0], issue.Message);
        Assert.Equal("A", issue.Location!.Record);
        Assert.Equal("effects[1]/effect[1]/Tech[2]", issue.Location.Path);
        var clone = XDocument.Parse(before);
        var missing = clone.Descendants("Tech").Last();
        Assert.Equal(issue.Location, ValidationSourceLocation.From(missing));
        Assert.Equal(before, document.ToString(SaveOptions.DisableFormatting));
    }

    [Fact]
    public void UnmappedTechnologyShapeFailureDoesNotPretendItsNameOrBodyIsTheInvalidField()
    {
        var document = XDocument.Parse("<techtreemods><tech name='A'><effects><effect type='Data' subtype='Damage' amount='1' relativity='Absolute' allactions='1'/></effects></tech></techtreemods>");
        var catalogs = new TechnologySaveCatalogs(["Damage"], [], ["A"], [], [], [], [], [], [], [], [], [], []);
        var result = TechnologySaveValidator.Validate(document, catalogs);
        var issue = Assert.Single(result.Issues);
        Assert.Contains("exactly one <target>", issue.Message);
        Assert.Null(issue.Location);
    }

    [Fact]
    public void PowerLocationsRetainNumericRulesAndUseTheActualSourceSlot()
    {
        var document = XDocument.Parse("<powersmod><power name='A' type='Bolt'><radius>-1</radius></power></powersmod>");
        var before = document.ToString(SaveOptions.DisableFormatting);
        var result = PowerSaveValidator.Validate(document, new PowerSaveCatalogs(["Bolt"]));
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Issues);
        Assert.Equal(ValidationSourceLocation.From(document.Descendants("radius").Single()), result.Issues[0].Location);
        Assert.Equal(result.Errors.ToArray(), result.Issues.Select(issue => issue.Message).ToArray());
        Assert.Equal(before, document.ToString(SaveOptions.DisableFormatting));
    }

    [Fact]
    public void RepeatedRootLevelChipBindingMatchesItsChildWithoutAFalseLeadingSeparator()
        => AvaloniaTestHost.Run(() =>
        {
            var record = XElement.Parse("<tech name='A'><techtype>Known</techtype><techtype>Missing</techtype></tech>");
            var picker = EditorValidationBindings.Bind(new AutoCompleteBox(), record, "techtype", repeated: true);
            var root = new StackPanel { Children = { picker } };
            var location = ValidationSourceLocation.From(new XElement(record).Elements("techtype").Last());
            Assert.Same(picker, EditorValidationBindings.Find(root, new("Error", location)));
            Assert.Null(EditorValidationBindings.Find(root, new("Error", location with { Record = "Other" })));
        });

    [Fact]
    public void BindingNeverSelectsAnotherRecordOrAnotherRepeatedAttribute()
        => AvaloniaTestHost.Run(() =>
        {
            var record = XElement.Parse("<tech name='A'><effects><effect amount='1'/><effect amount='bad'/></effects></tech>");
            var fields = record.Descendants("effect").ToArray();
            var first = EditorValidationBindings.Bind(new TextBox(), fields[0], "@amount");
            var second = EditorValidationBindings.Bind(new TextBox(), fields[1], "@amount");
            var root = new StackPanel { Children = { first, second } };
            var location = ValidationSourceLocation.From(new XElement(record).Descendants("effect").Last(), "@amount");
            Assert.Same(second, EditorValidationBindings.Find(root, new("Error", location)));
            Assert.Null(EditorValidationBindings.Find(root, new("Error", location with { Record = "Other" })));
            Assert.Null(EditorValidationBindings.Find(root, new("Document error", null)));
        });
}

public sealed partial class TechnologyEditorWorkflowTests
{
    [Fact]
    public void InvalidRandomTechChipNavigatesAfterPopupAndCorrectionCanSave()
        => WithTechnologyPresentation("<effects><effect type='RandomTech' select='1'><Tech>MissingTechnology</Tech></effect></effects>", async (editor, window, fixture) =>
        {
            var before = File.ReadAllBytes(fixture.TechtreePath);
            var saving = editor.SaveAsync();
            for (var attempt = 0; attempt < 200 && !window.OwnedWindows.OfType<AoMDivineDataEditor.Windows.Prompt>().Any(); attempt++) await Task.Delay(10);
            var popup = Assert.Single(window.OwnedWindows.OfType<AoMDivineDataEditor.Windows.Prompt>());
            Assert.Contains("MissingTechnology", popup.PromptText);
            popup.Close(); Assert.False(await saving.WaitAsync(TimeSpan.FromSeconds(5)));
            await SettleTechnologyPresentation(window);
            var panel = GetPrivateField<StackPanel>(editor, "_effectsPanel");
            var picker = Assert.Single(panel.GetLogicalDescendants().OfType<AutoCompleteBox>(), control => control.Classes.Contains("invalid-field"));
            Assert.True(picker.IsKeyboardFocusWithin);
            Assert.Equal(2, GetPrivateField<Control>(editor, "_technologyForm").GetLogicalDescendants().OfType<TabControl>().Single().SelectedIndex);
            Assert.Equal(before, File.ReadAllBytes(fixture.TechtreePath));
            var remove = Assert.Single(panel.GetLogicalDescendants().OfType<Button>(), button => button.Classes.Contains("chip-remove-button"));
            remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.DoesNotContain("invalid-field", picker.Classes);
            picker.SelectedItem = "B";
            await SettleTechnologyPresentation(window);
            Assert.True(await editor.SaveAsync().WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Contains("<Tech>B</Tech>", File.ReadAllText(fixture.TechtreePath));
        });
}
