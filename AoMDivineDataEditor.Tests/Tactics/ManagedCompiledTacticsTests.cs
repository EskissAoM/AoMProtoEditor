using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Windows;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class ManagedCompiledTacticsTests
{
    private const string Definition = "<tactics><action><name>Attack</name><type>Attack</type><unit>Hoplite</unit></action><tactic>Default</tactic></tactics>";

    private static string Write(TestWorkspace workspace, string relative, bool compiled)
    {
        var path = workspace.GetPath(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (compiled) File.WriteAllBytes(path, XmbReaderTests.BuildDocument(XElement.Parse(Definition)));
        else File.WriteAllText(path, Definition);
        return path;
    }

    [Theory]
    [InlineData("nested/Attack.tactics", false)]
    [InlineData("nested/Attack.tactics.xmb", true)]
    [InlineData("nested/Attack.xmb", true)]
    public void PhysicalSourcesRetainFormatAndOnlyXmlIsEditable(string relative, bool compiled)
    {
        using var workspace = new TestWorkspace("f13-physical");
        var root = workspace.CreateDirectory("tactics");
        var path = Write(workspace, "tactics/" + relative, compiled);
        var source = ManagedTacticsSource.Resolve(root, "nested/Attack.tactics");
        Assert.Equal(path, Assert.Single(source.Paths));
        Assert.Equal(compiled, source.IsCompiled);
        Assert.Equal(!compiled, source.CanEdit);
        Assert.Equal("Hoplite", source.Load().Descendants("unit").Single().Value);
        Assert.Throws<IOException>(source.RequireMissing);
        if (compiled)
        {
            Assert.Throws<InvalidOperationException>(() => source.RequireEditable());
            Assert.Contains("read-only", source.ReadOnlyReason!);
            var session = new TacticsActionEditorSession("Attack", !source.CanEdit, source.Load(), null);
            Assert.True(session.IsReadOnly);
            Assert.False(session.CanSave);
        }
        else Assert.Equal(path, source.RequireEditable());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompetingAliasesAreNeverSelectedForMutation(bool xmlPair)
    {
        using var workspace = new TestWorkspace("f13-pair");
        var root = workspace.CreateDirectory("tactics");
        Write(workspace, "tactics/Attack.tactics.xmb", true);
        Write(workspace, xmlPair ? "tactics/Attack.tactics" : "tactics/Attack.xmb", !xmlPair);
        var source = ManagedTacticsSource.Resolve(root, "Attack");
        Assert.True(source.IsAmbiguous);
        Assert.False(source.CanEdit);
        Assert.Throws<InvalidOperationException>(() => source.RequireEditable());
        Assert.Throws<InvalidOperationException>(() => source.Load());
        Assert.Equal("Hoplite", source.Load(allowAmbiguousView: true).Descendants("unit").Single().Value);
        Assert.Throws<IOException>(source.RequireMissing);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CounterpartCreatedAfterInspectionBlocksWrites(bool existingXml)
    {
        using var workspace = new TestWorkspace("f13-counterpart");
        var root = workspace.CreateDirectory("tactics");
        if (existingXml) Write(workspace, "tactics/Attack.tactics", false);
        var source = ManagedTacticsSource.Resolve(root, "Attack");
        Write(workspace, "tactics/Attack.tactics.xmb", true);
        var wrote = false;
        Assert.Throws<IOException>(() => FileIntegrityTransaction.Execute([source.XmlPath], source.ExpectedStates, () => wrote = true));
        Assert.False(wrote);
    }

    [Fact]
    public void ResolverRejectsNamesOutsideTacticsRoot()
    {
        using var workspace = new TestWorkspace("f13-contained");
        Assert.Throws<InvalidOperationException>(() => ManagedTacticsSource.Resolve(workspace.RootPath, "../escape"));
    }

    [Fact]
    public void ActualXmlRenameAndDeleteCallbacksReportTheirCommittedResult()
    {
        using var fixture = new BaselineSafetyFixture();
        var original = Write(fixture.Workspace, "TestMod/game/data/gameplay/tactics/Attack.tactics", false);
        var before = File.ReadAllBytes(original);
        var host = AvaloniaTestHost.Run(() => fixture.CreateHost(false));
        try
        {
            AvaloniaTestHost.RunAsync(async () =>
            {
                Assert.True(await (Task<bool>)host.Call("RenameManagedTacticsFileAsync", "Attack.tactics", "Renamed.tactics")!);
                var renamed = fixture.Workspace.GetPath("TestMod/game/data/gameplay/tactics/Renamed.tactics");
                Assert.False(File.Exists(original));
                Assert.Equal(before, File.ReadAllBytes(renamed));
                Assert.True(await (Task<bool>)host.Call("DeleteManagedTacticsFileAsync", "Renamed.tactics")!);
                Assert.False(File.Exists(renamed));
            });
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }

    [Theory]
    [InlineData("nested/Attack.tactics", false)]
    [InlineData("nested/Attack.tactics.xmb", true)]
    [InlineData("nested/Attack.xmb", true)]
    public void ActualDuplicateCallbackPreservesSourceAndCreatesCompleteEditableXml(string relative, bool compiled)
    {
        using var fixture = new BaselineSafetyFixture();
        var path = Write(fixture.Workspace, "TestMod/game/data/gameplay/tactics/" + relative, compiled);
        var before = File.ReadAllBytes(path);
        var host = AvaloniaTestHost.Run(() => fixture.CreateHost(false));
        try
        {
            AvaloniaTestHost.RunAsync(async () =>
            {
                var names = (HashSet<string>)host.Call("GetCustomTacticsNames")!;
                Assert.Contains("nested\\Attack.tactics", names);
                Assert.True(await (Task<bool>)host.Call("DuplicateManagedTacticsFileAsync", "nested/Attack.tactics", false, "Copy.tactics")!);
            });
            var copy = fixture.Workspace.GetPath("TestMod/game/data/gameplay/tactics/Copy.tactics");
            Assert.True(XNode.DeepEquals(XElement.Parse(Definition), XElement.Load(copy)));
            Assert.Equal(before, File.ReadAllBytes(path));
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }

    [Theory]
    [InlineData("Delete", false)]
    [InlineData("Rename", false)]
    [InlineData("Delete", true)]
    [InlineData("Duplicate", true)]
    public void ActualCallbacksRefuseReadOnlySourcesWithoutReportingSuccess(string operation, bool paired)
    {
        using var fixture = new BaselineSafetyFixture();
        var path = Write(fixture.Workspace, "TestMod/game/data/gameplay/tactics/Attack.tactics.xmb", true);
        if (paired) Write(fixture.Workspace, "TestMod/game/data/gameplay/tactics/Attack.tactics", false);
        var before = File.ReadAllBytes(path);
        var host = AvaloniaTestHost.Run(() => fixture.CreateHost(false));
        try
        {
            AvaloniaTestHost.RunAsync(async () =>
            {
                host.Show();
                var task = operation switch
                {
                    "Delete" => (Task<bool>)host.Call("DeleteManagedTacticsFileAsync", "Attack.tactics")!,
                    "Rename" => (Task<bool>)host.Call("RenameManagedTacticsFileAsync", "Attack.tactics", "Renamed.tactics")!,
                    _ => (Task<bool>)host.Call("DuplicateManagedTacticsFileAsync", "Attack.tactics", false, "Copy.tactics")!
                };
                Prompt? prompt = null;
                for (var attempt = 0; attempt < 200 && prompt == null; attempt++)
                {
                    prompt = host.OwnedWindows.OfType<Prompt>().SingleOrDefault();
                    if (prompt == null) await Task.Delay(10);
                }
                Assert.NotNull(prompt);
                Assert.Contains(paired ? "Multiple" : "read-only", prompt.PromptText, StringComparison.OrdinalIgnoreCase);
                prompt.Close();
                Assert.False(await task);
            });
            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.False(File.Exists(fixture.Workspace.GetPath("TestMod/game/data/gameplay/tactics/Copy.tactics")));
            Assert.False(File.Exists(fixture.Workspace.GetPath("TestMod/game/data/gameplay/tactics/Renamed.tactics")));
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }
}
