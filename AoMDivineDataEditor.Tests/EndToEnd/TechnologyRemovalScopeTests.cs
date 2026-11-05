using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed partial class TechnologyEditorWorkflowTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void TechnologyCardRemovalKeepsTheOtherCardAndPreservedXml(bool effect)
    {
        using var fixture = new TechnologyEditorFixture(
            "<techtreemods><tech name='A'><prereqs><techstatus status='Active'>B</techstatus></prereqs>" +
            "<effects><effect type='ConsoleCommand'>test_command</effect></effects><future><child/></future></tech><tech name='B'/></techtreemods>");
        AvaloniaTestHost.RunAsync(async () =>
        {
            var editor = fixture.CreateEditor();
            await InXmlWindow(editor, async _ =>
            {
                editor.SetModifiedMode(true); editor.SelectTechnology("A"); await AwaitTechnologyBuild(editor);
                var panel = GetPrivateField<StackPanel>(editor, effect ? "_effectsPanel" : "_prereqsPanel");
                var remove = Assert.Single(panel.GetLogicalDescendants().OfType<Button>(), button => button.Classes.Contains("remove-card"));
                Assert.Equal("×", remove.Content);
                remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await AwaitTechnologyBuild(editor);
                Assert.True(editor.IsDirty);
                Assert.DoesNotContain(panel.GetLogicalDescendants().OfType<Button>(), button => button.Classes.Contains("remove-card"));
                await SaveXmlChecked(editor); await AwaitTechnologyBuild(editor);
                var saved = XDocument.Load(fixture.TechtreePath).Root!.Elements("tech").First();
                Assert.Null(saved.Element(effect ? "effects" : "prereqs"));
                Assert.NotNull(saved.Element(effect ? "prereqs" : "effects"));
                Assert.NotNull(saved.Element("future")!.Element("child")); Assert.False(editor.IsDirty);
            });
        });
    }
}
