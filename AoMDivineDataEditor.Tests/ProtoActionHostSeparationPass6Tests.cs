using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Windows;
using System.Xml.Linq;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class ProtoActionHostSeparationPass6Tests
{
    [Fact]
    public void ProtoUnitHost_DeclaresUnitCapabilitiesExplicitly()
    {
        var host = ProtoActionEditorHostContext.ProtoUnit;

        Assert.False(host.IsTacticsDocument);
        Assert.True(host.UsesTacticsInheritance);
        Assert.True(host.EnforcesProtoUnitOnlyConstraints);
        Assert.True(host.ShowsProtoUnitChrome);
        Assert.True(host.TracksProtoUnitDraft);
        Assert.False(host.UsesStandaloneDocumentLifecycle);
        Assert.False(host.ShowsTacticsDefinitionEditor);
        Assert.False(host.AllowsStandaloneActionCreation);
    }

    [Fact]
    public void TacticsHost_DoesNotLeakProtoUnitOnlyBehavior()
    {
        var host = ProtoActionEditorHostContext.TacticsDocument;

        Assert.True(host.IsTacticsDocument);
        Assert.False(host.UsesTacticsInheritance);
        Assert.False(host.EnforcesProtoUnitOnlyConstraints);
        Assert.False(host.ShowsProtoUnitChrome);
        Assert.False(host.TracksProtoUnitDraft);
        Assert.True(host.UsesStandaloneDocumentLifecycle);
        Assert.True(host.ShowsTacticsDefinitionEditor);
        Assert.True(host.AllowsStandaloneActionCreation);
    }

    [Fact]
    public void ProtoActionWindow_UsesHostCapabilitiesInsteadOfTheLegacyModeFlag()
    {
        var source = File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "Windows", "ProtoEditorWindow.axaml.cs")));

        Assert.DoesNotContain("_isTacticsActionEditorMode", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_protoActionHost.", source, StringComparison.Ordinal);
        Assert.Contains("_protoActionHostAdapter = new TacticsActionEditorHostAdapter(GetTacticsEditorActions);", source, StringComparison.Ordinal);
        Assert.Contains("_protoActionHostAdapter.LoadActions(unit);", source, StringComparison.Ordinal);
        Assert.Contains("_protoActionHostAdapter.ResolveActionType(", source, StringComparison.Ordinal);
        Assert.Contains("_protoActionHostAdapter.FindInheritedAction(", source, StringComparison.Ordinal);
        Assert.Contains("_protoActionHostAdapter.ShouldCollapseTacticsOnlyOverlay(", source, StringComparison.Ordinal);
        Assert.Contains("_protoActionHostAdapter.WriteActions(unit, actionsList);", source, StringComparison.Ordinal);
        Assert.Contains("_protoActionHostAdapter.Context.UsesTacticsInheritance", source, StringComparison.Ordinal);
        Assert.Contains("_protoActionHostAdapter.Context.EnforcesProtoUnitOnlyConstraints", source, StringComparison.Ordinal);
        Assert.Contains("_protoActionHostAdapter.Context.TracksProtoUnitDraft", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_protoActionHostAdapter.Context.IsTacticsDocument", source, StringComparison.Ordinal);
        Assert.Contains("_protoActionHostAdapter.Context.UsesStandaloneDocumentLifecycle", source, StringComparison.Ordinal);
        Assert.Contains("_protoActionHostAdapter.Context.ShowsTacticsDefinitionEditor", source, StringComparison.Ordinal);
        Assert.Contains("_protoActionHostAdapter.Context.AllowsStandaloneActionCreation", source, StringComparison.Ordinal);
        Assert.Contains("_protoActionHostAdapter.Context.OwnsCompleteActionSequence", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ProtoUnitAdapter_OwnsProtoUnitLoadingInheritanceAndOverlayWritingRules()
    {
        var adapter = new ProtoUnitActionEditorHostAdapter();
        var unit = XElement.Parse("<unit><protoaction><name>Attack</name><maxrange>5</maxrange></protoaction></unit>");
        var inherited = new ProtoAction { Name = "Attack", Type = "Attack" };

        var actions = adapter.LoadActions(unit);

        Assert.Single(actions);
        Assert.Equal("Attack", actions[0].Name);
        Assert.Equal("5", actions[0].MaxRange);
        Assert.Equal("MappedAttack", adapter.ResolveActionType(
            "Attack",
            "EditorFallback",
            _ => "MappedAttack",
            value => value ?? string.Empty));
        Assert.Same(inherited, adapter.FindInheritedAction("Attack", _ => inherited));
        Assert.True(adapter.ShouldCollapseTacticsOnlyOverlay(true));

        var output = new XElement("unit");
        adapter.WriteActions(output, actions);
        Assert.Equal("Attack", output.Element("protoaction")?.Element("name")?.Value);
    }

    [Fact]
    public void TacticsAdapter_OwnsTacticsLoadingAndDoesNotApplyProtoUnitOverlayRules()
    {
        var tacticsActions = new List<ProtoAction>
        {
            new() { Name = "Heal", Type = "Heal" }
        };
        var adapter = new TacticsActionEditorHostAdapter(() => tacticsActions);

        Assert.Same(tacticsActions, adapter.LoadActions(new XElement("unit")));
        Assert.Equal("ExactHeal", adapter.ResolveActionType(
            "Heal",
            "ExactHeal",
            _ => "MappedHeal",
            value => value ?? string.Empty));
        Assert.Null(adapter.FindInheritedAction("Heal", _ => tacticsActions[0]));
        Assert.False(adapter.ShouldCollapseTacticsOnlyOverlay(true));

        var output = new XElement("unit");
        adapter.WriteActions(output, tacticsActions);
        Assert.Equal("Heal", output.Element("protoaction")?.Element("name")?.Value);
    }

    [Fact]
    public async Task TacticsSession_CommitsOnlyAfterSuccessfulSave()
    {
        var original = XDocument.Parse("<tactics><action><name>Old</name></action></tactics>");
        var updated = XDocument.Parse("<tactics><action><name>New</name></action></tactics>");
        var outcome = TacticsDocumentSaveOutcome.Cancelled;
        var session = new TacticsActionEditorSession(
            "test.tactics",
            isReadOnly: false,
            original,
            _ => Task.FromResult(outcome));

        Assert.Equal(TacticsEditorSaveResult.Cancelled, await session.TrySaveAsync(updated));
        Assert.True(XNode.DeepEquals(original, session.CommittedDocument));

        outcome = TacticsDocumentSaveOutcome.Saved;
        Assert.Equal(TacticsEditorSaveResult.Saved, await session.TrySaveAsync(updated));
        Assert.True(XNode.DeepEquals(updated, session.CommittedDocument));
    }

    [Fact]
    public async Task TacticsSession_ReadOnlyModeCannotSaveOrReportUnsavedChanges()
    {
        var callbackInvoked = false;
        var session = new TacticsActionEditorSession(
            "built-in.tactics",
            isReadOnly: true,
            XDocument.Parse("<tactics />"),
            _ =>
            {
                callbackInvoked = true;
                return Task.FromResult(TacticsDocumentSaveOutcome.Saved);
            });

        Assert.False(session.CanSave);
        Assert.Equal(
            TacticsEditorSaveResult.Unavailable,
            await session.TrySaveAsync(XDocument.Parse("<tactics><action /></tactics>")));
        Assert.False(callbackInvoked);
        Assert.False(session.HasUnsavedChanges(true, () => throw new InvalidOperationException()));
    }

    [Fact]
    public void TacticsSession_ComparesDirtyStateWithCommittedXmlAndFailsSafe()
    {
        var committed = XDocument.Parse("<tactics><action><name>Attack</name></action></tactics>");
        var session = new TacticsActionEditorSession("test.tactics", false, committed, null);

        Assert.False(session.HasUnsavedChanges(false, () => XDocument.Parse("<different />")));
        Assert.False(session.HasUnsavedChanges(true, () => new XDocument(committed)));
        Assert.True(session.HasUnsavedChanges(true, () => XDocument.Parse("<tactics><action><name>Heal</name></action></tactics>")));
        Assert.True(session.HasUnsavedChanges(true, () => throw new InvalidOperationException("Invalid draft")));
    }

    [Fact]
    public void TacticsSession_PreventsDuplicateClosePromptsAndTracksClosePermission()
    {
        var session = new TacticsActionEditorSession("test.tactics", false, XDocument.Parse("<tactics />"), null);

        Assert.False(session.IsCloseAllowed);
        Assert.True(session.TryBeginClosePrompt());
        Assert.False(session.TryBeginClosePrompt());
        session.EndClosePrompt();
        Assert.True(session.TryBeginClosePrompt());
        session.EndClosePrompt();

        session.AllowClose();
        Assert.True(session.IsCloseAllowed);
    }

    [Fact]
    public void ProtoActionWindow_DelegatesTacticsLifecycleToSession()
    {
        var source = File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "Windows", "ProtoEditorWindow.axaml.cs")));

        Assert.DoesNotContain("_tacticsEditorDocument", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_saveTacticsEditorDocumentAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_allowTacticsEditorClose", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_tacticsEditorClosePromptOpen", source, StringComparison.Ordinal);
        Assert.Contains("_tacticsEditorSession.TrySaveAsync(updated)", source, StringComparison.Ordinal);
        Assert.Contains("_tacticsEditorSession.IsSaveInProgress", source, StringComparison.Ordinal);
        Assert.Contains("newer edits remain unsaved", source, StringComparison.Ordinal);
        Assert.Contains("_tacticsEditorSession?.HasUnsavedChanges(", source, StringComparison.Ordinal);
        Assert.Contains("_tacticsEditorSession?.TryBeginClosePrompt()", source, StringComparison.Ordinal);
        Assert.Contains("_tacticsEditorSession?.IsCloseAllowed", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedHostBoundaries_RemainIndependentFromAvaloniaAndWindowLifecycle()
    {
        var projectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var adapters = File.ReadAllText(Path.Combine(projectRoot, "Classes", "ProtoActionEditorHostAdapters.cs"));
        var session = File.ReadAllText(Path.Combine(projectRoot, "Windows", "TacticsActionEditorSession.cs"));

        Assert.DoesNotContain("Avalonia", adapters, StringComparison.Ordinal);
        Assert.DoesNotContain("ProtoEditorWindow", adapters, StringComparison.Ordinal);
        Assert.DoesNotContain("Avalonia", session, StringComparison.Ordinal);
        Assert.DoesNotContain("new Prompt(", session, StringComparison.Ordinal);
        Assert.DoesNotContain("ProtoEditorWindow", session, StringComparison.Ordinal);

        foreach (var controlPath in Directory.GetFiles(Path.Combine(projectRoot, "Controls"), "*.cs"))
        {
            var controlSource = File.ReadAllText(controlPath);
            Assert.DoesNotContain("TacticsActionEditorSession", controlSource, StringComparison.Ordinal);
            Assert.DoesNotContain("TacticsDocumentSaveOutcome", controlSource, StringComparison.Ordinal);
            Assert.DoesNotContain("ProtoActionEditorHostKind", controlSource, StringComparison.Ordinal);
        }
    }
}
