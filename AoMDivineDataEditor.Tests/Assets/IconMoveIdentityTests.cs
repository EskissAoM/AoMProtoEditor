using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class IconMoveIdentityTests
{
    private static AssetDestination Destination(string root)
    {
        Assert.True(AssetDestinationPolicy.TryResolve(root, "moved", "new", ".png", "resources", out var destination, out var error), error);
        return destination!;
    }

    private static IconMoveIdentity Prepare(string root)
        => IconMoveIdentity.Prepare(root, IconSourceResolver.ResolveCustom(root, "old.png")!, Destination(root));

    private static Dictionary<string, byte[]> Snapshot(TestWorkspace workspace)
        => Directory.GetFiles(workspace.RootPath, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);

    private static void Unchanged(TestWorkspace workspace, Dictionary<string, byte[]> expected)
    {
        Assert.Equal(expected.Keys.Order(), Directory.GetFiles(workspace.RootPath, "*", SearchOption.AllDirectories).Order());
        foreach (var file in expected) Assert.Equal(file.Value, File.ReadAllBytes(file.Key));
    }

    [Theory]
    [InlineData("png", false)]
    [InlineData("dds", false)]
    [InlineData("png", true)]
    [InlineData("dds", true)]
    public void PhysicalMoveAndAllSupportedReferencesCommitOrRollBackTogether(string extension, bool failWrite)
    {
        using var workspace = new TestWorkspace();
        var source = workspace.WriteText("resources/old." + extension, "original image bytes\r\n");
        var resources = Path.GetDirectoryName(source)!;
        string[] documents = [
            "<protomods><unit name='U'><icon>resources/old.png</icon><future keep='yes'/></unit></protomods>",
            "<techtreemods><tech name='T'><icon>OLD.PNG</icon></tech></techtreemods>",
            "<civsmods><civ><name>G</name><portrait>old</portrait><breakoutportrait>old.png</breakoutportrait></civ></civsmods>",
            "<minorgodsmods><ClassicalAgeG portraitfilename='old.png' portraiticonfilename='resources/old.png'/></minorgodsmods>",
            "<powersmod><power name='P'><icon>old.png</icon></power></powersmod>",
            "<abilities><ability name='A'><icon>old.png</icon></ability></abilities>"
        ];
        var paths = documents.Select((xml, index) => workspace.WriteXml($"data/{index}.xml", xml)).ToList();
        if (extension == "dds") paths.Add(workspace.WriteXml("data/explicit.xml", "<protomods><unit name='Legacy'><icon>RESOURCES/OLD.DDS</icon></unit></protomods>"));
        var before = Snapshot(workspace);
        var identity = Prepare(resources);
        Assert.EndsWith("new." + extension, identity.Destination.AbsolutePath);
        Assert.Equal("resources\\moved\\new.png", identity.Destination.XmlValue);
        var files = ModReferenceFileCatalog.Discover(workspace.RootPath);
        var plan = new AssetMovePlan(identity, files);
        var writes = 0;
        void Commit() => plan.Commit(ModReferenceFileCatalog.Discover(workspace.RootPath), (document, path) =>
        {
            PreservedXmlFile.SaveAtomic(document, path);
            if (failWrite && ++writes == 2) throw new IOException("injected reference write failure");
        });
        if (failWrite)
        {
            Assert.Throws<IOException>(Commit);
            Unchanged(workspace, before);
        }
        else
        {
            Commit();
            Assert.False(File.Exists(source));
            Assert.Equal(before[source], File.ReadAllBytes(identity.Destination.AbsolutePath));
            Assert.False(File.Exists(Path.ChangeExtension(identity.Destination.AbsolutePath, extension == "dds" ? ".png" : ".dds")));
            foreach (var path in paths)
            {
                Assert.True(XNode.DeepEquals(plan.References.GetDocument(path), XDocument.Load(path)));
                Assert.Contains("resources\\moved\\new." + (path.EndsWith("explicit.xml", StringComparison.Ordinal) ? "dds" : "png"), File.ReadAllText(path));
            }
            Assert.Equal("yes", (string?)XDocument.Load(paths[0]).Descendants("future").Single().Attribute("keep"));
        }
    }

    [Theory]
    [InlineData("png", "source-counterpart")]
    [InlineData("dds", "source-counterpart")]
    [InlineData("png", "target-counterpart")]
    [InlineData("dds", "target-counterpart")]
    [InlineData("png", "target")]
    [InlineData("dds", "target")]
    [InlineData("png", "source")]
    [InlineData("dds", "source")]
    public void LatePhysicalChangesRefuseWithoutTouchingExternalFiles(string extension, string change)
    {
        using var workspace = new TestWorkspace();
        var source = workspace.WriteText("resources/old." + extension, "source");
        workspace.WriteXml("proto_mods.xml", "<protomods><unit><icon>old.png</icon></unit></protomods>");
        var identity = Prepare(Path.GetDirectoryName(source)!);
        var plan = new AssetMovePlan(identity, ModReferenceFileCatalog.Discover(workspace.RootPath));
        var otherExtension = extension == "dds" ? ".png" : ".dds";
        var changedPath = change switch
        {
            "source-counterpart" => Path.ChangeExtension(source, otherExtension),
            "target-counterpart" => Path.ChangeExtension(identity.Destination.AbsolutePath, otherExtension),
            "target" => identity.Destination.AbsolutePath,
            _ => source
        };
        Directory.CreateDirectory(Path.GetDirectoryName(changedPath)!);
        File.WriteAllText(changedPath, "external bytes");
        var before = Snapshot(workspace);
        Assert.Throws<IOException>(() => plan.Commit(ModReferenceFileCatalog.Discover(workspace.RootPath), (_, _) => Assert.Fail("Must refuse before writing")));
        Unchanged(workspace, before);
    }

    [Theory]
    [InlineData("png", false)]
    [InlineData("dds", false)]
    [InlineData("png", true)]
    [InlineData("dds", true)]
    public void CounterpartAppearingDuringReferenceWriteRollsBackButKeepsTheContender(string extension, bool sourceSide)
    {
        using var workspace = new TestWorkspace();
        var source = workspace.WriteText("resources/old." + extension, "source bytes");
        var xml = workspace.WriteXml("proto_mods.xml", "<protomods><!-- preserve --><unit><icon>old.png</icon></unit></protomods>");
        var identity = Prepare(Path.GetDirectoryName(source)!);
        var plan = new AssetMovePlan(identity, ModReferenceFileCatalog.Discover(workspace.RootPath));
        var counterpart = Path.ChangeExtension(sourceSide ? source : identity.Destination.AbsolutePath, extension == "dds" ? ".png" : ".dds");
        var sourceBytes = File.ReadAllBytes(source);
        var xmlBytes = File.ReadAllBytes(xml);
        Assert.Throws<IOException>(() => plan.Commit(ModReferenceFileCatalog.Discover(workspace.RootPath), (document, path) =>
        {
            PreservedXmlFile.SaveAtomic(document, path);
            File.WriteAllText(counterpart, "late external contender");
        }));
        Assert.Equal(sourceBytes, File.ReadAllBytes(source));
        Assert.Equal(xmlBytes, File.ReadAllBytes(xml));
        Assert.False(File.Exists(identity.Destination.AbsolutePath));
        Assert.Equal("late external contender", File.ReadAllText(counterpart));
    }

    [Theory]
    [InlineData("paired-source")]
    [InlineData("target-png")]
    [InlineData("target-dds")]
    [InlineData("stale-format")]
    [InlineData("original")]
    [InlineData("outside")]
    public void InvalidOrCompetingIdentityRefusesBeforeAnyWrite(string mode)
    {
        using var workspace = new TestWorkspace();
        var source = workspace.WriteText("resources/old.dds", "dds");
        var root = Path.GetDirectoryName(source)!;
        var retained = IconSourceResolver.ResolveCustom(root, "old.png")!;
        var destination = Destination(root);
        if (mode == "paired-source")
        {
            File.WriteAllText(Path.ChangeExtension(source, ".png"), "png");
            retained = IconSourceResolver.ResolveCustom(root, "old.png")!;
        }
        if (mode.StartsWith("target-", StringComparison.Ordinal))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination.AbsolutePath)!);
            File.WriteAllText(Path.ChangeExtension(destination.AbsolutePath, mode == "target-png" ? ".png" : ".dds"), "target");
        }
        if (mode == "stale-format") { File.Delete(source); File.WriteAllText(Path.ChangeExtension(source, ".png"), "replacement"); }
        if (mode == "original") retained = new IconAssetSource("resources\\old.png", [new IconPhysicalSource("resources\\old.dds", IconFileFormat.Dds, false, "original.bar")]);
        if (mode == "outside") destination = destination with { AbsolutePath = workspace.GetPath("outside.png") };
        var before = Snapshot(workspace);
        var error = Record.Exception(() => IconMoveIdentity.Prepare(root, retained, destination));
        Assert.NotNull(error);
        Assert.True(error is IOException or InvalidOperationException);
        Unchanged(workspace, before);
    }

    [Theory]
    [InlineData("<protomods><unit><icon>old.dds</icon></unit></protomods>", false)]
    [InlineData("<root><future>old.dds</future></root>", true)]
    public void ExplicitDdsRequiresProvenDdsIdentityAndSupportedField(string xml, bool dds)
    {
        using var workspace = new TestWorkspace();
        var source = workspace.WriteText("resources/old." + (dds ? "dds" : "png"), "image");
        workspace.WriteXml("references.xml", xml);
        var before = Snapshot(workspace);
        var identity = Prepare(Path.GetDirectoryName(source)!);
        Assert.Throws<InvalidOperationException>(() => new AssetMovePlan(identity, ModReferenceFileCatalog.Discover(workspace.RootPath)));
        Unchanged(workspace, before);
    }
}
