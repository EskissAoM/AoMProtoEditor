using System.Text;
using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Windows;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class TypePersistenceTests
{
    private static async Task<bool> FinishAsync(BaselineHost host, Task<bool> operation)
    {
        // Use the existing headless owner/dialog pattern; do not automate the desktop.
        for (var attempt = 0; attempt < 200 && !operation.IsCompleted; attempt++)
        {
            foreach (var prompt in host.OwnedWindows.OfType<Prompt>().ToArray()) prompt.Close();
            await Task.Delay(10);
        }
        Assert.True(operation.IsCompleted, "The type operation did not finish after its error dialog closed.");
        return await operation;
    }

    [Theory]
    [InlineData(false, "modified")]
    [InlineData(true, "modified")]
    [InlineData(false, "deleted")]
    [InlineData(true, "deleted")]
    [InlineData(false, "appeared")]
    [InlineData(true, "appeared")]
    public void ActualTypeMutationRejectsExternalChangesAfterLoading(bool tech, string change)
    {
        using var fixture = new BaselineSafetyFixture();
        var path = tech ? fixture.Workspace.GetPath("TestMod/game/data/gameplay/tech_types_mods.xml") : fixture.TypePath;
        var root = tech ? "techtypesmods" : "abstractunittypesmods";
        var entry = tech ? "techtype" : "abstractunittype";
        File.WriteAllText(path, $"<{root}><{entry}>Original</{entry}></{root}>");
        if (change == "appeared") File.Delete(path);
        var external = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(
            $"<?xml version=\"1.0\" encoding=\"utf-16\"?><{root}><!-- external --><{entry}>External</{entry}></{root}>\r\n")).ToArray();
        AvaloniaTestHost.RunAsync(async () =>
        {
            var host = fixture.CreateHost(false);
            host.Show();
            var mutated = false;
            try
            {
                Action<XDocument> mutation = document =>
                {
                    if (tech) TechTypeCatalog.AddDefinition(document, "AddedType");
                    else UnitTypeCatalog.AddDefinition(document, "AddedType");
                    mutated = true;
                    if (change == "deleted") File.Delete(path);
                    else File.WriteAllBytes(path, external);
                };
                var task = (Task<bool>)host.Call(tech ? "MutateTechTypeDocumentAsync" : "MutateUnitTypeDocumentAsync", "create type", mutation)!;
                Assert.False(await FinishAsync(host, task));
                Assert.True(mutated);
                if (change == "deleted") Assert.False(File.Exists(path));
                else Assert.Equal(external, File.ReadAllBytes(path));
            }
            finally { host.DisposeFixtureWindow(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ActualManagerCreateRenameDeleteAndUnitDuplicateStillWork(bool tech)
    {
        using var fixture = new BaselineSafetyFixture();
        var path = tech ? fixture.Workspace.GetPath("TestMod/game/data/gameplay/tech_types_mods.xml") : fixture.TypePath;
        var treePath = fixture.Workspace.GetPath("TestMod/game/data/gameplay/techtree_mods.xml");
        if (tech) File.WriteAllText(path, "<techtypesmods><!-- keep --><future keep='yes'/></techtypesmods>");
        else File.WriteAllText(path, "<abstractunittypesmods><!-- keep --><abstractunittype spatialmapped='true'>CheckpointType</abstractunittype><future keep='yes'/></abstractunittypesmods>");
        AvaloniaTestHost.RunAsync(async () =>
        {
            var host = fixture.CreateHost(false);
            host.Show();
            var label = tech ? "TechType" : "UnitType";
            try
            {
                Assert.True(await FinishAsync(host, (Task<bool>)host.Call("Create" + label + "Async", "FreshType")!));
                var beforeCollision = File.ReadAllBytes(path);
                Assert.False(await FinishAsync(host, (Task<bool>)host.Call("Create" + label + "Async", "freshtype")!));
                Assert.Equal(beforeCollision, File.ReadAllBytes(path));
                if (tech) File.WriteAllText(treePath, "<techtreemods><tech name='UsesType'><techtype>FreshType</techtype><future keep='yes'/></tech></techtreemods>");
                Assert.True(await FinishAsync(host, (Task<bool>)host.Call("Rename" + label + "Async", "FreshType", "RenamedType")!));
                if (tech)
                {
                    var tree = XDocument.Load(treePath);
                    Assert.Equal("RenamedType", tree.Descendants("techtype").Single().Value);
                    Assert.Equal("yes", (string?)tree.Descendants("future").Single().Attribute("keep"));
                    // Remove the assignment externally before deleting the now-unused type.
                    tree.Descendants("techtype").Remove();
                    tree.Save(treePath);
                }
                else
                {
                    Assert.True(await FinishAsync(host, (Task<bool>)host.Call("DuplicateUnitTypeAsync", "CheckpointType", false, "ClonedType")!));
                    Assert.Equal("true", (string?)XDocument.Load(path).Descendants("abstractunittype")
                        .Single(element => element.Value == "ClonedType").Attribute("spatialmapped"));
                    Assert.True(await FinishAsync(host, (Task<bool>)host.Call("DeleteUnitTypeAsync", "ClonedType")!));
                }
                Assert.True(await FinishAsync(host, (Task<bool>)host.Call("Delete" + label + "Async", "RenamedType")!));
                var saved = XDocument.Load(path);
                Assert.DoesNotContain(saved.Root!.Elements(), element => element.Value == "RenamedType");
                Assert.Equal("yes", (string?)saved.Descendants("future").Single().Attribute("keep"));
                Assert.Contains("<!-- keep -->", File.ReadAllText(path));
            }
            finally { host.DisposeFixtureWindow(); }
        });
    }

    [Theory]
    [InlineData(false, "modified")]
    [InlineData(true, "modified")]
    [InlineData(false, "deleted")]
    [InlineData(true, "deleted")]
    [InlineData(false, "appeared")]
    [InlineData(true, "appeared")]
    public void PreparedRenameOrDeleteRefusesStaleTypeBeforeAnyRelatedWrite(bool tech, string change)
    {
        using var workspace = new TestWorkspace();
        var root = tech ? "techtypesmods" : "abstractunittypesmods";
        var entry = tech ? "techtype" : "abstractunittype";
        var path = workspace.WriteXml("types.xml", $"<{root}><{entry}>Original</{entry}></{root}>");
        if (change == "appeared") File.Delete(path);
        var prepared = ModDocumentPersistence.Prepare(path, tech ? ModXmlDocumentKind.TechTypes : ModXmlDocumentKind.UnitTypes);
        if (change == "appeared")
        {
            if (tech) TechTypeCatalog.AddDefinition(prepared.Document, "Added");
            else UnitTypeCatalog.AddDefinition(prepared.Document, "Added");
        }
        else if (change == "deleted")
        {
            if (tech) Assert.True(TechTypeCatalog.DeleteDefinition(prepared.Document, "Original"));
            else Assert.True(UnitTypeCatalog.DeleteDefinition(prepared.Document, "Original"));
        }
        else
        {
            if (tech) Assert.True(TechTypeCatalog.RenameDefinition(prepared.Document, "Original", "Renamed"));
            else Assert.True(UnitTypeCatalog.RenameDefinition(prepared.Document, "Original", "Renamed"));
        }
        var related = workspace.WriteXml("related.xml", "<root><!-- exact related bytes --></root>");
        var relatedBefore = File.ReadAllBytes(related);
        var expected = FileIntegrityTransaction.CaptureStates([related]);
        var external = Encoding.UTF8.GetBytes($"<{root}><!-- preserve external --><{entry}>External</{entry}></{root}>\r\n");
        if (change == "deleted") File.Delete(path);
        else File.WriteAllBytes(path, external);
        var wrote = false;
        Assert.Throws<IOException>(() => ModDocumentPersistence.Execute([prepared], [related], expected, () =>
        {
            wrote = true;
            if (tech) ProtoEditorWindow.SaveTechTypeXmlDocument(prepared.Document, path);
            else ProtoEditorWindow.SaveUnitTypeXmlDocument(prepared.Document, path);
            File.WriteAllText(related, "should never run");
        }));
        Assert.False(wrote);
        Assert.Equal(relatedBefore, File.ReadAllBytes(related));
        if (change == "deleted") Assert.False(File.Exists(path));
        else Assert.Equal(external, File.ReadAllBytes(path));
    }

    [Fact]
    public void TypeMutationCallbacksUsePreparedDocumentsAndRetainRenameReadDependencies()
    {
        var source = ProjectSource.Read("Windows", "ProtoEditorWindow.axaml.cs");
        foreach (var method in new[] { "MutateUnitTypeDocumentAsync", "MutateTechTypeDocumentAsync", "RenameUnitTypeAsync", "RenameTechTypeAsync", "DeleteUnitTypeAsync", "DeleteTechTypeAsync" })
        {
            var body = ProjectSource.ExtractMethod(source, "private async Task<bool> " + method);
            Assert.Contains("ModDocumentPersistence.Prepare(", body);
            Assert.Contains("ModDocumentPersistence.Execute(", body);
            Assert.DoesNotContain("LoadOrCreateModDocument(", body);
            if (method.StartsWith("Rename", StringComparison.Ordinal))
                Assert.Contains("expected[preparedType.Path] = preparedType.LoadedState;", body);
        }
        var techRename = ProjectSource.ExtractMethod(source, "private async Task<bool> RenameTechTypeAsync");
        Assert.Contains("expected[Path.GetFullPath(techtreePath)] = treeStateAfter;", techRename);
        var treeExpectedLine = techRename.IndexOf("expected[Path.GetFullPath(techtreePath)]", StringComparison.Ordinal);
        Assert.DoesNotContain("if (renamedReferences > 0)", techRename[techRename.IndexOf("expected[preparedType.Path]", StringComparison.Ordinal)..treeExpectedLine]);
    }
}
