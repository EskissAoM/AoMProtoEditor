using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed partial class UnitXmlEditingTests
{
    [Fact]
    [Trait("Category", TestCategories.Ui)]
    public void SavingCustomRawXmlInMixedDocumentPreservesOriginalPatchAndBaseline()
        => WithHost(async (fixture, host) =>
        {
            var baseline = XElement.Parse("<proto><unit name='A'><maxhitpoints>99</maxhitpoints><trainpoints>12</trainpoints></unit></proto>");
            host.Set("_barXmlRoot", baseline);
            var before = new XElement(XDocument.Load(fixture.ProtoPath).Root!.Element("unit")!);
            host.FindControl<TabStrip>("_unitTabs")!.SelectedIndex = 1;
            host.Call("BuildEditorPanel", "A", true); Dispatcher.UIThread.RunJobs();
            Assert.True(Raw(host).IsReadOnly);
            Assert.DoesNotContain("99", Raw(host).Text);
            host.Call("BuildEditorPanel", "B", false); Dispatcher.UIThread.RunJobs();
            await Unlock(host);
            Raw(host).Text = "<unit name='B'><maxhitpoints>21</maxhitpoints><future sibling='yes'/></unit>";
            host.Call("RefreshUnitList");
            host.Call("ToggleUnitGroup", Classes.AdditiveEntryKind.Custom);
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("21", Raw(host).Text); // Collapsing does not discard the active raw draft.
            host.Call("ToggleUnitGroup", Classes.AdditiveEntryKind.Custom);
            Assert.Empty(await Save(host));
            var saved = XDocument.Load(fixture.ProtoPath);
            Assert.True(XNode.DeepEquals(before, saved.Root!.Element("unit")));
            Assert.Equal("99", baseline.Element("unit")!.Element("maxhitpoints")!.Value);
            Assert.Equal("21", saved.Root.Elements("unit").Last().Element("maxhitpoints")!.Value);
            Assert.Null(saved.Root.Element("unit")!.Element("trainpoints"));
            host.Call("BuildEditorPanel", "B", false); Dispatcher.UIThread.RunJobs();
            Assert.Contains("21", Raw(host).Text);
        });
}
