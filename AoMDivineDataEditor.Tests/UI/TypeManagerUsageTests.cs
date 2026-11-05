using System.Collections;
using System.Reflection;
using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Controls;
using AoMDivineDataEditor.Windows;
using Avalonia.Controls;
using Avalonia.Automation;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class TypeManagerUsageTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [Fact]
    public void UnitTypeUsageCountsTechnologySelectorsAndTacticsOncePerOwner()
    {
        using var fixture = new BaselineSafetyFixture();
        File.WriteAllText(fixture.TypePath, "<abstractunittypesmods><abstractunittype>SelectorType</abstractunittype><abstractunittype>UnusedType</abstractunittype></abstractunittypesmods>");
        File.WriteAllText(fixture.ProtoPath, "<protomods><unit name='SameUnit'><unittype>SelectorType</unittype><unittype>selectortype</unittype></unit></protomods>");
        var technology = fixture.Workspace.WriteXml("TestMod/game/data/gameplay/techtree_mods.xml", """
            <techtreemods><tech name="SelectorTech"><effects>
              <effect type="Data" subtype="Damage" amount="1" action="All"><target type="ProtoUnit">SelectorType</target></effect>
              <effect type="Data" subtype="MaximumRange" amount="1" action="All"><target type="ProtoUnit">selectortype</target></effect>
            </effects></tech><tech name="OtherTech"><effects>
              <effect type="Data" subtype="Damage" amount="1" action="All"><target type="ProtoUnit">SelectorType</target></effect>
            </effects></tech><metadata>UnusedType</metadata></techtreemods>
            """);
        var tactics = fixture.Workspace.WriteXml("TestMod/game/data/tactics/custom.tactics", "<tactics><tactic>Default<attacktype>SelectorType</attacktype><excludeattacktype>SelectorType</excludeattacktype></tactic></tactics>");
        var before = new[] { fixture.TypePath, fixture.ProtoPath, technology, tactics }.ToDictionary(path => path, File.ReadAllBytes);
        AvaloniaTestHost.Run(() =>
        {
            var host = fixture.CreateHost(false);
            host.Set("_barXmlRoot", XElement.Parse("<proto><unit name='SameUnit'><unittype>SelectorType</unittype></unit></proto>"));
            try
            {
                var usage = (Dictionary<string, int>)host.Call("GetUnitTypeUsageCounts")!;
                Assert.Equal(4, usage["SelectorType"]); // One Unit, two Technologies, one tactic.
                Assert.False(usage.ContainsKey("UnusedType")); // Definition/metadata are not usage.
                Assert.Equal(4, (int)host.Call("CountUnitTypeUsage", "selectortype")!);
                foreach (var file in before) Assert.Equal(file.Value, File.ReadAllBytes(file.Key));
            }
            finally { host.DisposeFixtureWindow(); }
        });
    }

    [Theory]
    [InlineData(true, 0, "Data.bar")]
    [InlineData(true, 2, "Data.bar · Used by 2")]
    [InlineData(false, 0, "Custom")]
    [InlineData(false, 2, "Used by 2")]
    public void BothTypeManagersUseMatchingChromeStatusAndRemoveStyle(bool original, int usage, string expected)
    {
        AvaloniaTestHost.Run(() =>
        {
            var units = new UnitTypeManagerWindow([new UnitTypeManagerItem("Type", original, usage)],
                _ => Task.FromResult(true), (_, _) => Task.FromResult(true), _ => Task.FromResult(true), _ => usage);
            var techs = new TechTypeManagerWindow([new TechTypeManagerItem("Type", original, new TechTypeUsage(usage, 0))],
                _ => Task.FromResult(true), (_, _) => Task.FromResult(true), _ => Task.FromResult(true), _ => new TechTypeUsage(usage, 0));
            try
            {
                Assert.Equal(units.Width, techs.Width);
                Assert.Equal(units.Height, techs.Height);
                Assert.Equal(units.MinWidth, techs.MinWidth);
                Assert.Equal(units.MinHeight, techs.MinHeight);
                Assert.IsType<ManagerListShell>(units.Content);
                Assert.IsType<ManagerListShell>(techs.Content);
                Grid Row(object manager)
                {
                    var item = ((IList)manager.GetType().GetField("_items", Private)!.GetValue(manager)!)[0]!;
                    return (Grid)manager.GetType().GetMethod("CreateRow", Private)!.Invoke(manager, [item])!;
                }
                var unitRow = Row(units);
                var techRow = Row(techs);
                Assert.Equal(unitRow.Margin, techRow.Margin);
                Assert.Equal(expected, unitRow.Children.OfType<TextBlock>().Last().Text);
                Assert.Equal(expected, techRow.Children.OfType<TextBlock>().Last().Text);
                if (original)
                {
                    Assert.Empty(unitRow.Children.OfType<Button>());
                    Assert.Empty(techRow.Children.OfType<Button>());
                }
                else
                {
                    var unitRemove = Assert.Single(unitRow.Children.OfType<Button>());
                    var techRemove = Assert.Single(techRow.Children.OfType<Button>());
                    Assert.Contains("remove-button", unitRemove.Classes);
                    Assert.Contains("remove-button", techRemove.Classes);
                    Assert.Equal(unitRemove.Margin, techRemove.Margin);
                    Assert.Equal("Delete unit type", ToolTip.GetTip(unitRemove));
                    Assert.Equal("Delete unit type", AutomationProperties.GetName(unitRemove));
                    Assert.Equal("Delete tech type", ToolTip.GetTip(techRemove));
                    Assert.Equal("Delete tech type", AutomationProperties.GetName(techRemove));
                }
            }
            finally { units.Close(); techs.Close(); }
        });
    }
}
