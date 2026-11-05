using System.Text;
using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.GameData;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class ModStringReferenceInspectionTests
{
    private const string Id = "STR_UNIT_OLD_NAME";
    private static string Table => $"ID = \"{Id}\" ; Str = \"Éclaireur\"\r\nID = \"STR_KEEP\" ; Str = \"Keep\"\r\n";

    [Fact]
    public void BatchedInspectionUsesProposedDocumentsDraftsXmlTacticsAndSupportedTextButNotDefinitions()
    {
        using var workspace = new TestWorkspace("f07-b-sources");
        var owner = workspace.WriteText("owner.xml", "<root><label>STR_STALE</label></root>");
        var xml = workspace.WriteText("other.xml", "<root label='STR_ATTR'><text>STR_LEAF</text></root>");
        var tactics = workspace.WriteText("unit.tactics", "<tactics><label>STR_TACTICS</label></tactics>");
        var text = workspace.WriteText("custom.txt", "label = STR_CUSTOM", Encoding.Unicode);
        var strings = workspace.WriteText("stringmods.txt", "ID = \"STR_DEFINITION\" ; Str = \"text\"");
        workspace.WriteText("ignored.json", "STR_UNSUPPORTED");
        var futureOwner = workspace.GetPath("future.xml");
        var inspection = ModDeletionSafety.InspectStringReferences(workspace.RootPath,
            ["STR_STALE", "STR_CURRENT", "STR_ATTR", "STR_LEAF", "STR_TACTICS", "STR_CUSTOM",
                "STR_DRAFT", "STR_FUTURE", "STR_DEFINITION", "STR_UNUSED", "STR_UNSUPPORTED"],
            new Dictionary<string, XDocument>
            {
                [owner] = XDocument.Parse("<root label='STR_CURRENT'/>"),
                [futureOwner] = XDocument.Parse("<root><label>STR_FUTURE</label></root>")
            }, [XDocument.Parse("<root><label>STR_DRAFT</label></root>")]);

        Assert.Equal(7, inspection.ReferencedIds.Count);
        foreach (var id in new[] { "STR_CURRENT", "STR_ATTR", "STR_LEAF", "STR_TACTICS", "STR_CUSTOM", "STR_DRAFT", "STR_FUTURE" })
            Assert.Contains(id, inspection.ReferencedIds);
        Assert.DoesNotContain("STR_STALE", inspection.ReferencedIds);
        Assert.DoesNotContain("STR_DEFINITION", inspection.ReferencedIds);
        Assert.DoesNotContain("STR_UNSUPPORTED", inspection.ReferencedIds);
        foreach (var path in new[] { owner, xml, tactics, text }) Assert.True(inspection.ExpectedStates[path].Exists);
        Assert.False(inspection.ExpectedStates[futureOwner].Exists);
        Assert.Contains(strings, inspection.DiscoveredPaths);
        Assert.DoesNotContain(strings, inspection.ExpectedStates.Keys);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AllProposedOwnersParticipateInTheSameCleanupDecision(bool keepSecondOwner)
    {
        using var workspace = new TestWorkspace("f07-b-proposed-owners");
        var first = workspace.WriteText("first.xml", $"<root><label>{Id}</label></root>");
        var second = workspace.WriteText("second.xml", $"<root label='{Id}'/>");
        var inspection = ModDeletionSafety.InspectStringReferences(workspace.RootPath, [Id],
            new Dictionary<string, XDocument>
            {
                [first] = XDocument.Parse("<root/>"),
                [second] = XDocument.Parse(keepSecondOwner ? $"<root label='{Id}'/>" : "<root/>")
            });
        var plan = OwnedStringLifecyclePolicy.Prepare([], [Id], new HashSet<string> { Id },
            new Dictionary<string, string> { [Id] = "Éclaireur" }, new Dictionary<string, string>(),
            inspection.ReferencedIds, new HashSet<string>());

        Assert.Equal(!keepSecondOwner, plan.StringRemovals.Contains(Id));
    }

    [Theory]
    [InlineData(".xml")]
    [InlineData(".tactics")]
    public void EncodedAndSplitXmlReferencesAreRetained(string extension)
    {
        using var workspace = new TestWorkspace("f07-b-encoded");
        workspace.WriteText("custom" + extension,
            "<root label='STR_&#65;TTR|OTHER'><label>prefix STR_<![CDATA[SPLIT]]> suffix</label>" +
            "<text>STR_LONG_SUFFIX</text></root>");
        var inspection = ModDeletionSafety.InspectStringReferences(workspace.RootPath, ["STR_ATTR", "STR_SPLIT", "STR_LONG"]);
        Assert.Contains("STR_ATTR", inspection.ReferencedIds);
        Assert.Contains("STR_SPLIT", inspection.ReferencedIds);
        Assert.DoesNotContain("STR_LONG", inspection.ReferencedIds);
    }

    [Theory]
    [InlineData("edit")]
    [InlineData("delete")]
    [InlineData("move")]
    [InlineData("new-xml")]
    [InlineData("new-tactics")]
    [InlineData("new-text")]
    public void LateChangesRefuseBeforeAnyTransactionWriteAndPreserveExternalChanges(string change)
    {
        using var workspace = new TestWorkspace("f07-b-late");
        var owner = workspace.WriteText("owner.xml", "<root/>");
        var strings = workspace.WriteText("stringmods.txt", Table);
        var metadata = workspace.WriteText("bindings.json", "keep metadata");
        var reference = workspace.WriteText("existing.xml", "<root/>");
        var inspection = ModDeletionSafety.InspectStringReferences(workspace.RootPath, [Id]);
        var before = new[] { owner, strings, metadata }.ToDictionary(path => path, File.ReadAllBytes);
        switch (change)
        {
            case "edit": File.WriteAllText(reference, $"<root label='{Id}'/>"); break;
            case "delete": File.Delete(reference); break;
            case "move": File.Move(reference, workspace.GetPath("moved.xml")); break;
            default:
                workspace.WriteText("nested/late" + (change == "new-xml" ? ".xml" : change == "new-tactics" ? ".tactics" : ".txt"),
                    change == "new-text" ? Id : $"<root label='{Id}'/>");
                break;
        }
        var external = FileIntegrityTransaction.CaptureStates(Directory.EnumerateFiles(workspace.RootPath, "*", SearchOption.AllDirectories));
        var wrote = false;

        Assert.Throws<IOException>(() => FileIntegrityTransaction.Execute(before.Keys, () =>
        {
            inspection.ValidateReadDependencies();
            wrote = true;
            foreach (var path in before.Keys) File.WriteAllText(path, "unexpected write");
        }));

        Assert.False(wrote);
        foreach (var entry in before) Assert.Equal(entry.Value, File.ReadAllBytes(entry.Key));
        foreach (var entry in external) Assert.Equal(entry.Value, FileIntegrityTransaction.CaptureState(entry.Key));
        if (change is "delete" or "move") Assert.False(File.Exists(reference));
    }

    [Theory]
    [InlineData("proposed-value")]
    [InlineData("proposed-added")]
    [InlineData("draft-value")]
    [InlineData("draft-added")]
    [InlineData("exclusion-added")]
    public void ProposedOrPendingInputChangesInvalidateTheInspection(string change)
    {
        using var workspace = new TestWorkspace("f07-b-draft-change");
        var path = workspace.WriteText("owner.xml", "<root/>");
        var overrides = new Dictionary<string, XDocument> { [path] = XDocument.Parse("<root/>") };
        var drafts = new List<XDocument> { XDocument.Parse("<root/>") };
        var excluded = new List<string>();
        var inspection = ModDeletionSafety.InspectStringReferences(workspace.RootPath, [Id], overrides, drafts, excluded);
        switch (change)
        {
            case "proposed-value": overrides[path].Root!.SetAttributeValue("label", Id); break;
            case "proposed-added": overrides[workspace.GetPath("future.xml")] = XDocument.Parse("<root/>"); break;
            case "draft-value": drafts[0].Root!.SetAttributeValue("label", Id); break;
            case "draft-added": drafts.Add(XDocument.Parse($"<root label='{Id}'/>")); break;
            default: excluded.Add(path); break;
        }

        Assert.Throws<IOException>(inspection.ValidateReadDependencies);
        Assert.Equal("<root/>", File.ReadAllText(path));
    }

    [Fact]
    public void PendingDraftReferenceProtectsAKeyWithoutReplacingTheProposedOwner()
    {
        using var workspace = new TestWorkspace("f07-b-draft-retain");
        var path = workspace.WriteText("owner.xml", $"<root label='{Id}'/>");
        var inspection = ModDeletionSafety.InspectStringReferences(workspace.RootPath, [Id],
            new Dictionary<string, XDocument> { [path] = XDocument.Parse("<root/>") },
            [XDocument.Parse($"<draft><label>{Id}</label></draft>")]);
        Assert.Contains(Id, inspection.ReferencedIds);
        Assert.Equal($"<root label='{Id}'/>", File.ReadAllText(path));
    }

    [Fact]
    public void FilesCreatedDuringInputPreparationAreNotAcceptedAsANewBaseline()
    {
        using var workspace = new TestWorkspace("f07-b-during-preparation");
        IEnumerable<XDocument> Drafts()
        {
            workspace.WriteText("late.txt", Id);
            yield return XDocument.Parse("<root/>");
        }
        Assert.Throws<IOException>(() => ModDeletionSafety.InspectStringReferences(workspace.RootPath, [Id], pendingDrafts: Drafts()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InspectionFailureIsNotTreatedAsProofThatAKeyIsUnused(bool locked)
    {
        using var workspace = new TestWorkspace("f07-b-unreadable");
        var path = workspace.WriteText(locked ? "locked.txt" : "malformed.xml", locked ? "unrelated" : "<root>");
        using var fileLock = locked ? new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None) : null;
        Assert.Throws<InvalidOperationException>(() => ModDeletionSafety.InspectStringReferences(workspace.RootPath, [Id]));
    }

    [Fact]
    public void MissingRootIsNotAnEmptySuccessfulInspectionAndEmptyExistingRootWorks()
    {
        using var workspace = new TestWorkspace("f07-b-root");
        Assert.ThrowsAny<IOException>(() => ModDeletionSafety.InspectStringReferences(workspace.GetPath("missing"), [Id]));
        var inspection = ModDeletionSafety.InspectStringReferences(workspace.RootPath, [Id]);
        Assert.Empty(inspection.ReferencedIds);
        Assert.Empty(inspection.ExpectedStates);
        Assert.Empty(inspection.DiscoveredPaths);
    }

    [Fact]
    public void MergeRejectsConflictingBaselinesWithoutReplacingOrPartiallyAddingObservations()
    {
        using var workspace = new TestWorkspace("f07-b-merge-conflict");
        var first = workspace.WriteText("a.xml", "<root/>");
        workspace.WriteText("b.xml", "<root/>");
        var old = FileIntegrityTransaction.CaptureState(first);
        File.AppendAllText(first, "<!--external-->");
        var inspection = ModDeletionSafety.InspectStringReferences(workspace.RootPath, [Id]);
        var destination = new Dictionary<string, FileIntegrityTransaction.FileState> { [first.ToUpperInvariant()] = old };

        Assert.Throws<IOException>(() => inspection.MergeExpectedStatesInto(destination));
        Assert.Single(destination);
        Assert.Equal(old, destination[first.ToUpperInvariant()]);
    }

    [Fact]
    public void MatchingBaselinesCanBeMergedAndUnknownExtensionsDoNotInvalidateTheResult()
    {
        using var workspace = new TestWorkspace("f07-b-merge-stable");
        var path = workspace.WriteText("reference.txt", "unrelated");
        var inspection = ModDeletionSafety.InspectStringReferences(workspace.RootPath, [Id]);
        var destination = new Dictionary<string, FileIntegrityTransaction.FileState>
        { [path.ToUpperInvariant()] = FileIntegrityTransaction.CaptureState(path) };
        inspection.MergeExpectedStatesInto(destination);
        Assert.Single(destination);
        workspace.WriteText("unrelated.json", Id);
        inspection.ValidateReadDependencies();
    }

    [Fact]
    public void ProposedMissingFileRetainsExpectedAbsence()
    {
        using var workspace = new TestWorkspace("f07-b-missing-owner");
        var path = workspace.GetPath("future.xml");
        var inspection = ModDeletionSafety.InspectStringReferences(workspace.RootPath, [Id],
            new Dictionary<string, XDocument> { [path] = XDocument.Parse("<root/>") });
        Assert.False(inspection.ExpectedStates[path].Exists);
        File.WriteAllText(path, "<root/>");
        Assert.Throws<IOException>(inspection.ValidateReadDependencies);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GuardedPolicyAndExistingWriterCommitOrRollBackTogether(bool fail)
    {
        using var workspace = new TestWorkspace("f07-b-transaction");
        var owner = workspace.WriteText("owner.xml", $"<root label='{Id}'/>");
        var strings = workspace.WriteText("stringmods.txt", Table);
        var metadata = workspace.WriteText("bindings.json", "keep metadata");
        var proposed = XDocument.Parse("<root/>");
        var inspection = ModDeletionSafety.InspectStringReferences(workspace.RootPath, [Id],
            new Dictionary<string, XDocument> { [owner] = proposed }, excludedPaths: [strings]);
        var table = StringTableDocument.Parse(Table);
        var plan = OwnedStringLifecyclePolicy.Prepare([], [Id], new HashSet<string> { Id }, table.ToDictionary(),
            new Dictionary<string, string>(), inspection.ReferencedIds, new HashSet<string>());
        var before = new[] { owner, strings, metadata }.ToDictionary(path => path, File.ReadAllBytes);
        var expected = FileIntegrityTransaction.CaptureStates(before.Keys).ToDictionary(entry => entry.Key, entry => entry.Value);
        inspection.MergeExpectedStatesInto(expected);

        var error = Record.Exception(() => FileIntegrityTransaction.Execute(before.Keys, expected, () =>
        {
            inspection.ValidateReadDependencies();
            PreservedXmlFile.SaveAtomic(proposed, owner);
            File.WriteAllText(strings, table.Apply(plan.StringUpdates, plan.StringRemovals));
            File.WriteAllText(metadata, "new metadata");
            if (fail) throw new IOException("Injected after writes");
        }));

        if (fail)
        {
            Assert.IsType<IOException>(error);
            foreach (var entry in before) Assert.Equal(entry.Value, File.ReadAllBytes(entry.Key));
        }
        else
        {
            Assert.Null(error);
            var saved = StringTableDocument.Parse(File.ReadAllText(strings)).ToDictionary();
            Assert.False(saved.ContainsKey(Id));
            Assert.Equal("Keep", saved["STR_KEEP"]);
            Assert.Null(XDocument.Load(owner).Root!.Attribute("label"));
        }
    }
}
