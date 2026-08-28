using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Windows;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class ProtoActionFinalHardeningPass7Tests
{
    private sealed class OrderedItem(string name)
    {
        public string Name { get; } = name;
    }

    [Fact]
    public void ActionOrdering_MovesLinkedGroupsWithoutSplittingOrRebuildingThem()
    {
        var build = new OrderedItem("Build");
        var maul = new OrderedItem("Maul");
        var areaAttack = new OrderedItem("AreaAttack");
        var heal = new OrderedItem("Heal");
        var actions = new List<OrderedItem> { build, maul, areaAttack, heal };

        var moved = ProtoActionOrderPolicy.MoveGroup(
            actions,
            [maul, areaAttack],
            [heal],
            insertAfter: true);

        Assert.True(moved);
        Assert.Equal(["Build", "Heal", "Maul", "AreaAttack"], actions.Select(item => item.Name));
        Assert.Same(maul, actions[2]);
        Assert.Same(areaAttack, actions[3]);
    }

    [Fact]
    public void ActionOrdering_ReorderedModelsPreserveUnknownXmlPayload()
    {
        var source = XElement.Parse(
            """
            <unit name="OrderTest">
              <protoaction custom="first">
                <name>First</name>
                <type>Attack</type>
                <unknown flag="kept"><child>alpha</child></unknown>
              </protoaction>
              <protoaction custom="second">
                <name>Second</name>
                <type>Heal</type>
                <futurefield mode="kept">beta</futurefield>
              </protoaction>
            </unit>
            """);
        var actions = ProtoXmlHandler.GetProtoActions(source);
        var first = actions[0];
        var second = actions[1];

        Assert.True(ProtoActionOrderPolicy.MoveGroup(actions, [second], [first], insertAfter: false));
        var output = new XElement("unit", new XAttribute("name", "OrderTest"));
        ProtoXmlHandler.SetProtoActions(output, actions);

        var reordered = output.Elements("protoaction").ToList();
        Assert.Equal(["Second", "First"], reordered.Select(element => element.Element("name")?.Value));
        Assert.Equal("second", reordered[0].Attribute("custom")?.Value);
        Assert.Equal("kept", reordered[0].Element("futurefield")?.Attribute("mode")?.Value);
        Assert.Equal("beta", reordered[0].Element("futurefield")?.Value);
        Assert.Equal("first", reordered[1].Attribute("custom")?.Value);
        Assert.Equal("kept", reordered[1].Element("unknown")?.Attribute("flag")?.Value);
        Assert.Equal("alpha", reordered[1].Element("unknown")?.Element("child")?.Value);
    }

    [Fact]
    public void ActionOrdering_UIUsesInheritedActionsAsAnchorsAndArmsAfterLongPress()
    {
        var source = File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "Windows", "ProtoEditorWindow.axaml.cs")));

        Assert.Contains("if (!CanReorderActionGroup(source))", source, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "if (!CanReorderActionGroup(source) || !CanReorderActionGroup(target))",
            source,
            StringComparison.Ordinal);
        Assert.Contains("Interval = TimeSpan.FromSeconds(0.1)", source, StringComparison.Ordinal);
        Assert.Contains("pointerDragSourceButton.Cursor = new Cursor(StandardCursorType.SizeAll);", source, StringComparison.Ordinal);
        Assert.Contains("targetButton.BorderThickness = insertAfter", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("BirthAttack", "Birth Attack")]
    [InlineData("ChargedHandAttack", "Hand Attack")]
    [InlineData("ChargedRangedAttack", "Ranged Attack")]
    [InlineData("ChopAttack", "Hand Attack")]
    [InlineData("FlyingUnitAttack", "Ranged Attack")]
    [InlineData("HandAttack", "Hand Attack")]
    [InlineData("HandAttackLand", "Hand Attack")]
    [InlineData("HuntingRangedAttack", "Ranged Attack")]
    [InlineData("LandChargedRangedAttack", "Ranged Attack")]
    [InlineData("LandHandAttack", "Hand Attack")]
    [InlineData("MythSelfDestructDivineAttack", "Self-Destruct Attack")]
    [InlineData("MythSelfDestructHeroicAttack", "Self-Destruct Attack")]
    [InlineData("MythSelfDestructMundaneAttack", "Self-Destruct Attack")]
    [InlineData("MythSelfDestructMythicalAttack", "Self-Destruct Attack")]
    [InlineData("MythSelfDestructRareAttack", "Self-Destruct Attack")]
    [InlineData("NavalHandAttack", "Hand Attack")]
    [InlineData("RangedAttack", "Ranged Attack")]
    [InlineData("RangedAttackFlying", "Ranged Attack")]
    [InlineData("RangedAttackMyth", "Ranged Attack")]
    [InlineData("SelfDestructAttack", "Self-Destruct Attack")]
    [InlineData("TitanAttack", "Hand Attack")]
    public void AttackModeDefaults_MatchApprovedActionNameInventory(string actionName, string expectedMode)
    {
        Assert.True(ProtoEditorWindow.TryGetDefaultAttackModeFromActionName(actionName, out var mode));
        Assert.Equal(expectedMode, mode);
    }

    [Theory]
    [InlineData("AntiWallAttack")]
    [InlineData("CustomAttack")]
    [InlineData("")]
    public void AttackModeDefaults_DoNotGuessForUnlistedNames(string actionName)
    {
        Assert.False(ProtoEditorWindow.TryGetDefaultAttackModeFromActionName(actionName, out _));
    }

    [Fact]
    public void SpawnLayout_RendersSpawnedUnitBeforeSpawnAtTarget()
    {
        var source = ReadProtoEditorSource();
        const string spawnedUnits = "state.AdditionalFieldsContainer.Children.Add(spawnedUnitsSection);";
        const string spawnAtTarget = "state.AdditionalFieldsContainer.Children.Add(spawnAtTargetSection);";

        var spawnedUnitsIndex = source.IndexOf(spawnedUnits, StringComparison.Ordinal);
        var spawnAtTargetIndex = source.IndexOf(spawnAtTarget, StringComparison.Ordinal);
        Assert.True(spawnedUnitsIndex >= 0 && spawnAtTargetIndex > spawnedUnitsIndex);
    }

    [Fact]
    public void ReferenceIntegrity_AcceptsCaseInsensitiveLocalAndInheritedTargets()
    {
        var original = XElement.Parse("<unit name='Test' />");
        var candidate = XElement.Parse(
            """
            <unit name="Test">
              <stackprotoaction>stackcontroller</stackprotoaction>
              <protoaction>
                <name>StackController</name>
                <type>StackControl</type>
                <stackcontrol>
                  <stackaddaction>LOCALACTION</stackaddaction>
                  <stacksubaction>tacticsaction</stacksubaction>
                </stackcontrol>
              </protoaction>
              <protoaction><name>LocalAction</name><type>Attack</type></protoaction>
            </unit>
            """);

        var issues = ProtoActionIntegrityPolicy.FindBrokenReferences(
            candidate,
            original,
            ["TacticsAction"]);

        Assert.Empty(issues);
    }

    [Fact]
    public void ReferenceIntegrity_ReportsEveryNewBrokenManagedReference()
    {
        var original = XElement.Parse("<unit name='Test' />");
        var candidate = XElement.Parse(
            """
            <unit name="Test">
              <birthprotoaction>MissingBirth</birthprotoaction>
              <protoaction>
                <name>Controller</name>
                <type>StackControl</type>
                <areaprotoaction>MissingArea</areaprotoaction>
                <stackcontrol>
                  <stackaddaction>MissingAdd</stackaddaction>
                  <stacksubaction>MissingSub</stacksubaction>
                </stackcontrol>
                <onhiteffect type="Infect" protoaction="MissingInfect" />
              </protoaction>
            </unit>
            """);

        var issues = ProtoActionIntegrityPolicy.FindBrokenReferences(candidate, original);
        var report = ProtoActionIntegrityPolicy.BuildReferenceValidationReport("Test", issues);

        Assert.Equal(5, issues.Count);
        Assert.Contains("ProtoUnit: Test", report, StringComparison.Ordinal);
        Assert.Contains("Action 'Controller':", report, StringComparison.Ordinal);
        Assert.Contains("MissingInfect", report, StringComparison.Ordinal);
        Assert.Contains("MissingBirth", report, StringComparison.Ordinal);
    }

    [Fact]
    public void ReferenceIntegrity_PreservesUnchangedLegacyBrokenReferencesButBlocksEdits()
    {
        var original = XElement.Parse(
            """
            <unit name="Legacy">
              <protoaction>
                <name>Controller</name>
                <type>StackControl</type>
                <stackcontrol><stackaddaction>MissingLegacy</stackaddaction></stackcontrol>
              </protoaction>
            </unit>
            """);
        var unchanged = new XElement(original);
        var edited = new XElement(original);
        edited.Descendants("stackaddaction").Single().Value = "MissingEdited";

        Assert.Empty(ProtoActionIntegrityPolicy.FindBrokenReferences(unchanged, original));
        var issue = Assert.Single(ProtoActionIntegrityPolicy.FindBrokenReferences(edited, original));
        Assert.Equal("Stack Add Action", issue.FieldLabel);
        Assert.Equal("MissingEdited", issue.TargetName);
    }

    [Theory]
    [InlineData(true, null, true)]
    [InlineData(true, "", true)]
    [InlineData(true, "Attack", false)]
    [InlineData(false, null, false)]
    [InlineData(false, "", false)]
    public void ProtoActionTypePolicy_RequiresResolvedTypeOnlyForNewActions(
        bool isNewCustomAction,
        string? resolvedType,
        bool expected)
    {
        Assert.Equal(
            expected,
            ProtoActionIntegrityPolicy.IsMissingRequiredType(isNewCustomAction, resolvedType));
    }

    [Fact]
    public async Task TacticsSession_RejectsConcurrentSaveAndReleasesGateAfterCompletion()
    {
        var callbackEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCallback = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbackCount = 0;
        var session = new TacticsActionEditorSession(
            "test.tactics",
            isReadOnly: false,
            XDocument.Parse("<tactics />"),
            async _ =>
            {
                callbackCount++;
                callbackEntered.TrySetResult();
                await releaseCallback.Task;
                return TacticsDocumentSaveOutcome.Saved;
            });

        var firstSave = session.TrySaveAsync(XDocument.Parse("<tactics><action /></tactics>"));
        await callbackEntered.Task;

        Assert.True(session.IsSaveInProgress);
        Assert.Equal(
            TacticsEditorSaveResult.Busy,
            await session.TrySaveAsync(XDocument.Parse("<tactics><other /></tactics>")));
        Assert.Equal(1, callbackCount);

        releaseCallback.TrySetResult();
        Assert.Equal(TacticsEditorSaveResult.Saved, await firstSave);
        Assert.False(session.IsSaveInProgress);
    }

    [Fact]
    public async Task TacticsSession_ReleasesSaveGateWhenCallbackThrows()
    {
        var attempt = 0;
        var session = new TacticsActionEditorSession(
            "test.tactics",
            isReadOnly: false,
            XDocument.Parse("<tactics />"),
            _ =>
            {
                attempt++;
                if (attempt == 1)
                    throw new IOException("Simulated save failure");
                return Task.FromResult(TacticsDocumentSaveOutcome.Saved);
            });

        await Assert.ThrowsAsync<IOException>(() =>
            session.TrySaveAsync(XDocument.Parse("<tactics><first /></tactics>")));
        Assert.False(session.IsSaveInProgress);

        Assert.Equal(
            TacticsEditorSaveResult.Saved,
            await session.TrySaveAsync(XDocument.Parse("<tactics><second /></tactics>")));
    }

    [Fact]
    public void TacticsSession_DoesNotExposeMutableCommittedDocument()
    {
        var original = XDocument.Parse("<tactics><action><name>Attack</name></action></tactics>");
        var session = new TacticsActionEditorSession("test.tactics", false, original, null);

        var exposedCopy = session.CommittedDocument;
        exposedCopy.Root?.Add(new XElement("action", new XElement("name", "Injected")));

        Assert.False(session.HasUnsavedChanges(true, () => new XDocument(original)));
        Assert.Single(session.CommittedDocument.Root?.Elements("action") ?? []);
    }

    [Fact]
    public void ProtoUnitCapture_RestoresAllMutableStateWhenProjectionFails()
    {
        var source = ReadProtoEditorSource();
        var captureStart = source.IndexOf("private async Task<bool> CaptureCurrentUnitDraftAsync()", StringComparison.Ordinal);
        var saveStart = source.IndexOf("private async void Save_Click", captureStart, StringComparison.Ordinal);
        Assert.True(captureStart >= 0 && saveStart > captureStart);
        var capture = source[captureStart..saveStart];

        Assert.Contains("var snapshot = CreateProtoUnitDraftCaptureSnapshot();", capture, StringComparison.Ordinal);
        Assert.True(
            capture.IndexOf("var snapshot = CreateProtoUnitDraftCaptureSnapshot();", StringComparison.Ordinal) <
            capture.IndexOf("ApplyCurrentEdits();", StringComparison.Ordinal));
        Assert.Contains("RestoreProtoUnitDraftCaptureSnapshot(snapshot);", capture, StringComparison.Ordinal);
        Assert.Contains("catch (Exception rollbackException)", capture, StringComparison.Ordinal);
        Assert.Contains("throw new AggregateException(", capture, StringComparison.Ordinal);

        Assert.Contains("PendingUnitRenames", source, StringComparison.Ordinal);
        Assert.Contains("UnitAbilityDrafts", source, StringComparison.Ordinal);
        Assert.Contains("UnitCategories", source, StringComparison.Ordinal);
        Assert.Contains("DuplicatedUnitRechargeFallbacks", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ProtoUnitSave_BlocksNewActionWithoutResolvedType()
    {
        var source = ReadProtoEditorSource();

        Assert.Contains("ProtoActionIntegrityPolicy.IsMissingRequiredType(", source, StringComparison.Ordinal);
        Assert.Contains("Missing ProtoAction Type", source, StringComparison.Ordinal);
        Assert.Contains("Every new ProtoAction requires a valid action type", source, StringComparison.Ordinal);
    }

    private static string ReadProtoEditorSource()
        => File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "Windows", "ProtoEditorWindow.axaml.cs")));
}
