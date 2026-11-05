using System.Reflection;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Windows;
using Avalonia.Controls;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class ReferenceReadBudgetTests
{
    private static void Prepare(string kind, IReadOnlyList<TechnologyReferenceFile> files, ReferenceReadBudget budget, Action writer)
    {
        switch (kind)
        {
            case "technology": TechnologyReferenceMigrationPlan.PrepareWithBudget(budget, files, [new("Old", "New")]).Save((_, _) => writer()); break;
            case "unit": GameIdentifierReferenceMigrationPlan.PrepareWithBudget(budget, files, GameIdentifierKind.ProtoUnit, [new("Old", "New")]).Save((_, _) => writer()); break;
            default: AssetReferenceMigrationPlan.PrepareCore(files, AssetReferenceKind.Icon, "old.png", "new.png", false, budget).Save((_, _) => writer()); break;
        }
    }

    [Theory]
    [InlineData("technology", -1)] [InlineData("technology", 0)] [InlineData("technology", 1)]
    [InlineData("unit", -1)] [InlineData("unit", 0)] [InlineData("unit", 1)]
    [InlineData("asset", -1)] [InlineData("asset", 0)] [InlineData("asset", 1)]
    public void AggregateSourceBoundaryIsSharedAndRefusalNeverCallsWriter(string kind, int headroom)
    {
        using var workspace = new TestWorkspace("reference-source-budget");
        workspace.WriteText("a.xml", kind == "asset"
            ? "<protomods><unit name='U'><icon>old.png</icon></unit></protomods>"
            : "<protomods><unit name='U'><tech>Old</tech><train>Old</train></unit></protomods>");
        var second = workspace.WriteText("b.xml", "<r/>");
        var files = ModReferenceFileCatalog.Discover(workspace.RootPath);
        var original = files.ToDictionary(file => file.Path, file => File.ReadAllBytes(file.Path));
        var size = original.Sum(entry => entry.Value.LongLength);
        var writes = 0;
        var budget = new ReferenceReadBudget(maximumSourceBytes: size + headroom);
        if (headroom < 0)
        {
            var error = Assert.Throws<ReferenceResourceLimitException>(() => Prepare(kind, files, budget, () => writes++));
            Assert.Contains("aggregate source bytes", error.Message);
            Assert.Contains("No files were written", error.Message);
            Assert.Equal(0, writes);
        }
        else
        {
            Prepare(kind, files, budget, () => writes++);
            Assert.Equal(1, writes);
        }
        foreach (var entry in original) Assert.Equal(entry.Value, File.ReadAllBytes(entry.Key));
    }

    [Theory]
    [InlineData("technology", "characters")] [InlineData("technology", "nodes")] [InlineData("technology", "estimated")]
    [InlineData("unit", "characters")] [InlineData("unit", "nodes")] [InlineData("unit", "estimated")]
    [InlineData("asset", "characters")] [InlineData("asset", "nodes")] [InlineData("asset", "estimated")]
    public void XmlBudgetRefusesMultipleIndividuallySmallDocuments(string kind, string limit)
    {
        using var workspace = new TestWorkspace("reference-dom-budget");
        workspace.WriteText("a.xml", "<r><!--Old--></r>");
        workspace.WriteText("b.xml", "<r><!--Old--></r>");
        // Each file: three nodes, five character units, 394 estimated bytes.
        var budget = new ReferenceReadBudget(maximumXmlCharacters: limit == "characters" ? 9 : 100,
            maximumNodes: limit == "nodes" ? 5 : 100,
            maximumEstimatedDomBytes: limit == "estimated" ? 787 : 10_000);
        var writes = 0;
        Assert.Throws<ReferenceResourceLimitException>(() => Prepare(kind,
            ModReferenceFileCatalog.Discover(workspace.RootPath), budget, () => writes++));
        Assert.Equal(0, writes);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)]
    public void NodeAndCharacterLimitsAcceptExactBoundaryWithAttributesAndTrivia(int headroom)
    {
        using var workspace = new TestWorkspace("reference-dom-boundary");
        var path = workspace.WriteText("r.xml", "<r a='v'><!--c--></r>");
        // start r + attribute a=v, comment c, end r: four nodes, five chars.
        var budget = new ReferenceReadBudget(maximumXmlCharacters: 5 + headroom, maximumNodes: 4 + headroom,
            maximumEstimatedDomBytes: 522 + headroom);
        var document = ModReferenceDocumentReader.Load(path, budget);
        Assert.Equal("<r a=\"v\"><!--c--></r>", document.ToString(SaveOptions.DisableFormatting));
    }

    [Fact]
    public void StreamLengthRecheckChargesGrowthWithoutDoubleChargingTheSameSource()
    {
        using var workspace = new TestWorkspace("reference-growth-budget");
        var path = workspace.WriteText("r.xml", "<r/>");
        var budget = new ReferenceReadBudget(maximumSourceBytes: 4);
        budget.AdmitFile(path);
        _ = ModReferenceDocumentReader.Load(path, budget);
        File.WriteAllText(path, "<root/>", new UTF8Encoding(false));
        Assert.Throws<ReferenceResourceLimitException>(() => ModReferenceDocumentReader.Load(path, budget));
    }

    [Fact]
    public void CompiledExpansionAndStructureAreBudgetedBeforeReturningDocument()
    {
        using var workspace = new TestWorkspace("reference-compiled-budget");
        var path = workspace.GetPath("r.tactics.xmb");
        File.WriteAllBytes(path, XmbReaderTests.BuildDocument(XElement.Parse("<tactics><action><name>Attack</name></action></tactics>")));
        Assert.Throws<ReferenceResourceLimitException>(() => ModReferenceDocumentReader.Load(path, new ReferenceReadBudget(maximumNodes: 2)));
        Assert.Throws<InvalidOperationException>(() => ModReferenceDocumentReader.Load(path, new ReferenceReadBudget(maximumXmlCharacters: 8)));
        Assert.Equal("tactics", ModReferenceDocumentReader.Load(path).Root!.Name.LocalName);
    }

    [Theory]
    [InlineData("technology")] [InlineData("unit")] [InlineData("asset")]
    public void OversizedSparseFileIsRefusedBeforeHashingOrParsing(string kind)
    {
        using var workspace = new TestWorkspace("reference-early-limit");
        var path = workspace.GetPath("large.xml");
        using (var stream = File.Create(path)) stream.SetLength(ModReferenceDocumentReader.MaximumFileBytes + 1);
        var error = Assert.Throws<InvalidOperationException>(() => Prepare(kind,
            ModReferenceFileCatalog.Discover(workspace.RootPath), new ReferenceReadBudget(), () => Assert.Fail("No writes permitted")));
        Assert.Contains("per-file bytes", error.Message);
    }

    [Fact]
    public void TechnologyChainsAndUnknownReferencesKeepTheirExistingSemantics()
    {
        using var workspace = new TestWorkspace("reference-budget-chains");
        var path = workspace.WriteText("proto_mods.xml", "<protomods><unit name='U'><tech>Old</tech><!--keep--></unit></protomods>");
        var files = ModReferenceFileCatalog.Discover(workspace.RootPath);
        var plan = TechnologyReferenceMigrationPlan.PrepareWithBudget(new ReferenceReadBudget(), files, [new("Old", "Middle"), new("Middle", "New")]);
        Assert.Empty(plan.UnknownReferences);
        Assert.Equal(2, plan.AffectedReferences.Count);
        Assert.Equal("New", plan.GetDocument(path)!.Descendants("tech").Single().Value);
        Assert.Contains("<!--keep-->", plan.GetDocument(path)!.ToString());
        File.WriteAllText(path, "<root><futuretech>Old</futuretech></root>");
        Assert.NotEmpty(TechnologyReferenceMigrationPlan.PrepareWithBudget(new ReferenceReadBudget(), files, [new("Old", "New")]).UnknownReferences);
    }

    [Theory]
    [InlineData("technology")] [InlineData("unit")] [InlineData("asset")]
    public void RetainedChangedDocumentsAreRecheckedAfterReplacementGrowth(string kind)
    {
        using var workspace = new TestWorkspace("reference-retained-budget");
        var tag = kind == "technology" ? "tech" : kind == "unit" ? "train" : "icon";
        var oldName = kind == "asset" ? "old.png" : "Old";
        var path = workspace.WriteText("proto_mods.xml", $"<protomods><unit name='U'><{tag}>{oldName}</{tag}></unit></protomods>");
        var before = File.ReadAllBytes(path);
        var files = ModReferenceFileCatalog.Discover(workspace.RootPath);
        var budget = new ReferenceReadBudget(maximumXmlCharacters: 128);
        var newName = new string('N', 256);
        var writes = 0;
        var error = Assert.Throws<ReferenceResourceLimitException>(() =>
        {
            if (kind == "technology") TechnologyReferenceMigrationPlan.PrepareWithBudget(budget, files, [new(oldName, newName)]).Save((_, _) => writes++);
            else if (kind == "unit") GameIdentifierReferenceMigrationPlan.PrepareWithBudget(budget, files, GameIdentifierKind.ProtoUnit, [new(oldName, newName)]).Save((_, _) => writes++);
            else AssetReferenceMigrationPlan.PrepareCore(files, AssetReferenceKind.Icon, oldName, newName + ".png", false, budget).Save((_, _) => writes++);
        });
        Assert.Contains("aggregate XML characters", error.Message);
        Assert.Equal(0, writes);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void HostBrowsingReportsIncompleteWhileDeletionAndOverridesRefuse()
    {
        using var fixture = new BaselineSafetyFixture();
        var before = File.ReadAllBytes(fixture.ProtoPath);
        AvaloniaTestHost.Run(() =>
        {
            var host = fixture.CreateHost(false);
            try
            {
                var usages = (IReadOnlyList<TechnologyReferenceUsage>)host.Call("GetExternalTechnologyReferencesCore", new ReferenceReadBudget(maximumNodes: 1))!;
                Assert.Empty(usages);
                var status = (TextBlock)typeof(ProtoEditorWindow).GetField("_statusMessage", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
                Assert.Contains("incomplete", status.Text);
                var overrides = new Dictionary<string, XDocument> { [fixture.ProtoPath] = XDocument.Parse("<protomods><unit name='Override'/></protomods>") };
                var error = Assert.Throws<TargetInvocationException>(() => host.Call("GetTechnologyDeletionReferencesCore",
                    "Old", overrides, new ReferenceReadBudget(maximumNodes: 1)));
                Assert.IsType<ReferenceResourceLimitException>(error.InnerException);
                Assert.Equal(before, File.ReadAllBytes(fixture.ProtoPath));
            }
            finally { host.DisposeFixtureWindow(); }
        });
    }

    [Fact]
    public void SecureReaderStillRejectsDtdAndPreservesUtf16CommentsWhitespace()
    {
        using var workspace = new TestWorkspace("reference-budget-reader");
        var path = workspace.WriteText("r.xml", "<!DOCTYPE r [<!ENTITY x 'value'>]><r>&x;</r>");
        Assert.Throws<XmlException>(() => ModReferenceDocumentReader.Load(path));
        File.WriteAllText(path, "<r>\r\n  <!--keep--><future a='x'>é</future>\r\n</r>", Encoding.Unicode);
        var original = XDocument.Load(path, LoadOptions.PreserveWhitespace);
        Assert.True(XNode.DeepEquals(original, ModReferenceDocumentReader.Load(path)));
    }
}
