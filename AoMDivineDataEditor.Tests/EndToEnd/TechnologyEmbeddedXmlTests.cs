using System.Xml.Linq;
using AoMDivineDataEditor.Windows;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using AvaloniaEdit;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed partial class TechnologyEditorWorkflowTests
{
    private static TextEditor[] TechnologySnippets(TechnologyEditorView editor)
        => new[] { "_propertiesPanel", "_prereqsPanel", "_effectsPanel" }
            .SelectMany(name => GetPrivateField<StackPanel>(editor, name)
                .GetLogicalDescendants().OfType<TextEditor>()).ToArray();

    [Theory]
    [InlineData("<future><child marker='old'/></future>", "future")]
    [InlineData("<prereqs><futurePrereq marker='old'/></prereqs>", "futurePrereq")]
    [InlineData("<cost resourcetype='Food' marker='old'>2</cost>", "cost")]
    [InlineData("<effects><futureContainer marker='old'/></effects>", "futureContainer")]
    [InlineData("<effects><effect type='FutureEffect' marker='old'><payload/></effect></effects>", "effect")]
    public void EmbeddedTechnologyXmlRefreshesThroughParentSaveDiscardAndRemoval(string fragment, string tag)
    {
        using var fixture = new TechnologyEditorFixture(
            $"<techtreemods><tech name='A'>{fragment}</tech><tech name='B'/></techtreemods>");
        AvaloniaTestHost.RunAsync(async () =>
        {
            var editor = fixture.CreateEditor();
            await InXmlWindow(editor, async _ =>
            {
                editor.SetModifiedMode(true); editor.SelectTechnology("A"); await AwaitTechnologyBuild(editor);
                var snippet = Assert.Single(TechnologySnippets(editor));
                Assert.True(snippet.IsReadOnly); Assert.False(snippet.IsTabStop); Assert.False(editor.IsDirty);
                Assert.Equal("old", (string?)XElement.Parse(snippet.Text).Attribute("marker") ??
                    (string?)XElement.Parse(snippet.Text).Descendants().Single().Attribute("marker"));

                await UnlockTechnologyXml(editor);
                Raw(editor).Text = $"<tech name='A'>{fragment.Replace("'old'", "'saved'", StringComparison.Ordinal)}</tech>";
                await SaveXmlChecked(editor); await AwaitTechnologyBuild(editor);
                Assert.False(editor.IsDirty); Assert.True(Raw(editor).IsReadOnly);
                snippet = Assert.Single(TechnologySnippets(editor));
                Assert.True(snippet.IsReadOnly); Assert.Contains("saved", snippet.Text); Assert.DoesNotContain("old", snippet.Text);
                Assert.Contains("saved", XDocument.Load(fixture.TechtreePath).Descendants(tag).Single().ToString());

                await UnlockTechnologyXml(editor);
                Raw(editor).Text = Raw(editor).Text.Replace("saved", "discarded", StringComparison.Ordinal);
                editor.DiscardTechnologyChanges("A", editor.LoadSavedTechnologyElement("A"));
                editor.SelectTechnology("B"); await AwaitTechnologyBuild(editor);
                Assert.Empty(TechnologySnippets(editor));
                editor.SelectTechnology("A"); await AwaitTechnologyBuild(editor);
                Assert.False(editor.IsDirty); Assert.True(Raw(editor).IsReadOnly);
                Assert.Contains("saved", Assert.Single(TechnologySnippets(editor)).Text);
                Assert.DoesNotContain("discarded", Raw(editor).Text);

                await UnlockTechnologyXml(editor); Raw(editor).Text = "<tech name='A'/>";
                await SaveXmlChecked(editor); await AwaitTechnologyBuild(editor);
                Assert.Empty(TechnologySnippets(editor));
                Assert.Empty(XDocument.Load(fixture.TechtreePath).Descendants(tag));
                editor.SelectTechnology("B"); editor.SelectTechnology("A"); await AwaitTechnologyBuild(editor);
                Assert.Empty(TechnologySnippets(editor)); Assert.False(editor.IsDirty);
            });
        });
    }

    [Fact]
    public void TechnologyFallbackAndStructuredEffectsFollowParentXmlAndInlineFocusCannotCommit()
    {
        using var fixture = new TechnologyEditorFixture(
            "<techtreemods><tech name='A'><effects><effect type='FutureEffect' marker='old'/></effects></tech></techtreemods>");
        AvaloniaTestHost.RunAsync(async () =>
        {
            var editor = fixture.CreateEditor();
            await InXmlWindow(editor, async _ =>
            {
                editor.SetModifiedMode(true); editor.SelectTechnology("A"); await AwaitTechnologyBuild(editor);
                var before = Raw(editor).Text;
                // Programmatic text changes bypass IsReadOnly: focus loss must still have no commit path.
                var snippet = Assert.Single(TechnologySnippets(editor));
                snippet.Text = "<effect type='FutureEffect' marker='inline'/>";
                snippet.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(InputElement.LostFocusEvent));
                await AwaitTechnologyBuild(editor);
                Assert.False(editor.IsDirty); Assert.Equal(before, Raw(editor).Text);

                await UnlockTechnologyXml(editor);
                Raw(editor).Text = "<tech name='A'><effects><effect type='ConsoleCommand'>test_command</effect></effects></tech>";
                await SaveXmlChecked(editor); await AwaitTechnologyBuild(editor);
                Assert.Empty(TechnologySnippets(editor)); Assert.Contains("test_command", Raw(editor).Text);

                await UnlockTechnologyXml(editor);
                Raw(editor).Text = "<tech name='A'><effects><effect type='FutureEffect' marker='returned'/></effects></tech>";
                await SaveXmlChecked(editor); await AwaitTechnologyBuild(editor);
                Assert.Contains("returned", Assert.Single(TechnologySnippets(editor)).Text);
                Assert.True(Assert.Single(TechnologySnippets(editor)).IsReadOnly); Assert.False(editor.IsDirty);
            });
        });
    }

    [Fact]
    public void OriginalTechnologySnippetsRemainReadOnly()
    {
        using var fixture = new TechnologyEditorFixture("<techtreemods/>");
        AvaloniaTestHost.RunAsync(async () =>
        {
            var editor = fixture.CreateEditor(originalDocuments:
                [XDocument.Parse("<techtree><tech name='Original'><effects><effect type='FutureEffect'/></effects><future><child/></future></tech></techtree>")]);
            await InXmlWindow(editor, async _ =>
            {
                editor.SetModifiedMode(false); editor.SelectTechnology("Original"); await AwaitTechnologyBuild(editor);
                Assert.Equal(2, TechnologySnippets(editor).Length);
                Assert.All(TechnologySnippets(editor), snippet => Assert.True(snippet.IsReadOnly));
                Assert.True(Raw(editor).IsReadOnly); Assert.False(editor.IsDirty);
            });
        });
    }
}
