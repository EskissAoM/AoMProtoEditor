using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Windows;
using Xunit;
using Xunit.Abstractions;

namespace AoMDivineDataEditor.Tests;

public sealed class CultureUsageBatchTests(ITestOutputHelper output)
{
    [Fact]
    public void BatchMatchesIndividualInspectionsAcrossSupportedSourcesWithoutWriting()
    {
        using var workspace = new TestWorkspace("culture-usage-batch");
        var names = Enumerable.Range(0, 16).Select(index => $"Culture{index}").Append("UnusedCulture").ToArray();
        workspace.WriteXml("cultures_mods.xml", new XElement("culturesmods", names.Select(name =>
            new XElement("culture", new XElement("name", name)))).ToString());
        foreach (var index in Enumerable.Range(0, 12))
            workspace.WriteXml($"rules{index}.xml", new XElement("protomods", names.Take(16).Select(name =>
                new XElement("unit", new XAttribute("name", $"Owner{index}_{name}"),
                    new XElement("icon", new XAttribute("culture", name), "icon.png"),
                    new XElement("culture", name), new XElement("futureReference", name)))).ToString());
        workspace.WriteXml("custom.tactics", "<tactics><tactic><culture>Culture0</culture></tactic></tactics>");
        var compiled = workspace.GetPath("compiled.tactics.xmb");
        File.WriteAllBytes(compiled, XmbReaderTests.BuildDocument(XElement.Parse(
            "<tactics><tactic><culture>Culture1</culture></tactic></tactics>")));
        workspace.WriteText("custom.txt", "Culture0 Culture0 Culture1 UnusedCultureElite");
        var utf16 = workspace.GetPath("utf16.txt");
        File.WriteAllBytes(utf16, Encoding.Unicode.GetBytes("Culture2 Culture3"));
        workspace.WriteText("stringmods.txt", "UnusedCulture");
        workspace.WriteText("ignored.json", "UnusedCulture");
        var files = ModReferenceFileCatalog.Discover(workspace.RootPath).ToList();
        files.Add(files[0]); // Duplicate discovery descriptors must not double counts.
        files.Add(files[0] with { Path = workspace.GetPath("missing.xml") });
        var bytesBefore = Directory.GetFiles(workspace.RootPath).ToDictionary(path => path, File.ReadAllBytes);
        var timer = Stopwatch.StartNew();
        var expected = names.ToDictionary(name => name, name => ModDeletionSafety.InspectIdentifier(
            files, GameIdentifierKind.Culture, name, workspace.RootPath).References.Count);
        var individualTime = timer.Elapsed;
        timer.Restart();
        var actual = ModDeletionSafety.CountIdentifierReferencesForDisplay(files, GameIdentifierKind.Culture,
            names.Concat(["culture0", "", " "]), workspace.RootPath);
        var batchTime = timer.Elapsed;
        Assert.Equal(names.Length, actual.Count);
        foreach (var name in names) Assert.Equal(expected[name], actual[name.ToUpperInvariant()]);
        Assert.Equal(0, actual["UnusedCulture"]);
        Assert.True(actual["Culture0"] > 0);
        foreach (var entry in bytesBefore) Assert.Equal(entry.Value, File.ReadAllBytes(entry.Key));
        output.WriteLine($"17 identifiers / 15 XML or tactics documents: individual={individualTime.TotalMilliseconds:F1} ms; batch={batchTime.TotalMilliseconds:F1} ms. Diagnostic timing only.");
    }

    [Fact]
    public void EmptyNamesDoNotEnumerateFiles()
    {
        static IEnumerable<TechnologyReferenceFile> UnexpectedEnumeration()
            => throw new InvalidOperationException("Files must not be requested.");
        // Defer the exception until GetEnumerator is actually used.
        static IEnumerable<TechnologyReferenceFile> Files()
        {
            foreach (var file in UnexpectedEnumeration()) yield return file;
        }
        Assert.Empty(ModDeletionSafety.CountIdentifierReferencesForDisplay(Files(), GameIdentifierKind.Culture, ["", " "], null));
    }

    [Fact]
    public void SharedTextScannerRetainsTacticsStemAliasesAndExclusions()
    {
        using var workspace = new TestWorkspace("usage-text-alias");
        var included = workspace.WriteText("custom.txt", "custom custom.tactics customElite");
        var excluded = workspace.WriteText("excluded.txt", "custom");
        workspace.WriteText("stringmods.txt", "custom");
        var individual = ModDeletionSafety.InspectIdentifier([], GameIdentifierKind.Tactics, "custom.tactics",
            workspace.RootPath, excludedPaths: [excluded]);
        Assert.Equal(included, Assert.Single(individual.References).Path);
        var counts = ModDeletionSafety.CountIdentifierReferencesForDisplay([], GameIdentifierKind.Tactics,
            ["custom.tactics", "custom"], workspace.RootPath);
        Assert.Equal(2, counts["custom.tactics"]);
        Assert.Equal(2, counts["custom"]);
    }

    [Theory]
    [InlineData("<rules>")]
    [InlineData("<!DOCTYPE rules [<!ENTITY ext SYSTEM 'file:///not-read'>]><rules>&ext;</rules>")]
    public void MalformedOrUnsafeDocumentsAreNotSilentlyCountedAsSafe(string xml)
    {
        using var fixture = new BaselineSafetyFixture();
        fixture.Workspace.WriteText("TestMod/rules.xml", xml);
        var files = ModReferenceFileCatalog.Discover(fixture.Workspace.ModPath);
        Assert.ThrowsAny<Exception>(() => ModDeletionSafety.CountIdentifierReferencesForDisplay(
            files, GameIdentifierKind.Culture, ["Greek"], fixture.Workspace.ModPath));
        AvaloniaTestHost.Run(() =>
        {
            var host = fixture.CreateHost(false);
            try
            {
                var counts = (IReadOnlyDictionary<string, int>)host.Call("GetCultureUsageCountsForDisplay", (object)new[] { "Greek" })!;
                Assert.Empty(counts); // Existing optional display fallback only.
                Assert.Throws<TargetInvocationException>(() => host.Call("InspectIdentifierForDeletion", GameIdentifierKind.Culture, "Greek", null!, null!, false));
            }
            finally { host.DisposeFixtureWindow(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ManagerShowsCountsAndNewReferencesStillBlockDeletion(bool textReference)
    {
        using var fixture = new BaselineSafetyFixture();
        var culturePath = fixture.Workspace.WriteXml("TestMod/game/data/gameplay/culture_mods.xml",
            "<culturesmods><culture><name>Greek</name></culture><culture><name>UnusedCulture</name></culture></culturesmods>");
        var before = File.ReadAllBytes(culturePath);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var host = fixture.CreateHost(false);
            host.Show();
            try
            {
                host.Call("InitializeEditorTabs");
                var opening = (Task)host.Call("OpenCultureManagerAsync")!;
                var manager = Assert.Single(host.OwnedWindows.OfType<CultureManagerWindow>());
                var items = (IList)typeof(CultureManagerWindow).GetField("_items", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager)!;
                var unused = items.Cast<object>().Single(item => (string)item.GetType().GetProperty("Name")!.GetValue(item)! == "UnusedCulture");
                Assert.Equal(0, (int)unused.GetType().GetProperty("UsageCount")!.GetValue(unused)!);
                manager.Close();
                await opening.WaitAsync(TimeSpan.FromSeconds(5));
                if (textReference) fixture.Workspace.WriteText("TestMod/new-reference.txt", "UnusedCulture");
                else fixture.Workspace.WriteXml("TestMod/new-reference.xml", "<civsmods><civ><name>NewGod</name><culture>UnusedCulture</culture></civ></civsmods>");
                var refreshed = (IReadOnlyDictionary<string, int>)host.Call("GetCultureUsageCountsForDisplay", (object)new[] { "UnusedCulture" })!;
                Assert.Equal(1, refreshed["UnusedCulture"]);
                var deletion = (Task<bool>)host.Call("DeleteCultureAsync", "UnusedCulture")!;
                Assert.False(deletion.IsCompleted);
                Assert.Single(host.OwnedWindows).Close();
                Assert.False(await deletion.WaitAsync(TimeSpan.FromSeconds(5)));
                Assert.Equal(before, File.ReadAllBytes(culturePath));
            }
            finally
            {
                foreach (var child in host.OwnedWindows.ToArray()) child.Close();
                host.DisposeFixtureWindow();
            }
        });
    }
}
