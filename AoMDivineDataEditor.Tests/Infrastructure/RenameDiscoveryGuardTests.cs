using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class RenameDiscoveryGuardTests
{
    private sealed record PreparedRename(
        IReadOnlyList<string> Paths,
        IReadOnlyDictionary<string, FileIntegrityTransaction.FileState> States,
        Action Validate,
        Action Save);

    private static PreparedRename Prepare(bool technology, IEnumerable<TechnologyReferenceFile> files,
        FileDiscoverySnapshot discovery, bool noOperations = false)
    {
        if (technology)
        {
            var plan = TechnologyReferenceMigrationPlan.Prepare(files,
                noOperations ? [] : [new("Old", "New")], discovery);
            Assert.Empty(plan.UnknownReferences);
            return new(plan.Paths, plan.ExpectedStates, plan.ValidateDiscovery,
                () => plan.Save((document, path) => PreservedXmlFile.SaveAtomic(document, path)));
        }
        var identifierPlan = GameIdentifierReferenceMigrationPlan.Prepare(files, GameIdentifierKind.ProtoUnit,
            noOperations ? [] : [new("Old", "New")], discovery: discovery);
        return new(identifierPlan.Paths, identifierPlan.ExpectedStates, identifierPlan.ValidateDiscovery,
            () => identifierPlan.Save((document, path) => PreservedXmlFile.SaveAtomic(document, path)));
    }

    private static string Reference(bool technology) => technology
        ? "<proto><unit name='U'><tech>Old</tech></unit></proto>"
        : "<proto><unit name='U'><train>Old</train></unit></proto>";

    [Theory]
    [InlineData(true, "xml")]
    [InlineData(true, "tactics")]
    [InlineData(true, "move")]
    [InlineData(false, "xml")]
    [InlineData(false, "tactics")]
    [InlineData(false, "move")]
    public void DiscoveryChangesRefuseBeforeOwnerReferencesStringsOrMetadataAreWritten(bool technology, string change)
    {
        using var workspace = new TestWorkspace("f06-b-late-discovery");
        var owner = workspace.WriteText("owner.xml", "<root name='Old'/>");
        var reference = workspace.WriteText("proto_mods.xml", Reference(technology));
        var strings = workspace.WriteText("stringmods.txt", "keep strings");
        var metadata = workspace.WriteText("bindings.json", "keep metadata");
        var files = ModReferenceFileCatalog.Discover(workspace.RootPath, out var discovery);
        var plan = Prepare(technology, files.Where(file => file.Path != owner), discovery);
        Assert.Equal(reference, Assert.Single(plan.Paths));
        Assert.DoesNotContain(owner, plan.States.Keys);
        var paths = new[] { owner, reference, strings, metadata };
        var before = paths.ToDictionary(path => path, File.ReadAllBytes);
        var late = change == "move" ? workspace.GetPath("moved-owner.xml") :
            workspace.WriteText(change == "xml" ? "late.xml" : "nested/late.tactics", "<root>Old</root>");
        if (change == "move") File.Move(owner, late);
        var external = File.ReadAllBytes(late);
        var wrote = false;

        Assert.Throws<IOException>(() => FileIntegrityTransaction.Execute(paths, plan.States, () =>
        {
            plan.Validate();
            wrote = true;
            File.WriteAllText(owner, "<root name='New'/>");
            File.WriteAllText(strings, "new strings");
            File.WriteAllText(metadata, "new metadata");
            plan.Save();
        }));

        Assert.False(wrote);
        foreach (var path in paths.Where(path => path != owner || change != "move"))
            Assert.Equal(before[path], File.ReadAllBytes(path));
        if (change == "move") Assert.False(File.Exists(owner));
        Assert.Equal(external, File.ReadAllBytes(late));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FilesAppearingDuringPreparationAreRejected(bool technology)
    {
        using var workspace = new TestWorkspace("f06-b-during-prepare");
        var reference = workspace.WriteText("proto_mods.xml", Reference(technology));
        var before = File.ReadAllBytes(reference);
        var files = ModReferenceFileCatalog.Discover(workspace.RootPath, out var discovery);
        IEnumerable<TechnologyReferenceFile> ChangingFiles()
        {
            foreach (var file in files) yield return file;
            workspace.WriteText("late.xml", "<root>Old</root>");
        }

        Assert.Throws<IOException>(() => Prepare(technology, ChangingFiles(), discovery));
        Assert.Equal(before, File.ReadAllBytes(reference));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public void StableRenameCommitsOrRollsBackAndNeverWritesUnchangedSources(bool technology, bool fail)
    {
        using var workspace = new TestWorkspace("f06-b-stable");
        var reference = workspace.WriteText("proto_mods.xml", Reference(technology));
        var untouched = workspace.WriteText("untouched.xml", "<root><!--preserve exactly--></root>");
        var owner = workspace.WriteText("owner.xml", "<root name='Old'/>");
        var files = ModReferenceFileCatalog.Discover(workspace.RootPath, out var discovery);
        var plan = Prepare(technology, files.Where(file => file.Path != owner), discovery);
        var before = new[] { reference, untouched, owner }.ToDictionary(path => path, File.ReadAllBytes);
        Assert.Equal(reference, Assert.Single(plan.Paths));
        Assert.Contains(untouched, plan.States.Keys);

        var error = Record.Exception(() => FileIntegrityTransaction.Execute(plan.Paths.Append(owner), plan.States, () =>
        {
            plan.Validate();
            File.WriteAllText(owner, "<root name='New'/>");
            plan.Save();
            if (fail) throw new IOException("Injected after rename writes");
        }));

        Assert.Equal(before[untouched], File.ReadAllBytes(untouched));
        if (fail)
        {
            Assert.IsType<IOException>(error);
            foreach (var entry in before) Assert.Equal(entry.Value, File.ReadAllBytes(entry.Key));
        }
        else
        {
            Assert.Null(error);
            Assert.Equal("New", XDocument.Load(reference).Descendants(technology ? "tech" : "train").Single().Value);
            Assert.Equal("New", (string?)XDocument.Load(owner).Root!.Attribute("name"));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExplicitMissingInputsKeepExpectedAbsence(bool technology)
    {
        using var workspace = new TestWorkspace("f06-b-missing-input");
        var files = ModReferenceFileCatalog.Discover(workspace.RootPath, out var discovery).ToList();
        var missing = workspace.GetPath("known-future.xml");
        files.Add(new(missing, TechnologyReferenceDocumentKind.Other, "Known absent source"));
        var plan = Prepare(technology, files, discovery);
        Assert.False(plan.States[missing].Exists);
        File.WriteAllText(missing, "<root>Old</root>");
        var wrote = false;

        Assert.Throws<IOException>(() => FileIntegrityTransaction.Execute(plan.Paths, plan.States, () => wrote = true));
        Assert.False(wrote);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EmptyMigrationStillRetainsItsOriginalDiscovery(bool technology)
    {
        using var workspace = new TestWorkspace("f06-b-empty-plan");
        var files = ModReferenceFileCatalog.Discover(workspace.RootPath, out var discovery);
        var plan = Prepare(technology, files, discovery, noOperations: true);
        Assert.Empty(plan.Paths);
        workspace.WriteText("late.xml", "<root/>");

        Assert.Throws<IOException>(plan.Validate);
    }
}
