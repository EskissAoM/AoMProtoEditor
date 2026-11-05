using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Windows;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed partial class TechnologyEditorWorkflowTests
{
    private const string OrderFixture = "<techtreemods><tech name='A'><cost resourcetype='Food'>5</cost><flag>HideAllNotifications</flag><researchpoints>1</researchpoints><status>UNOBTAINABLE</status></tech><tech name='B'><researchpoints>1</researchpoints><status>UNOBTAINABLE</status></tech></techtreemods>";
    private static string[] TechnologyOrder(XElement tech) => tech.Elements().Select(e => e.Name.LocalName).ToArray();

    [Fact]
    public void StructuredTechnologyOrderMatchesPreviewDiskAndReopenWithoutRewritingSibling()
    {
        using var fixture = new TechnologyEditorFixture(OrderFixture);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var editor = fixture.CreateEditor();
            await InXmlWindow(editor, async _ =>
            {
                editor.SetModifiedMode(true); editor.SelectTechnology("A"); await AwaitTechnologyBuild(editor);
                Assert.False(editor.IsDirty);
                var current = FindTechnology(GetDraftDocument(editor), "A");
                current.Element("researchpoints")!.Value = "2";
                InvokePrivate(editor, "MarkDirty"); InvokePrivate(editor, "UpdatePreview");
                string[] expected = ["status", "researchpoints", "cost", "flag"];
                Assert.Equal(expected, TechnologyOrder(XElement.Parse(Raw(editor).Text)));
                Assert.True(await SaveXmlChecked(editor));
                var disk = XDocument.Load(fixture.TechtreePath);
                Assert.Equal(expected, TechnologyOrder(FindTechnology(disk, "A")));
                Assert.True(XNode.DeepEquals(FindTechnology(XDocument.Parse(OrderFixture), "B"), FindTechnology(disk, "B")));
                var once = File.ReadAllBytes(fixture.TechtreePath);
                Assert.True(await SaveXmlChecked(editor));
                Assert.Equal(once, File.ReadAllBytes(fixture.TechtreePath));
                var reopened = fixture.CreateEditor(); reopened.SetModifiedMode(true); reopened.SelectTechnology("A"); await AwaitTechnologyBuild(reopened);
                Assert.False(reopened.IsDirty);
                Assert.Equal(expected, TechnologyOrder(XElement.Parse(Raw(reopened).Text)));
            });
        });
    }

    [Fact]
    public void RawTechnologyManualOrderSurvivesSaveThenLaterStructuredSaveUsesFormOrder()
    {
        using var fixture = new TechnologyEditorFixture(OrderFixture);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var editor = fixture.CreateEditor();
            await InXmlWindow(editor, async _ =>
            {
                await UnlockTechnologyXml(editor);
                const string raw = "<tech name='A'><flag>HideAllNotifications</flag><cost resourcetype='Food'>6</cost><status>UNOBTAINABLE</status><researchpoints>2</researchpoints></tech>";
                Raw(editor).Text = raw;
                Assert.True(await SaveXmlChecked(editor));
                Assert.Equal(TechnologyOrder(XElement.Parse(raw)), TechnologyOrder(FindTechnology(XDocument.Load(fixture.TechtreePath), "A")));
                FindTechnology(GetDraftDocument(editor), "A").Element("researchpoints")!.Value = "3";
                InvokePrivate(editor, "MarkDirty");
                Assert.True(await SaveXmlChecked(editor));
                Assert.Equal(new[] { "status", "researchpoints", "cost", "flag" }, TechnologyOrder(FindTechnology(XDocument.Load(fixture.TechtreePath), "A")));
            });
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StructuredTechnologyPreservedSpaceKeepsItsOriginalOrder(bool inherited)
    {
        var xml = inherited ? OrderFixture.Replace("<techtreemods>", "<techtreemods xml:space='preserve'>")
            : OrderFixture.Replace("<tech name='A'>", "<tech name='A' xml:space='preserve'>");
        using var fixture = new TechnologyEditorFixture(xml);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var editor = fixture.CreateEditor();
            await InXmlWindow(editor, async _ =>
            {
                editor.SetModifiedMode(true); editor.SelectTechnology("A"); await AwaitTechnologyBuild(editor);
                FindTechnology(GetDraftDocument(editor), "A").Element("researchpoints")!.Value = "2";
                InvokePrivate(editor, "MarkDirty");
                Assert.True(await SaveXmlChecked(editor));
                Assert.Equal(TechnologyOrder(FindTechnology(XDocument.Parse(xml), "A")), TechnologyOrder(FindTechnology(XDocument.Load(fixture.TechtreePath), "A")));
            });
        });
    }
}
