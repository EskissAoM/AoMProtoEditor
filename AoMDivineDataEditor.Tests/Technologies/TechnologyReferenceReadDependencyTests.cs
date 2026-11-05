using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class TechnologyReferenceReadDependencyTests
{
    [Theory]
    [InlineData("unchanged-edited")]
    [InlineData("unchanged-deleted")]
    [InlineData("changed-edited")]
    [InlineData("missing-created")]
    [InlineData("no-op-edited")]
    public void ReadDependencyChangesRefuseBeforeAnyWrite(string scenario)
    {
        using var workspace = new TestWorkspace("f06-read-dependencies");
        var owner = workspace.WriteText("techtree_mods.xml", "<techtreemods><tech name='Old'/></techtreemods>");
        var referenced = workspace.WriteText("proto_mods.xml", "<proto><unit name='U'><tech>Old</tech></unit></proto>");
        var unrelated = workspace.WriteText("powers_mods.xml", "<powers><power name='P'/></powers>");
        var missing = workspace.GetPath("future.xml");
        var files = ModReferenceFileCatalog.Discover(workspace.RootPath).Where(f => f.Path != owner).ToList();
        files.Add(new(missing, TechnologyReferenceDocumentKind.Other, "Known missing source"));
        var plan = TechnologyReferenceMigrationPlan.Prepare(files,
            [new(scenario == "no-op-edited" ? "Absent" : "Old", "New")]);
        Assert.Equal(3, plan.ExpectedStates.Count);
        Assert.DoesNotContain(unrelated, plan.Paths);
        Assert.DoesNotContain(missing, plan.Paths);
        var ownerBefore = File.ReadAllBytes(owner);
        switch (scenario)
        {
            case "unchanged-deleted": File.Delete(unrelated); break;
            case "changed-edited": File.AppendAllText(referenced, "<!--external-->"); break;
            case "missing-created": File.WriteAllText(missing, "<root><tech>Old</tech></root>"); break;
            default: File.WriteAllText(unrelated, "<powers><power name='P'><tech>Old</tech></power></powers>"); break;
        }
        var externalStates = FileIntegrityTransaction.CaptureStates(files.Select(f => f.Path));
        var committed = false;
        Assert.Throws<IOException>(() => FileIntegrityTransaction.Execute(
            plan.Paths.Append(owner), plan.ExpectedStates, () =>
            {
                committed = true;
                File.WriteAllText(owner, "<techtreemods><tech name='New'/></techtreemods>");
                plan.Save((doc, path) => PreservedXmlFile.SaveAtomic(doc, path));
            }));
        Assert.False(committed);
        Assert.Equal(ownerBefore, File.ReadAllBytes(owner));
        foreach (var entry in externalStates)
            Assert.Equal(entry.Value, FileIntegrityTransaction.CaptureState(entry.Key));
    }

    [Fact]
    public void StableUnchangedSourcesAreGuardedButNeverWritten()
    {
        using var workspace = new TestWorkspace("f06-stable");
        var changed = workspace.WriteText("proto_mods.xml", "<proto><unit name='U'><tech>Old</tech></unit></proto>");
        var untouched = workspace.WriteText("powers_mods.xml", "<powers><!--keep formatting--><power name='P' /></powers>");
        var before = File.ReadAllBytes(untouched);
        var plan = TechnologyReferenceMigrationPlan.Prepare(ModReferenceFileCatalog.Discover(workspace.RootPath), [new("Old", "New")]);
        Assert.Equal(2, plan.ExpectedStates.Count);
        Assert.Equal(changed, Assert.Single(plan.Paths));
        var writes = new List<string>();
        FileIntegrityTransaction.Execute(plan.Paths, plan.ExpectedStates, () => plan.Save((doc, path) =>
        {
            writes.Add(path);
            PreservedXmlFile.SaveAtomic(doc, path);
        }));
        Assert.Equal(changed, Assert.Single(writes));
        Assert.Equal(before, File.ReadAllBytes(untouched));
        Assert.Equal("New", XDocument.Load(changed).Descendants("tech").Single().Value);
    }
}
