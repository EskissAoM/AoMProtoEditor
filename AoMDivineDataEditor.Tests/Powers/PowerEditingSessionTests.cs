using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class PowerEditingSessionTests
{
    [Fact]
    public void SelectionMovesAsOneStateAndClearsRawDraftStatus()
    {
        var session = new PowerEditingSession();
        var first = XElement.Parse("<power name='First' />");
        var second = XElement.Parse("<power name='Second' />");

        session.Select("First", first);
        session.RawEditorDirty = true;
        session.AdoptEntry("Second", second);

        Assert.Equal("Second", session.SelectedName);
        Assert.Equal("Second", session.LoadedEntryName);
        Assert.Same(second, session.CurrentEntry);
        Assert.False(session.RawEditorDirty);

        session.ClearSelection();
        Assert.Null(session.SelectedName);
        Assert.Null(session.LoadedEntryName);
        Assert.Null(session.CurrentEntry);
    }

    [Fact]
    public void RenameChainsAndCommitSnapshotsAreAtomicAndDetached()
    {
        var session = new PowerEditingSession();
        session.MarkDirty("Old");
        session.AddOrCollapseRename("Old", "Middle");
        session.AddOrCollapseRename("Middle", "Final");
        session.RenameDirty("Old", "Final");
        session.SetPendingString("STR_FINAL_NAME", "Final name", "Final");

        var snapshot = session.CreateCommitSnapshot();
        session.SetPendingString("STR_LATER", "Later", "Final");

        Assert.Equal(new KeyValuePair<string, string>("Old", "Final"), Assert.Single(snapshot.Renames));
        Assert.Contains("Final", snapshot.DirtyNames);
        Assert.Equal("Final name", snapshot.StringUpdates["STR_FINAL_NAME"]);
        Assert.DoesNotContain("STR_LATER", snapshot.StringUpdates.Keys);

        session.AcceptCommit();
        Assert.Empty(session.CreateCommitSnapshot().DirtyNames);
        Assert.Empty(session.CreateCommitSnapshot().Renames);
        Assert.Empty(session.CreateCommitSnapshot().StringUpdates);
        Assert.False(session.HasPendingWork);
    }

    [Fact]
    public void DiscardRemovesOnlyStateOwnedByTheDiscardedPower()
    {
        var session = new PowerEditingSession();
        session.MarkDirty("Discarded");
        session.MarkDirty("Kept");
        session.AddOrCollapseRename("OriginalDiscarded", "Discarded");
        session.SetPendingString("STR_DISCARDED", "discard", "Discarded");
        session.SetPendingString("STR_KEPT", "keep", "Kept");
        session.ScheduleStringRemoval("STR_OLD_DISCARDED", "Discarded");
        session.ScheduleStringRemoval("STR_OLD_KEPT", "Kept");

        session.DiscardPowerState("Discarded", "OriginalDiscarded");
        var snapshot = session.CreateCommitSnapshot();

        Assert.Equal(["Kept"], snapshot.DirtyNames);
        Assert.Empty(snapshot.Renames);
        Assert.Equal("keep", Assert.Single(snapshot.StringUpdates).Value);
        Assert.Equal(["STR_OLD_KEPT"], snapshot.StringRemovals);
        Assert.True(session.HasPendingWork);
    }

    [Fact]
    public void ExternalReplacementUsesTheExistingDirtyRecordPolicy()
    {
        var session = new PowerEditingSession();
        session.MarkDirty("ExternalWins");
        session.MarkDirty("LocalRemains");
        session.SetDirty(true);

        session.AcceptExternalReplacements(["ExternalWins"]);

        Assert.Equal(["LocalRemains"], session.CreateCommitSnapshot().DirtyNames);
        Assert.True(session.HasDirtyRecords);
        Assert.True(session.IsDirty); // The view publishes the recalculated dirty state atomically.
    }

    [Fact]
    public void TransientResetClosesAllOptionalPresentationState()
    {
        var state = new PowerTransientUiState();
        state[PowerTransientArea.CageVfx] = true;
        state[PowerTransientArea.XmlPreviewCollapsed] = true;
        state.OpenOtherAttributes.Add("Radius");
        state.OpenOtherSpecificAttributes.Add("VFX");

        state.Reset();

        Assert.False(state[PowerTransientArea.CageVfx]);
        Assert.False(state[PowerTransientArea.XmlPreviewCollapsed]);
        Assert.Empty(state.OpenOtherAttributes);
        Assert.Empty(state.OpenOtherSpecificAttributes);
    }
}
