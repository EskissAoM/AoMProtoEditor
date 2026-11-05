using System.Collections;
using System.Reflection;
using System.Xml.Linq;
using AoMDivineDataEditor.Windows;
using Avalonia.Controls;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class UnitDiscardBrowserTests
{
    [Fact]
    public void DiscardRestoresBrowserLabelImmediatelyWithoutSaving()
    {
        using var fixture = new BaselineSafetyFixture();
        File.WriteAllText(fixture.ProtoPath, "<protomods><unit name='agga'/><unit name='Other'/></protomods>");
        var before = File.ReadAllBytes(fixture.ProtoPath);
        var host = AvaloniaTestHost.Run(() => fixture.CreateHost(selectGod: false));
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        object Field(string name) => typeof(ProtoEditorWindow).GetField(name, flags)!.GetValue(host)!;
        try
        {
            AvaloniaTestHost.Run(() =>
            {
                var kind = Enum.Parse(typeof(ProtoEditorWindow).GetNestedType("EditorEntityKind", BindingFlags.NonPublic)!, "Units");
                host.Set("_activeEntityKind", kind);
                host.Set("_suppressUnitListSelectionChange", true);
                ((Avalonia.Controls.Primitives.TabStrip)Field("_unitTabs")).SelectedIndex = 1;
                host.Set("_currentUnitName", "agga");
                host.Set("_allCurrentNames", new List<string> { "agga", "Other" });
                var box = new TextBox { Text = "aggabbbb" };
                ((IDictionary)Field("_fieldControls"))["__proto_unit_name"] = box;
                var tab = host.Call("CreateDocumentTab", true, kind, true, "agga")!;
                tab.GetType().GetProperty("SavedElement")!.SetValue(tab, XElement.Parse("<unit name='agga'/>"));
                host.Call("FilterUnitList");
                string[] Names() => ((IEnumerable)Field("_filteredUnitNames")).Cast<object>()
                    .Select(item => (string)item.GetType().GetProperty("DisplayName")!.GetValue(item)!).ToArray();
                Assert.Contains("aggabbbb", Names());
                host.Call("DiscardUnitDocumentChanges", tab);
                Assert.Contains("agga", Names());
                Assert.DoesNotContain("aggabbbb", Names());
                Assert.Contains("Other", Names());
                Assert.Equal("agga", box.Text);
                Assert.False((bool)host.Call("HasPendingAssetMoveChanges")!);
            });
            Assert.Equal(before, File.ReadAllBytes(fixture.ProtoPath));
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }
}
