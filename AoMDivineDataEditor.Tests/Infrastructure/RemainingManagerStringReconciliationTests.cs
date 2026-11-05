using AoMDivineDataEditor.Windows;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class RemainingManagerStringReconciliationTests
{
    [Theory]
    [InlineData("UnitType")]
    [InlineData("TechType")]
    [InlineData("Tactics")]
    [InlineData("OrphanMinorGod")]
    public void ManagersWithoutGeneratedStringOwnershipLeaveStringTableUntouched(string kind)
    {
        using var fixture = new BaselineSafetyFixture();
        var file = kind switch
        {
            "UnitType" => "abstract_unit_types_mods.xml",
            "TechType" => "tech_types_mods.xml",
            "Tactics" => "tactics/Unused.tactics",
            _ => "minor_gods_mods.xml"
        };
        var xml = kind switch
        {
            "UnitType" => "<abstractunittypesmods><abstractunittype>Unused</abstractunittype></abstractunittypesmods>",
            "TechType" => "<techtypesmods><techtype>Unused</techtype></techtypesmods>",
            "Tactics" => "<tactics><future>STR_ABILITY_UNUSED</future></tactics>",
            _ => "<minorgodsmods><ClassicalAgeUnused portraitfilename='keep.png' future='STR_ABILITY_UNUSED'/></minorgodsmods>"
        };
        var path = fixture.Workspace.WriteText("TestMod/game/data/gameplay/" + file, xml);
        var strings = fixture.Workspace.WriteText("TestMod/game/data/strings/English/stringmods.txt",
            "// unmanaged keys stay\r\nID = \"STR_ABILITY_UNUSED\" ; Str = \"Éclaireur  \"\r\n", System.Text.Encoding.Unicode);
        var before = File.ReadAllBytes(strings);
        var host = AvaloniaTestHost.Run(() => fixture.CreateHost(false));
        try
        {
            AvaloniaTestHost.RunAsync(async () =>
            {
                if (kind == "Tactics")
                {
                    var result = new TacticsManagerResult(); result.DeletedNames.Add("Unused.tactics");
                    await (Task)host.Call("ApplyTacticsManagerChangesAsync", result)!;
                    Assert.False(File.Exists(path));
                }
                else
                {
                    var method = kind == "UnitType" ? "DeleteUnitTypeAsync" : kind == "TechType" ? "DeleteTechTypeAsync" : "DeleteMinorGodAsync";
                    Assert.True(await (Task<bool>)host.Call(method, kind == "OrphanMinorGod" ? "ClassicalAgeUnused" : "Unused")!);
                    Assert.DoesNotContain(kind == "OrphanMinorGod" ? "ClassicalAgeUnused" : "Unused", File.ReadAllText(path));
                }
                Assert.Equal(before, File.ReadAllBytes(strings));
            });
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }
}
