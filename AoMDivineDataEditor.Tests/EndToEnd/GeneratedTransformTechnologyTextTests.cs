using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.GameData;
using AoMDivineDataEditor.Windows;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed partial class TechnologyEditorWorkflowTests
{
    [Theory]
    [InlineData("displaynameid", "Display Name")]
    [InlineData("rollovertextid", "Rollover")]
    public void MissingTransformTechnologyTextCanBeEditedSavedReopenedAndDiscarded(string tag, string label)
    {
        const string name = "MilitaryMineToMilitaryCampTower";
        using var fixture = new TechnologyEditorFixture("<techtreemods>" + GeneratedTowerTechnology(false, false) + "</techtreemods>");
        var originalXml = File.ReadAllBytes(fixture.TechtreePath);
        var originalStrings = File.ReadAllBytes(fixture.StringsPath);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var editor = fixture.CreateEditor(
                saveStringsAsync: (updates, removals) =>
                {
                    File.WriteAllText(fixture.StringsPath, StringTableDocument.Parse(File.ReadAllText(fixture.StringsPath))
                        .Apply(updates, removals, StringTableSection.Technologies));
                    return Task.CompletedTask;
                },
                resolveStringAsync: id => Task.FromResult(StringTableDocument.Parse(File.ReadAllText(fixture.StringsPath))
                    .ToDictionary().GetValueOrDefault(id)));
            await InXmlWindow(editor, async _ =>
            {
                editor.SetModifiedMode(true); editor.SelectTechnology(name); await AwaitTechnologyBuild(editor);
                var box = TransformTechnologyTextBox(editor, label);
                Assert.True(box.IsEnabled); Assert.True(string.IsNullOrEmpty(box.Text));
                Assert.False(editor.IsDirty);
                Assert.Equal(originalXml, File.ReadAllBytes(fixture.TechtreePath));
                Assert.True(await SaveXmlChecked(editor));
                Assert.False(editor.IsDirty);
                Assert.True(XNode.DeepEquals(XDocument.Parse(System.Text.Encoding.UTF8.GetString(originalXml)),
                    XDocument.Load(fixture.TechtreePath)));
                Assert.Equal(originalStrings, File.ReadAllBytes(fixture.StringsPath));

                await AwaitTechnologyBuild(editor);
                box = TransformTechnologyTextBox(editor, label);
                box.Text = "New transform text";
                await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
                Assert.True(editor.IsDirty);
                Assert.True(await SaveXmlChecked(editor)); await AwaitTechnologyBuild(editor);
                var saved = FindTechnology(XDocument.Load(fixture.TechtreePath), name);
                var id = TechnologyStringIdPolicy.BuildId(name, tag);
                Assert.Equal(id, saved.Element(tag)?.Value);
                Assert.Null(saved.Element(tag == "displaynameid" ? "rollovertextid" : "displaynameid"));
                Assert.Equal("New transform text", StringTableDocument.Parse(File.ReadAllText(fixture.StringsPath)).ToDictionary()[id]);
                editor.SelectTechnology(null); editor.SelectTechnology(name); await AwaitTechnologyBuild(editor);
                box = TransformTechnologyTextBox(editor, label);
                Assert.Equal("New transform text", box.Text); Assert.False(editor.IsDirty);
                var savedBytes = File.ReadAllBytes(fixture.TechtreePath);
                var savedStrings = File.ReadAllBytes(fixture.StringsPath);
                box.Text = "Discard this";
                await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
                editor.DiscardTechnologyChanges(name, editor.LoadSavedTechnologyElement(name)); await AwaitTechnologyBuild(editor);
                Assert.False(editor.IsDirty);
                Assert.Equal("New transform text", TransformTechnologyTextBox(editor, label).Text);
                Assert.Equal(savedBytes, File.ReadAllBytes(fixture.TechtreePath));
                Assert.Equal(savedStrings, File.ReadAllBytes(fixture.StringsPath));
            });
        });
    }

    [Fact]
    public void MissingTransformTechnologyTextDoesNotOverwriteAnExistingStringId()
    {
        const string name = "MilitaryMineToMilitaryCampTower";
        var id = TechnologyStringIdPolicy.BuildId(name, "displaynameid");
        using var fixture = new TechnologyEditorFixture("<techtreemods>" + GeneratedTowerTechnology(false, false) + "</techtreemods>",
            $"ID = \"{id}\" ; Str = \"Keep unrelated text\"\n");
        var xmlBefore = File.ReadAllBytes(fixture.TechtreePath);
        var stringsBefore = File.ReadAllBytes(fixture.StringsPath);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var editor = fixture.CreateEditor(saveStringsAsync: (_, _) => throw new InvalidOperationException("Writer must not run."));
            await InXmlWindow(editor, async owner =>
            {
                editor.SetModifiedMode(true); editor.SelectTechnology(name); await AwaitTechnologyBuild(editor);
                TransformTechnologyTextBox(editor, "Display Name").Text = "New name";
                await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
                var saving = editor.SaveAsync();
                var popup = await XmlPopup(owner);
                Assert.Contains("already exists", popup.PromptText, StringComparison.Ordinal);
                popup.Close(); Assert.False(await saving.WaitAsync(TimeSpan.FromSeconds(3)));
                Assert.Equal(xmlBefore, File.ReadAllBytes(fixture.TechtreePath));
                Assert.Equal(stringsBefore, File.ReadAllBytes(fixture.StringsPath));
            });
        });
    }

    private static TextBox TransformTechnologyTextBox(TechnologyEditorView editor, string label)
    {
        var caption = editor.GetVisualDescendants().OfType<TextBlock>().Single(block => block.Text == label);
        return caption.GetVisualParent()!.GetVisualDescendants().OfType<TextBox>().Single();
    }
}
