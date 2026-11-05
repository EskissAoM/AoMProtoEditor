using System.Diagnostics;
using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Controls;
using AoMDivineDataEditor.Windows;
using Avalonia.Controls;
using Avalonia.Threading;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class UnitTypeManagerOpeningTests
{
    public static IEnumerable<object[]> Documents => new[]
    {
        "<proto><unit name='Owner'><unittype>A</unittype><socketunittype>b</socketunittype><future>A</future><protoaction><name>Attack</name><rate type='A'>1</rate><damagebonus type='B'>2</damagebonus><target>A</target><empowerdata><A>1</A></empowerdata></protoaction></unit></proto>",
        "<techtree><tech name='T'><effects><effect type='Data' subtype='Damage' unittype='B'><target type='ProtoUnit'> A </target></effect><effect type='CreateUnit' generator='B'><target type='ProtoUnit'>A</target></effect><effect type='Data' subtype='WorkRate' unittype='Food'/><effect type='Enable'><target type='ProtoUnit'>B</target></effect></effects><metadata>B</metadata></tech></techtree>",
        "<powersmod><power name='P' type='GeneralEffect'><abstractplacementtargettype>A</abstractplacementtargettype><placement excludetypes='A|b|A'>Unit</placement><effect type='DamageBonus' unittype='B'/><future type='B'>A</future></power></powersmod>",
        "<tactics><tactic>Default<attacktype>A</attacktype><excludeattacktype>b</excludeattacktype><attackprioritytype>A</attackprioritytype><future>B</future></tactic></tactics>",
        "<civsmods><civ><name>God</name><contributor type='A'/><chainablebuilding>B</chainablebuilding><shades><unit>A</unit><targetunit>B</targetunit></shades></civ></civsmods>",
        "<proto xmlns:x='urn:foreign'><unit name='A'><protoaction><name>Empower</name><enemyempowerdata><B>1</B></enemyempowerdata><natureempowerdata><A>2</A></natureempowerdata></protoaction><x:unittype>B</x:unittype><x:future><unittype>A</unittype></x:future></unit></proto>",
        "<abstractunittypesmods><abstractunittype>A</abstractunittype><abstractunittype>B</abstractunittype></abstractunittypesmods>"
    }.Select(xml => new object[] { xml });

    [Theory]
    [MemberData(nameof(Documents))]
    public void BulkDisplayLookupMatchesAuthoritativeRecognitionWithoutMutatingXml(string xml)
    {
        var document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        var before = document.ToString(SaveOptions.DisableFormatting);
        var known = new HashSet<string>(["A", "B", "Unit", "Food", "Absent"], StringComparer.OrdinalIgnoreCase);
        foreach (XContainer scope in new XContainer[] { document }.Concat(document.Root!.Elements()))
        {
            var bulk = GameIdentifierReferencePolicy.FindRecognizedUnitTypeIdentifiers(scope, known);
            foreach (var name in known)
                Assert.Equal(GameIdentifierReferencePolicy.FindRecognizedReferences(scope, GameIdentifierKind.UnitType, name).Count > 0,
                    bulk.Contains(name));
        }
        Assert.Equal(before, document.ToString(SaveOptions.DisableFormatting));
    }

    [Fact]
    public void BulkDisplayLookupAvoidsPerTypeFullRecordAllocation()
    {
        var known = Enumerable.Range(0, 64).Select(i => "Type" + i).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var owner = new XElement("unit", new XAttribute("name", "Owner"), known.Select(name => new XElement("unittype", name)));
        _ = GameIdentifierReferencePolicy.FindRecognizedUnitTypeIdentifiers(owner, known);
        foreach (var name in known) _ = GameIdentifierReferencePolicy.FindRecognizedReferences(owner, GameIdentifierKind.UnitType, name);
        var start = GC.GetAllocatedBytesForCurrentThread();
        var bulk = GameIdentifierReferencePolicy.FindRecognizedUnitTypeIdentifiers(owner, known);
        var bulkBytes = GC.GetAllocatedBytesForCurrentThread() - start;
        start = GC.GetAllocatedBytesForCurrentThread();
        foreach (var name in known) Assert.NotEmpty(GameIdentifierReferencePolicy.FindRecognizedReferences(owner, GameIdentifierKind.UnitType, name));
        var previousBytes = GC.GetAllocatedBytesForCurrentThread() - start;
        Assert.True(known.SetEquals(bulk));
        Assert.True(bulkBytes * 4 < previousBytes, $"Bulk: {bulkBytes}; per-type: {previousBytes}");
        Console.WriteLine($"Unit Type usage allocations: bulk={bulkBytes}, per-type={previousBytes} bytes.");
    }

    [Fact]
    public void FullSizedUnitCatalogOpensFiltersAndReopensWithoutWritingFiles()
    {
        using var fixture = new BaselineSafetyFixture();
        var types = Enumerable.Range(0, 64).Select(i => "Type" + i).ToArray();
        File.WriteAllText(fixture.TypePath, new XElement("abstractunittypesmods", types.Select(name => new XElement("abstractunittype", name))).ToString());
        var proto = new XElement("proto", Enumerable.Range(0, 2500).Select(i => new XElement("unit", new XAttribute("name", "Owner" + i),
            types.Take(32).Select(name => new XElement("unittype", name)),
            new XElement("protoaction", new XElement("name", "Attack"),
                types.Take(32).Select(name => new XElement("rate", new XAttribute("type", name), 1))))));
        var before = new[] { fixture.TypePath, fixture.ProtoPath, fixture.GodPath }.ToDictionary(path => path, File.ReadAllBytes);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var host = fixture.CreateHost(false); host.Set("_barXmlRoot", proto); host.Show();
            try
            {
                var counts = (Dictionary<string, int>)host.Call("GetUnitTypeUsageCounts")!;
                Assert.Equal(2500, counts["Type0"]); Assert.False(counts.ContainsKey("Type63"));
                for (var i = 0; i < 2; i++)
                {
                    var clock = Stopwatch.StartNew();
                    var opening = (Task)host.Call("OpenUnitTypeManagerAsync")!;
                    for (var attempt = 0; attempt < 200 && !host.OwnedWindows.OfType<UnitTypeManagerWindow>().Any(); attempt++) await Task.Delay(10);
                    var manager = Assert.Single(host.OwnedWindows.OfType<UnitTypeManagerWindow>());
                    await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                    Console.WriteLine($"Unit Type manager full-catalog opening {i + 1}: {clock.ElapsedMilliseconds} ms.");
                    var shell = Assert.IsType<ManagerListShell>(manager.Content);
                    shell.FilterComboBox.SelectedItem = "Custom";
                    shell.SearchBox.Text = "Type63"; await Task.Delay(220);
                    Assert.Contains("custom", shell.FooterTextBlock.Text!);
                    Assert.False(shell.EmptyStateMessage.IsVisible);
                    manager.Close(); await opening.WaitAsync(TimeSpan.FromSeconds(5));
                }
                foreach (var file in before) Assert.Equal(file.Value, File.ReadAllBytes(file.Key));
            }
            finally { foreach (var child in host.OwnedWindows.ToArray()) child.Close(); host.DisposeFixtureWindow(); }
        });
    }
}
