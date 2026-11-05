using System.Reflection;
using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Controls;
using AoMDivineDataEditor.Windows;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class CompactFormWrappingTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CompactFormsWrapWholeGroupsAndKeepFooterAndSavePolicies(bool minorGod, bool builtIn)
    {
        using var workspace = new TestWorkspace();
        var source = minorGod
            ? XElement.Parse("<ClassicalAgeTest future='keep' portrait='portrait.png' icon='icon.png'/>")
            : XElement.Parse("<culture future='keep'><name>Test</name><displaynameid>STR_NAME</displaynameid><compendiumtopic>Old</compendiumtopic><unknown keep='yes'/></culture>");
        var original = new XElement(source);
        var path = workspace.GetPath("saved.xml");
        AvaloniaTestHost.RunAsync(async () =>
        {
            var calls = 0;
            Task<bool> Commit(XElement element) { calls++; element.Save(path); return Task.FromResult(true); }
            Window editor = minorGod
                ? new MinorGodEditorWindow(new MinorGodDefinition("Test", "ClassicalAgeTest", builtIn, source),
                    ["ClassicalAgeTest", "ClassicalAgeOther"], ["portrait.png", "icon.png"],
                    new IconPreviewService(() => null, () => null), (_, element) => Commit(element))
                : new CultureEditorWindow(new CultureDefinition("Test", builtIn, source), [],
                    _ => Task.FromResult<string?>("Display"), (_, element, _) => Commit(element));
            try
            {
                editor.Show();
                foreach (var narrow in new[] { false, true, false })
                {
                    editor.Width = narrow ? editor.MinWidth : minorGod ? 900 : 820;
                    editor.Height = editor.MinHeight;
                    await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
                    editor.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                    var scroll = editor.GetLogicalDescendants().OfType<ScrollViewer>()
                        .Single(viewer => viewer.Content is Grid or StackPanel);
                    Assert.Equal(ScrollBarVisibility.Disabled, scroll.HorizontalScrollBarVisibility);
                    Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + .5);
                    Control first = minorGod ? Field<AssetPathEditor>(editor, "_portraitEditor") : Field<TextBox>(editor, "_topicBox");
                    Control second = minorGod ? Field<AssetPathEditor>(editor, "_iconEditor") : Field<TextBox>(editor, "_imageBox");
                    foreach (var control in new[] { first, second })
                    {
                        var group = Assert.IsType<StackPanel>(control.Parent);
                        Assert.Single(group.Children.OfType<TextBlock>());
                        Assert.Same(control, group.Children[1]);
                        Assert.IsType<WrapPanel>(group.Parent);
                        Assert.Equal(8, group.Spacing);
                        Assert.Equal(12, group.Margin.Right);
                        var p = group.TranslatePoint(default, scroll)!.Value;
                        Assert.InRange(p.X, -.5, scroll.Viewport.Width);
                        Assert.True(p.X + group.Bounds.Width <= scroll.Viewport.Width + .5);
                        Assert.Equal(!builtIn, control.IsEnabled);
                    }
                    var firstPoint = first.TranslatePoint(default, editor)!.Value;
                    var secondPoint = second.TranslatePoint(default, editor)!.Value;
                    if (minorGod)
                    {
                        var preview = Field<IconPreviewControl>(editor, "_preview");
                        var previewPoint = preview.TranslatePoint(default, editor)!.Value;
                        Assert.Equal(narrow, previewPoint.Y > firstPoint.Y + first.Bounds.Height);
                    }
                    else
                    {
                        var firstGroup = Assert.IsType<StackPanel>(first.Parent);
                        var secondGroup = Assert.IsType<StackPanel>(second.Parent);
                        var wrap = Assert.IsType<WrapPanel>(firstGroup.Parent);
                        Assert.Equal(firstGroup.DesiredSize.Width + secondGroup.DesiredSize.Width > wrap.Bounds.Width + .5,
                            secondPoint.Y > firstPoint.Y + first.Bounds.Height);
                    }
                    var close = editor.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, builtIn ? "Close" : "Cancel"));
                    var closePoint = close.TranslatePoint(default, editor)!.Value;
                    Assert.InRange(closePoint.Y, 0, editor.ClientSize.Height - close.Bounds.Height);
                    Assert.DoesNotContain(close.GetLogicalAncestors(), ancestor => ReferenceEquals(ancestor, scroll));
                    Assert.Equal(builtIn ? 0 : 1, editor.GetLogicalDescendants().OfType<Button>().Count(button => Equals(button.Content, "Save")));
                    Assert.Equal(0, calls);
                    Assert.True(XNode.DeepEquals(original, source));
                }
                if (!builtIn)
                {
                    if (minorGod) Field<AutoCompleteBox>(editor, "_techBox").Text = "ClassicalAgeOther";
                    else Field<TextBox>(editor, "_topicBox").Text = "Changed";
                    await ((Task)editor.GetType().GetMethod("SaveAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor, null)!).WaitAsync(TimeSpan.FromSeconds(5));
                    Assert.Equal(1, calls);
                    var saved = XElement.Load(path);
                    Assert.Equal("keep", (string?)saved.Attribute("future"));
                    if (minorGod) Assert.Equal("ClassicalAgeOther", saved.Name.LocalName);
                    else { Assert.Equal("Changed", saved.Element("compendiumtopic")!.Value); Assert.NotNull(saved.Element("unknown")); }
                    Assert.True(XNode.DeepEquals(original, source));
                }
                else Assert.False(File.Exists(path));
            }
            finally { editor.Close(); }
        });
    }

    private static T Field<T>(object owner, string name) => (T)owner.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
}
