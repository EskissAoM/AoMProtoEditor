using System.Reflection;
using System.Xml.Linq;
using AoMDivineDataEditor.Windows;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed partial class TechnologyEditorWorkflowTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SelectorCatalogRefreshRetainsTechnologyXmlSessionAndDirtyText(bool editText)
    {
        using var fixture = new BaselineSafetyFixture();
        File.WriteAllText(fixture.ProtoPath, "<protomods><unit name='BaseUnit'/></protomods>");
        var path = fixture.Workspace.WriteText("TestMod/game/data/gameplay/techtree_mods.xml",
            "<techtreemods><tech name='A'><researchpoints>1</researchpoints><effects><effect type='Data' subtype='Hitpoints' amount='1'><target type='ProtoUnit'>BaseUnit</target></effect></effects></tech></techtreemods>");
        var before = File.ReadAllBytes(path);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var host = fixture.CreateHost(false);
            try
            {
                var (view, _) = AttachXmlTechnologyHost(host);
                await UnlockTechnologyXml(view);
                var selector = view.GetLogicalDescendants().OfType<AutoCompleteBox>().Single(box => box.Text == "BaseUnit");
                if (editText) Raw(view).Text = "<tech name='A'><unfinished>";
                var raw = Raw(view).Text;
                var hostType = typeof(ProtoEditorWindow);
                ((XElement)hostType.GetField("_modXmlRoot", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!)
                    .Add(new XElement("unit", new XAttribute("name", "CustomUnit")));
                host.Call("InvalidateSuggestionCaches", true);
                host.Call("RefreshTechnologyViewAfterTechTypeChanges");
                Assert.Same(view, hostType.GetField("_technologyView", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host));
                Assert.Equal(raw, Raw(view).Text); Assert.False(Raw(view).IsReadOnly);
                Assert.Equal(editText, view.IsDirty);
                Assert.Equal("BaseUnit", selector.Text); Assert.Contains("CustomUnit", selector.ItemsSource!.Cast<string>());
                Assert.Equal(before, File.ReadAllBytes(path));
            }
            finally { foreach (var child in host.OwnedWindows.ToArray()) child.Close(); host.DisposeFixtureWindow(); }
        });
    }
}
