using System.Text;
using System.Xml;
using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class ReferencePrefilterTests
{
    [Theory]
    [InlineData("utf8", false)]
    [InlineData("utf8", true)]
    [InlineData("utf16le", false)]
    [InlineData("utf16le", true)]
    [InlineData("utf16be", false)]
    [InlineData("utf16be", true)]
    public void EncodingsDoNotHideReferences(string encoding, bool bom)
    {
        using var workspace = new TestWorkspace("reference-encoding");
        Encoding codec = encoding switch
        {
            "utf16le" => new UnicodeEncoding(false, bom),
            "utf16be" => new UnicodeEncoding(true, bom),
            _ => new UTF8Encoding(bom)
        };
        var declaration = encoding == "utf8" ? "utf-8" : "utf-16";
        var path = workspace.WriteText("game/data/custom.xml",
            $"<?xml version='1.0' encoding='{declaration}'?><root><train>OldUnit</train></root>", codec);
        var original = File.ReadAllBytes(path);
        var plan = Prepare(workspace);
        Assert.Equal("NewUnit", plan.GetDocument(path)!.Root!.Element("train")!.Value);
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData("<train>Old&#85;nit</train>")]
    [InlineData("<train>Old&#x55;nit</train>")]
    [InlineData("<train>Old<![CDATA[Unit]]></train>")]
    [InlineData("<train>Old<!-- split -->Unit</train>")]
    [InlineData("<train>Old<?split value?>Unit</train>")]
    [InlineData("<entry unit='Old&#85;nit'/>")]
    public void XmlSyntaxDoesNotHideReferences(string reference)
    {
        using var workspace = new TestWorkspace("reference-syntax");
        var path = workspace.WriteXml("game/data/custom.xml", "<root>" + reference + "</root>");
        var plan = Prepare(workspace);
        Assert.Single(plan.Paths);
        Assert.Empty(GameIdentifierReferencePolicy.FindRecognizedReferences(plan.GetDocument(path)!, GameIdentifierKind.ProtoUnit, "OldUnit"));
        Assert.Single(plan.AffectedReferences);
    }

    [Fact]
    public void SuffixlessTacticsAndTrimmedNamesUsePolicySemantics()
    {
        using var workspace = new TestWorkspace("reference-tactics");
        var path = workspace.WriteXml("game/data/custom.xml", "<root><tactics>OldTactics</tactics></root>");
        var plan = GameIdentifierReferenceMigrationPlan.Prepare(ModReferenceFileCatalog.Discover(workspace.RootPath),
            GameIdentifierKind.Tactics, [new(" OldTactics.tactics ", "NewTactics.tactics")]);
        Assert.Equal("NewTactics", plan.GetDocument(path)!.Root!.Element("tactics")!.Value);
    }

    [Fact]
    public void EncodedUnknownReferenceStillBlocksBeforeWriting()
    {
        using var workspace = new TestWorkspace("reference-unknown");
        var path = workspace.WriteXml("game/data/custom.xml", "<root><futureunit>Old&#85;nit</futureunit></root>");
        var original = File.ReadAllBytes(path);
        Assert.Throws<InvalidOperationException>(() => Prepare(workspace));
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public void ChunkBoundaryAndRenameChainArePreserved()
    {
        using var workspace = new TestWorkspace("reference-boundary");
        const string prefix = "<root><padding>";
        const string middle = "</padding><train>Old";
        var path = workspace.WriteXml("game/data/custom.xml", prefix +
            new string('x', 65535 - prefix.Length - middle.Length) + middle + "<!--split-->Unit</train></root>");
        var plan = GameIdentifierReferenceMigrationPlan.Prepare(ModReferenceFileCatalog.Discover(workspace.RootPath),
            GameIdentifierKind.ProtoUnit, [new("OldUnit", "MiddleUnit"), new("MiddleUnit", "NewUnit")]);
        Assert.Equal("NewUnit", plan.GetDocument(path)!.Root!.Element("train")!.Value);
    }

    [Fact]
    public void SizeLimitAndDtdProtectionRemainEnforced()
    {
        using var workspace = new TestWorkspace("reference-limits");
        var path = workspace.WriteText("game/data/custom.xml", "<!DOCTYPE root [<!ENTITY unit 'OldUnit'>]><root><train>&unit;</train></root>");
        Assert.Throws<XmlException>(() => Prepare(workspace));
        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
            stream.SetLength(256L * 1024 * 1024 + 1);
        Assert.Throws<InvalidOperationException>(() => Prepare(workspace));
    }

    private static GameIdentifierReferenceMigrationPlan Prepare(TestWorkspace workspace)
        => GameIdentifierReferenceMigrationPlan.Prepare(ModReferenceFileCatalog.Discover(workspace.RootPath),
            GameIdentifierKind.ProtoUnit, [new("OldUnit", "NewUnit")]);
}
