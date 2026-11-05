using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.GameData;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class OwnedStringLifecyclePolicyTests
{
    private const string Old = "STR_TECH_OLD_NAME";
    private const string New = "STR_TECH_NEW_NAME";
    private const string Translation = "  Éclaireur — 神\n{0}  ";
    private static HashSet<string> Set(params string[] ids) => ids.ToHashSet(StringComparer.OrdinalIgnoreCase);
    private static Dictionary<string, string> Entries(string value = Translation) => new() { [Old] = value };

    private static OwnedStringLifecyclePlan Rename(
        IReadOnlyDictionary<string, string>? entries = null,
        IReadOnlyDictionary<string, string>? values = null,
        IReadOnlySet<string>? referenced = null,
        IReadOnlySet<string>? reserved = null) => OwnedStringLifecyclePolicy.Prepare(
            [new("displaynameid", Old, New)], [], Set(Old), entries ?? Entries(),
            values ?? new Dictionary<string, string>(), referenced ?? Set(New), reserved ?? Set());

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void RenameCopiesExactTextAndRetainsSharedSourceWithoutUpdatingOtherUsers(bool shared, bool edited)
    {
        var entries = Entries();
        var editedText = "Edited — traduction\n{1}";
        var plan = Rename(entries, edited ? new Dictionary<string, string> { [Old] = editedText } : null,
            referenced: shared ? Set(Old, New) : Set(New));

        Assert.Equal(edited ? editedText : Translation, plan.StringUpdates[New]);
        Assert.False(plan.StringUpdates.ContainsKey(Old));
        Assert.Equal(!shared, plan.StringRemovals.Contains(Old));
        Assert.Equal(new OwnedStringRename("displaynameid", Old, New), Assert.Single(plan.ReferenceRenames));
        if (shared) Assert.Equal(OwnedStringRetentionReason.ReferencedByResult, plan.RetainedStrings[Old]);
        Assert.Equal(Translation, entries[Old]);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    public void DeletionRequiresVerifiedOwnershipAndNoRemainingReferenceOrPendingClaim(bool owned, bool referenced, bool reserved)
    {
        var plan = OwnedStringLifecyclePolicy.Prepare([], [Old], owned ? Set(Old) : Set(), Entries(),
            new Dictionary<string, string>(), referenced ? Set(Old.ToLowerInvariant()) : Set(),
            reserved ? Set(Old) : Set());

        Assert.Equal(owned && !referenced && !reserved, plan.StringRemovals.Contains(Old));
        Assert.Empty(plan.StringUpdates);
        if (!owned) Assert.Equal(OwnedStringRetentionReason.UnverifiedOwnership, plan.RetainedStrings[Old]);
        else if (referenced) Assert.Equal(OwnedStringRetentionReason.ReferencedByResult, plan.RetainedStrings[Old]);
        else if (reserved) Assert.Equal(OwnedStringRetentionReason.ReservedByPendingEdit, plan.RetainedStrings[Old]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExistingDestinationIsNeverOverwrittenEvenIfTextMatches(bool sameValue)
    {
        var entries = Entries();
        entries[New.ToLowerInvariant()] = sameValue ? Translation : "Other owner's text";

        var error = Assert.Throws<InvalidOperationException>(() => Rename(entries));

        Assert.Contains("Choose another name", error.Message);
        Assert.Equal(Translation, entries[Old]);
        Assert.Equal(sameValue ? Translation : "Other owner's text", entries[New.ToLowerInvariant()]);
    }

    [Fact]
    public void PendingDestinationClaimBlocksRenameAndPendingSourceClaimPreventsRemoval()
    {
        Assert.Throws<InvalidOperationException>(() => Rename(reserved: Set(New.ToLowerInvariant())));
        var plan = Rename(reserved: Set(Old));
        Assert.Equal(Translation, plan.StringUpdates[New]);
        Assert.Empty(plan.StringRemovals);
        Assert.Equal(OwnedStringRetentionReason.ReservedByPendingEdit, plan.RetainedStrings[Old]);
    }

    [Fact]
    public void GeneratedLookingButUnverifiedKeysRemainUnchanged()
    {
        var plan = OwnedStringLifecyclePolicy.Prepare([new("displaynameid", Old, New)], [Old], Set(),
            Entries(), new Dictionary<string, string>(), Set(), Set());

        Assert.Empty(plan.ReferenceRenames);
        Assert.Empty(plan.StringUpdates);
        Assert.Empty(plan.StringRemovals);
        Assert.Equal(OwnedStringRetentionReason.UnverifiedOwnership, plan.RetainedStrings[Old]);
    }

    [Fact]
    public void SharedFieldsRemainLinkedAndDuplicateRequestsDoNotDuplicateWrites()
    {
        var plan = OwnedStringLifecyclePolicy.Prepare(
            [new("displaynameid", Old, New), new("editornameid", Old, New), new("editornameid", Old, New)],
            [Old], Set(Old), Entries(), new Dictionary<string, string>(), Set(New), Set());

        Assert.Equal(2, plan.ReferenceRenames.Count);
        Assert.All(plan.ReferenceRenames, rename => Assert.Equal(New, rename.NewId));
        Assert.Single(plan.StringUpdates);
        Assert.Equal(Old, Assert.Single(plan.StringRemovals));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AmbiguousSplitsAndMergesAreRejectedWithoutChangingInputs(bool split)
    {
        var entries = Entries();
        entries["STR_OTHER"] = Translation;
        OwnedStringRename[] renames = split ?
            [new("displaynameid", Old, New), new("editornameid", Old, "STR_SECOND_DESTINATION")] :
            [new("displaynameid", Old, New), new("editornameid", "STR_OTHER", New)];

        Assert.Throws<InvalidOperationException>(() => OwnedStringLifecyclePolicy.Prepare(renames, [],
            Set(Old, "STR_OTHER"), entries, new Dictionary<string, string>(), Set(), Set()));
        Assert.Equal(2, entries.Count);
        Assert.Equal(Translation, entries[Old]);
    }

    [Fact]
    public void UnchangedIdentityAndRenameBackToOriginalDoNotRemoveTheOriginalKey()
    {
        var plan = OwnedStringLifecyclePolicy.Prepare([new("displaynameid", Old, Old.ToLowerInvariant())],
            [Old], Set(Old), Entries(), new Dictionary<string, string>(), Set(), Set());

        Assert.Empty(plan.ReferenceRenames);
        Assert.Empty(plan.StringUpdates);
        Assert.Empty(plan.StringRemovals);
        Assert.Equal(OwnedStringRetentionReason.UnchangedIdentity, plan.RetainedStrings[Old]);
    }

    [Fact]
    public void MissingSourceDoesNotInventTextOrApproveADanglingNewReference()
    {
        var plan = Rename(entries: new Dictionary<string, string>());
        Assert.Empty(plan.ReferenceRenames);
        Assert.Empty(plan.StringUpdates);
        Assert.Empty(plan.StringRemovals);
        Assert.Equal(OwnedStringRetentionReason.MissingSourceValue, plan.RetainedStrings[Old]);
    }

    [Fact]
    public void ResolvedBaseTextCanBeCopiedWithoutCreatingAnAbsentOldDefinition()
    {
        var plan = Rename(entries: new Dictionary<string, string>(), values: Entries());
        Assert.Equal(Translation, plan.StringUpdates[New]);
        Assert.Single(plan.ReferenceRenames);
        Assert.Empty(plan.StringRemovals);
        Assert.Equal(OwnedStringRetentionReason.NotInStringTable, plan.RetainedStrings[Old]);
    }

    [Fact]
    public void EmptyOptionalFieldsProduceNoLifecycleChanges()
    {
        var plan = OwnedStringLifecyclePolicy.Prepare([], [], Set(Old), Entries(),
            new Dictionary<string, string>(), Set(), Set());
        Assert.Empty(plan.ReferenceRenames);
        Assert.Empty(plan.StringUpdates);
        Assert.Empty(plan.StringRemovals);
        Assert.Empty(plan.RetainedStrings);
    }

    [Fact]
    public void ExistingMajorGodOwnershipHelpersExcludeCustomIdsAndKeepCallerXmlUntouched()
    {
        var original = XElement.Parse("<civ><name>Old</name><displaynameid>STR_CIV_OLD</displaynameid><titleid>STR_CUSTOM</titleid></civ>");
        var copy = new XElement(original);
        var owned = MajorGodCatalog.CollectOwnedStringIds(original);
        var renames = MajorGodCatalog.RenameOwnedResolvedStringIds(copy, "Old", "New");
        var entries = new Dictionary<string, string> { ["STR_CIV_OLD"] = Translation, ["STR_CUSTOM"] = "Custom" };
        var plan = OwnedStringLifecyclePolicy.Prepare(renames, ["STR_CUSTOM"], owned, entries,
            new Dictionary<string, string>(), Set("STR_CIV_NEW"), Set());

        Assert.Equal("STR_CIV_OLD", original.Element("displaynameid")!.Value);
        Assert.Equal(Translation, plan.StringUpdates["STR_CIV_NEW"]);
        Assert.Equal("STR_CIV_OLD", Assert.Single(plan.StringRemovals));
        Assert.Equal(OwnedStringRetentionReason.UnverifiedOwnership, plan.RetainedStrings["STR_CUSTOM"]);
    }

    [Fact]
    public void ExistingCultureHelperSuppliesItsCanonicalMapping()
    {
        var original = XElement.Parse("<culture><name>Old</name><displaynameid>STR_CULTURE_OLD</displaynameid></culture>");
        var id = original.Element("displaynameid")!.Value;
        Assert.True(CultureCatalog.OwnsDisplayNameId("Old", id));
        var change = CultureCatalog.RenameOwnedDisplayNameId(new XElement(original), "Old", "New")!;
        var plan = OwnedStringLifecyclePolicy.Prepare([change], [], Set(id),
            new Dictionary<string, string> { [id] = Translation }, new Dictionary<string, string>(), Set(change.NewId), Set());
        Assert.Equal(Translation, plan.StringUpdates[change.NewId]);
        Assert.Equal(id, Assert.Single(plan.StringRemovals));
    }

    [Fact]
    public void PolicyNormalizesIdsButPreservesValuesAndSnapshotsItsInputs()
    {
        var entries = Entries();
        var values = new Dictionary<string, string> { [Old.ToLowerInvariant()] = "  exact text  " };
        var renames = new List<OwnedStringRename> { new("displaynameid", " " + Old + " ", New) };
        var owned = Set(Old.ToLowerInvariant());
        var plan = OwnedStringLifecyclePolicy.Prepare(renames, [], owned, entries, values, Set(New), Set());
        entries.Clear(); values.Clear(); renames.Clear(); owned.Clear();
        Assert.Equal("  exact text  ", plan.StringUpdates[New.ToLowerInvariant()]);
        Assert.Equal(Old, Assert.Single(plan.StringRemovals));
        Assert.Single(plan.ReferenceRenames);
    }

    [Fact]
    public void AmbiguousAffectedDefinitionsAreRejectedWhileUnrelatedAmbiguitiesAreLeftAlone()
    {
        var entries = Entries();
        entries[Old.ToLowerInvariant()] = "Ambiguous";
        Assert.Throws<InvalidOperationException>(() => Rename(entries));
        entries.Remove(Old.ToLowerInvariant());
        entries["STR_UNRELATED"] = "First";
        entries["str_unrelated"] = "Second";
        Assert.Equal(Translation, Rename(entries).StringUpdates[New]);
    }

    [Fact]
    public void ExistingStringWriterAppliesThePlanAndStillRejectsRawDuplicateDefinitions()
    {
        var original = $"ID = \"{Old}\" ; Str = \"Éclaireur\"\r\nID = \"STR_KEEP\" ; Str = \"Keep exactly\"\r\n";
        var table = StringTableDocument.Parse(original);
        var plan = Rename(table.ToDictionary());
        var updated = table.Apply(plan.StringUpdates, plan.StringRemovals, StringTableSection.Units);
        var saved = StringTableDocument.Parse(updated).ToDictionary();
        Assert.Equal("Éclaireur", saved[New]);
        Assert.False(saved.ContainsKey(Old));
        Assert.Contains("ID = \"STR_KEEP\" ; Str = \"Keep exactly\"\r\n", updated);
        var duplicated = StringTableDocument.Parse(original + $"ID = \"{Old.ToLowerInvariant()}\" ; Str = \"Duplicate\"\r\n");
        Assert.Throws<InvalidOperationException>(() => duplicated.Apply(plan.StringUpdates, plan.StringRemovals));
    }
}
