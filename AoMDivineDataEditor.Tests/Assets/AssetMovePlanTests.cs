using System.Reflection;
using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class AssetMovePlanTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MoveAndReferencesCommitOrRollBackTogether(bool failWrite)
    {
        using var workspace = new TestWorkspace("asset-move");
        var source = workspace.WriteText("resources/old.png", "asset bytes");
        var destination = workspace.GetPath("resources/new.png");
        var proto = workspace.WriteXml("proto_mods.xml", "<protomods><unit name='U'><icon>old.png</icon></unit></protomods>");
        var tech = workspace.WriteXml("techtree_mods.xml", "<techtreemods><tech name='T'><icon>old.png</icon></tech></techtreemods>");
        var before = new[] { source, proto, tech }.ToDictionary(path => path, File.ReadAllBytes);
        var plan = new AssetMovePlan(source, destination, ModReferenceFileCatalog.Discover(workspace.RootPath), AssetReferenceKind.Icon, "old.png", "new.png");
        var writes = 0;
        void Commit() => plan.Commit(ModReferenceFileCatalog.Discover(workspace.RootPath), (document, path) =>
        {
            PreservedXmlFile.SaveAtomic(document, path);
            if (failWrite && ++writes == 2) throw new IOException("injected after document writes");
        });
        if (failWrite)
        {
            Assert.Throws<IOException>(Commit);
            foreach (var entry in before) Assert.Equal(entry.Value, File.ReadAllBytes(entry.Key));
            Assert.False(File.Exists(destination));
        }
        else
        {
            Commit();
            Assert.False(File.Exists(source));
            Assert.Equal(before[source], File.ReadAllBytes(destination));
            foreach (var path in new[] { proto, tech }) Assert.Equal("new.png", XDocument.Load(path).Descendants("icon").Single().Value);
        }
    }

    [Theory]
    [InlineData("source")]
    [InlineData("destination")]
    [InlineData("added")]
    [InlineData("unchanged")]
    public void ChangesAfterReviewRefuseBeforeMoving(string change)
    {
        using var workspace = new TestWorkspace("asset-stale");
        var source = workspace.WriteText("old.png", "asset bytes");
        var destination = workspace.GetPath("new.png");
        var dependency = workspace.WriteXml("unrelated.xml", "<root />");
        var plan = new AssetMovePlan(source, destination, ModReferenceFileCatalog.Discover(workspace.RootPath), AssetReferenceKind.Icon, "old.png", "new.png");
        switch (change)
        {
            case "source": File.WriteAllText(source, "external asset"); break;
            case "destination": File.WriteAllText(destination, "external destination"); break;
            case "added": workspace.WriteXml("added.xml", "<root><icon>old.png</icon></root>"); break;
            case "unchanged": File.WriteAllText(dependency, "<root changed='yes'/>"); break;
        }
        var before = Directory.GetFiles(workspace.RootPath).ToDictionary(path => path, File.ReadAllBytes);
        Assert.ThrowsAny<IOException>(() => plan.Commit(ModReferenceFileCatalog.Discover(workspace.RootPath), (_, _) => Assert.Fail("Must not write")));
        foreach (var entry in before) Assert.Equal(entry.Value, File.ReadAllBytes(entry.Key));
        Assert.True(File.Exists(source));
    }

    [Fact]
    public void HostRefusesDirtyGodThenAdoptsHiddenGodAndSavedTabAfterCommit()
    {
        using var fixture = new BaselineSafetyFixture();
        var host = AvaloniaTestHost.Run(fixture.CreateHost);
        AvaloniaTestHost.Run(host.ChangeGodKey);
        AvaloniaTestHost.Run(() =>
        {
            try
            {
                Assert.True(host.God!.IsDirty);
                host.AddGodTab(pinned: true, visible: false);
                var source = fixture.Workspace.WriteText("TestMod/game/resources/fixture_icon.png", "icon bytes");
                var destination = fixture.Workspace.GetPath("TestMod/game/resources/new.png");
                var files = (IReadOnlyList<TechnologyReferenceFile>)host.Call("GetGameIdentifierReferenceFiles")!;
                var plan = new AssetMovePlan(source, destination, files, AssetReferenceKind.Icon, "fixture_icon", "new.png");
                var exception = Assert.Throws<TargetInvocationException>(() => host.Call("CommitAssetMove", plan));
                Assert.IsType<InvalidOperationException>(exception.InnerException);
                Assert.True(File.Exists(source));
                Assert.False(File.Exists(destination));
            }
            finally { host.DisposeFixtureWindow(); }

            host = fixture.CreateHost();
            try
            {
                var tab = host.AddGodTab(pinned: true, visible: false);
                var previousContent = host.GodHost.Content;
                var source = fixture.Workspace.GetPath("TestMod/game/resources/fixture_icon.png");
                var destination = fixture.Workspace.GetPath("TestMod/game/resources/new.png");
                var files = (IReadOnlyList<TechnologyReferenceFile>)host.Call("GetGameIdentifierReferenceFiles")!;
                var plan = new AssetMovePlan(source, destination, files, AssetReferenceKind.Icon, "fixture_icon", "new.png");
                host.Call("CommitAssetMove", plan);
                host.Call("AdoptAssetMove", plan);
                Assert.Same(previousContent, host.GodHost.Content);
                Assert.Equal("new.png", BaselineHost.SavedGod(tab).Element("icon")!.Value);
                Assert.Equal("new.png", host.God!.LoadSavedMajorGodElement("CheckpointGod")!.Element("icon")!.Value);
                Assert.False(host.God.IsDirty);
                Assert.False(File.Exists(source));
                Assert.True(File.Exists(destination));
            }
            finally { host.DisposeFixtureWindow(); }
        });
    }
}
