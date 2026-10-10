using System.Reflection;
using System.Text;
using System.Xml.Linq;
using Avalonia.Controls;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.GameData;
using AoMDivineDataEditor.Windows;
using Xunit;

namespace AoMDivineDataEditor.Tests.Powers;

public sealed class PowerEditorTransactionSafetyTests : IDisposable
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(false);

    public void Dispose() => AvaloniaTestHost.CollectUiGarbage();

    [Fact]
    public void FailedLateStringWriteRollsBackRenameAndReferencesAndCanBeRetried()
    {
        using var workspace = new TestWorkspace(nameof(
            FailedLateStringWriteRollsBackRenameAndReferencesAndCanBeRetried));
        var powerPath = workspace.WriteText("TestMod/game/data/gameplay/powers_mods.xml", """
            <powersmod><power name="OldPower" type="GodPower">
              <displaynameid>STR_POWER_OLD_POWER</displaynameid>
            </power></powersmod>
            """);
        var referencePath = workspace.WriteText("TestMod/game/data/gameplay/proto_mods.xml",
            "<protomods><unit name=\"Hero\"><castpower>OldPower</castpower></unit></protomods>");
        var stringsPath = workspace.WriteText("TestMod/game/data/strings/stringmods.txt", """
            Language = "English"
            ID = "STR_POWER_OLD_POWER"   ;   Str = "Old display"
            """);
        var powerBefore = File.ReadAllBytes(powerPath);
        var referenceBefore = File.ReadAllBytes(referencePath);
        var stringsBefore = File.ReadAllBytes(stringsPath);
        var failStringWrite = true;
        var adoptedPlans = 0;

        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], powerPath,
            prepareReferenceMigration: renames => GameIdentifierReferenceMigrationPlan.Prepare(
                [new TechnologyReferenceFile(referencePath, TechnologyReferenceDocumentKind.ProtoUnits,
                    "ProtoUnits")], GameIdentifierKind.Ability, renames),
            adoptReferenceMigration: _ => adoptedPlans++,
            resolveStringAsync: id => Task.FromResult(
                StringTableDocument.Parse(File.ReadAllText(stringsPath)).ToDictionary()
                    .GetValueOrDefault(id)),
            saveStringsAsync: (updates, removals) =>
            {
                var updated = StringTableDocument.Parse(File.ReadAllText(stringsPath))
                    .Apply(updates, removals, StringTableSection.Powers);
                File.WriteAllText(stringsPath, updated, Utf8WithoutBom);
                if (failStringWrite)
                    throw new InvalidOperationException("simulated late string failure");
                return Task.CompletedTask;
            },
            modStringsPath: stringsPath));

        AvaloniaTestHost.Run(() => editor.SetModifiedMode(true));
        AvaloniaTestHost.RunAsync(() => editor.SelectPowerAsync("OldPower"));
        AvaloniaTestHost.Run(() =>
        {
            GetPrivateField<TextBox>(editor, "_nameBox").Text = "NewPower";
            GetPrivateField<TextBox>(editor, "_displayNameBox").Text = "New display";
        });

        Assert.False(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        Assert.Equal(powerBefore, File.ReadAllBytes(powerPath));
        Assert.Equal(referenceBefore, File.ReadAllBytes(referencePath));
        Assert.Equal(stringsBefore, File.ReadAllBytes(stringsPath));
        Assert.Equal(0, adoptedPlans);
        Assert.True(AvaloniaTestHost.Run(() => editor.IsDirty));
        Assert.Equal("NewPower", AvaloniaTestHost.Run(() => editor.CurrentPowerName));

        failStringWrite = false;
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        Assert.Equal(1, adoptedPlans);
        Assert.False(AvaloniaTestHost.Run(() => editor.IsDirty));
        Assert.NotNull(PowerCatalog.Find(XDocument.Load(powerPath), "NewPower"));
        Assert.Null(PowerCatalog.Find(XDocument.Load(powerPath), "OldPower"));
        Assert.Equal("NewPower", XDocument.Load(referencePath).Descendants("castpower").Single().Value);
        var strings = StringTableDocument.Parse(File.ReadAllText(stringsPath)).ToDictionary();
        Assert.Equal("New display", strings[PowerCatalog.BuildStringId("NewPower", "displaynameid")]);
        Assert.DoesNotContain(PowerCatalog.BuildStringId("OldPower", "displaynameid"), strings.Keys);
    }

    [Fact]
    public void DetachedEditorStillUsesItsHostForCrossModuleDeleteValidation()
    {
        using var workspace = new TestWorkspace(nameof(
            DetachedEditorStillUsesItsHostForCrossModuleDeleteValidation));
        var powerPath = workspace.WriteText("powers_mods.xml",
            "<powersmod><power name=\"ReferencedPower\" type=\"GodPower\" /></powersmod>");
        var validationCalls = 0;
        var owner = AvaloniaTestHost.Run(() => new Window());
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], powerPath,
            ensureCanDeleteAsync: (actualOwner, name) =>
            {
                validationCalls++;
                Assert.Same(owner, actualOwner);
                Assert.Equal("ReferencedPower", name);
                return Task.FromResult(false);
            },
            dialogOwner: owner));

        AvaloniaTestHost.Run(() => editor.SetModifiedMode(true));
        AvaloniaTestHost.RunAsync(() => editor.SelectPowerAsync("ReferencedPower"));
        AvaloniaTestHost.RunAsync(editor.DeleteCurrentPowerAsync);

        Assert.Equal(1, validationCalls);
        Assert.Equal("ReferencedPower", AvaloniaTestHost.Run(() => editor.CurrentPowerName));
        Assert.False(AvaloniaTestHost.Run(() => editor.IsDirty));
        Assert.NotNull(PowerCatalog.Find(XDocument.Load(powerPath), "ReferencedPower"));
    }

    [Fact]
    public void ChainedRenameCollapsesToOneCrossModuleMigrationAndOneStringNamespace()
    {
        using var workspace = new TestWorkspace(nameof(
            ChainedRenameCollapsesToOneCrossModuleMigrationAndOneStringNamespace));
        var powerPath = workspace.WriteText("TestMod/game/data/gameplay/powers_mods.xml", """
            <powersmod><power name="OldPower" type="GodPower">
              <displaynameid>STR_POWER_OLD_POWER</displaynameid>
            </power></powersmod>
            """);
        var referencePath = workspace.WriteText("TestMod/game/data/gameplay/proto_mods.xml",
            "<protomods><unit name=\"Hero\"><castpower>OldPower</castpower></unit></protomods>");
        var stringsPath = workspace.WriteText("TestMod/game/data/strings/stringmods.txt", """
            Language = "English"
            ID = "STR_POWER_OLD_POWER"   ;   Str = "Original"
            """);
        IReadOnlyList<KeyValuePair<string, string>>? preparedRenames = null;
        var editor = AvaloniaTestHost.Run(() => CreateTransactionalEditor(
            powerPath, referencePath, stringsPath,
            renames => preparedRenames = renames.ToList()));

        AvaloniaTestHost.Run(() => editor.SetModifiedMode(true));
        AvaloniaTestHost.RunAsync(() => editor.SelectPowerAsync("OldPower"));
        AvaloniaTestHost.Run(() => GetPrivateField<TextBox>(editor, "_nameBox").Text = "MiddlePower");
        Assert.True(AvaloniaTestHost.RunAsync(editor.CommitCurrentPowerAsync));
        AvaloniaTestHost.RunAsync(() => editor.SelectPowerAsync("MiddlePower"));
        AvaloniaTestHost.Run(() => GetPrivateField<TextBox>(editor, "_nameBox").Text = "FinalPower");
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));

        var rename = Assert.Single(preparedRenames!);
        Assert.Equal("OldPower", rename.Key);
        Assert.Equal("FinalPower", rename.Value);
        Assert.NotNull(PowerCatalog.Find(XDocument.Load(powerPath), "FinalPower"));
        Assert.Equal("FinalPower", XDocument.Load(referencePath).Descendants("castpower").Single().Value);
        var strings = StringTableDocument.Parse(File.ReadAllText(stringsPath)).ToDictionary();
        Assert.Equal("Original", strings[PowerCatalog.BuildStringId("FinalPower", "displaynameid")]);
        Assert.DoesNotContain(PowerCatalog.BuildStringId("OldPower", "displaynameid"), strings.Keys);
        Assert.DoesNotContain(PowerCatalog.BuildStringId("MiddlePower", "displaynameid"), strings.Keys);
    }

    [Fact]
    public void DiscardingAChainedRenameRestoresOriginalStateAndClearsPendingWork()
    {
        using var workspace = new TestWorkspace(nameof(
            DiscardingAChainedRenameRestoresOriginalStateAndClearsPendingWork));
        var powerPath = workspace.WriteText("TestMod/game/data/gameplay/powers_mods.xml", """
            <powersmod><power name="OldPower" type="GodPower">
              <displaynameid>STR_POWER_OLD_POWER</displaynameid>
            </power></powersmod>
            """);
        var referencePath = workspace.WriteText("TestMod/game/data/gameplay/proto_mods.xml",
            "<protomods><unit name=\"Hero\"><castpower>OldPower</castpower></unit></protomods>");
        var stringsPath = workspace.WriteText("TestMod/game/data/strings/stringmods.txt", """
            Language = "English"
            ID = "STR_POWER_OLD_POWER"   ;   Str = "Original"
            """);
        var original = new XElement(PowerCatalog.Find(XDocument.Load(powerPath), "OldPower")!);
        var editor = AvaloniaTestHost.Run(() => CreateTransactionalEditor(
            powerPath, referencePath, stringsPath));

        AvaloniaTestHost.Run(() => editor.SetModifiedMode(true));
        AvaloniaTestHost.RunAsync(() => editor.SelectPowerAsync("OldPower"));
        AvaloniaTestHost.Run(() => GetPrivateField<TextBox>(editor, "_nameBox").Text = "MiddlePower");
        Assert.True(AvaloniaTestHost.RunAsync(editor.CommitCurrentPowerAsync));
        AvaloniaTestHost.RunAsync(() => editor.SelectPowerAsync("MiddlePower"));
        AvaloniaTestHost.Run(() => GetPrivateField<TextBox>(editor, "_nameBox").Text = "FinalPower");
        Assert.True(AvaloniaTestHost.RunAsync(editor.CommitCurrentPowerAsync));

        AvaloniaTestHost.Run(() => editor.DiscardPowerChanges("FinalPower", original));
        Assert.Equal("OldPower", AvaloniaTestHost.Run(() => editor.CurrentPowerName));
        Assert.False(AvaloniaTestHost.Run(() => editor.IsDirty));
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));
        Assert.NotNull(PowerCatalog.Find(XDocument.Load(powerPath), "OldPower"));
        Assert.Equal("OldPower", XDocument.Load(referencePath).Descendants("castpower").Single().Value);
        var strings = StringTableDocument.Parse(File.ReadAllText(stringsPath)).ToDictionary();
        Assert.Equal("Original", strings[PowerCatalog.BuildStringId("OldPower", "displaynameid")]);
        Assert.DoesNotContain(PowerCatalog.BuildStringId("MiddlePower", "displaynameid"), strings.Keys);
        Assert.DoesNotContain(PowerCatalog.BuildStringId("FinalPower", "displaynameid"), strings.Keys);
    }

    [Fact]
    public void ChainedRenameThenDeleteValidatesOriginalNameAndCancelsMigration()
    {
        using var workspace = new TestWorkspace(nameof(
            ChainedRenameThenDeleteValidatesOriginalNameAndCancelsMigration));
        var powerPath = workspace.WriteText("TestMod/game/data/gameplay/powers_mods.xml", """
            <powersmod><power name="OldPower" type="GodPower">
              <displaynameid>STR_POWER_OLD_POWER</displaynameid>
            </power></powersmod>
            """);
        var referencePath = workspace.WriteText("TestMod/game/data/gameplay/proto_mods.xml",
            "<protomods><unit name=\"Hero\"><castpower>OldPower</castpower></unit></protomods>");
        var stringsPath = workspace.WriteText("TestMod/game/data/strings/stringmods.txt", """
            Language = "English"
            ID = "STR_POWER_OLD_POWER"   ;   Str = "Original"
            """);
        var preparedMigrationCalls = 0;
        string? validatedName = null;
        var owner = AvaloniaTestHost.Run(() => new Window());
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], powerPath,
            prepareReferenceMigration: renames =>
            {
                preparedMigrationCalls++;
                return GameIdentifierReferenceMigrationPlan.Prepare(
                    [new TechnologyReferenceFile(referencePath,
                        TechnologyReferenceDocumentKind.ProtoUnits, "ProtoUnits")],
                    GameIdentifierKind.Ability, renames);
            },
            ensureCanDeleteAsync: (_, name) =>
            {
                validatedName = name;
                return Task.FromResult(true);
            },
            dialogOwner: owner,
            resolveStringAsync: id => Task.FromResult(
                StringTableDocument.Parse(File.ReadAllText(stringsPath)).ToDictionary()
                    .GetValueOrDefault(id)),
            saveStringsAsync: (updates, removals) =>
            {
                var updated = StringTableDocument.Parse(File.ReadAllText(stringsPath))
                    .Apply(updates, removals, StringTableSection.Powers);
                File.WriteAllText(stringsPath, updated, Utf8WithoutBom);
                return Task.CompletedTask;
            },
            modStringsPath: stringsPath,
            confirmDeleteAsync: (_, _) => Task.FromResult(true)));

        AvaloniaTestHost.Run(() => editor.SetModifiedMode(true));
        AvaloniaTestHost.RunAsync(() => editor.SelectPowerAsync("OldPower"));
        AvaloniaTestHost.Run(() => GetPrivateField<TextBox>(editor, "_nameBox").Text = "MiddlePower");
        Assert.True(AvaloniaTestHost.RunAsync(editor.CommitCurrentPowerAsync));
        AvaloniaTestHost.RunAsync(() => editor.SelectPowerAsync("MiddlePower"));
        AvaloniaTestHost.Run(() => GetPrivateField<TextBox>(editor, "_nameBox").Text = "FinalPower");
        Assert.True(AvaloniaTestHost.RunAsync(editor.CommitCurrentPowerAsync));

        AvaloniaTestHost.RunAsync(editor.DeleteCurrentPowerAsync);
        Assert.Equal("OldPower", validatedName);
        Assert.Null(AvaloniaTestHost.Run(() => editor.CurrentPowerName));
        Assert.True(AvaloniaTestHost.Run(() => editor.IsDirty));
        Assert.True(AvaloniaTestHost.RunAsync(editor.SaveAsync));

        Assert.Equal(0, preparedMigrationCalls);
        Assert.Empty(XDocument.Load(powerPath).Descendants("power"));
        Assert.Equal("OldPower", XDocument.Load(referencePath).Descendants("castpower").Single().Value);
        var strings = StringTableDocument.Parse(File.ReadAllText(stringsPath)).ToDictionary();
        Assert.DoesNotContain(PowerCatalog.BuildStringId("OldPower", "displaynameid"), strings.Keys);
        Assert.DoesNotContain(PowerCatalog.BuildStringId("MiddlePower", "displaynameid"), strings.Keys);
        Assert.DoesNotContain(PowerCatalog.BuildStringId("FinalPower", "displaynameid"), strings.Keys);
        Assert.False(AvaloniaTestHost.Run(() => editor.IsDirty));
    }

    [Theory]
    [InlineData("OldPower")]
    [InlineData("FinalPower")]
    [InlineData("oldpower")]
    public void ExternalConflictOnEitherRenameEndpointIsDetected(string conflictingName)
    {
        var session = new PowerEditingSession();
        session.AddOrCollapseRename("OldPower", "FinalPower");

        var conflict = session.FindConflictingRename([conflictingName]);

        Assert.Equal("OldPower", conflict.Key);
        Assert.Equal("FinalPower", conflict.Value);
    }

    [Fact]
    public void UnrelatedExternalChangeDoesNotConflictWithPendingRename()
    {
        var session = new PowerEditingSession();
        session.AddOrCollapseRename("OldPower", "FinalPower");

        var conflict = session.FindConflictingRename(["OtherPower"]);

        Assert.Equal(default, conflict);
    }

    private static PowerEditorView CreateTransactionalEditor(
        string powerPath,
        string referencePath,
        string stringsPath,
        Action<IReadOnlyList<KeyValuePair<string, string>>>? prepared = null)
        => new([], powerPath,
            prepareReferenceMigration: renames =>
            {
                prepared?.Invoke(renames);
                return GameIdentifierReferenceMigrationPlan.Prepare(
                    [new TechnologyReferenceFile(referencePath,
                        TechnologyReferenceDocumentKind.ProtoUnits, "ProtoUnits")],
                    GameIdentifierKind.Ability, renames);
            },
            resolveStringAsync: id => Task.FromResult(
                StringTableDocument.Parse(File.ReadAllText(stringsPath)).ToDictionary()
                    .GetValueOrDefault(id)),
            saveStringsAsync: (updates, removals) =>
            {
                var updated = StringTableDocument.Parse(File.ReadAllText(stringsPath))
                    .Apply(updates, removals, StringTableSection.Powers);
                File.WriteAllText(stringsPath, updated, Utf8WithoutBom);
                return Task.CompletedTask;
            },
            modStringsPath: stringsPath);

    private static T GetPrivateField<T>(object instance, string name)
        => (T)(instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                   ?.GetValue(instance)
               ?? throw new MissingFieldException(instance.GetType().FullName, name));
}
