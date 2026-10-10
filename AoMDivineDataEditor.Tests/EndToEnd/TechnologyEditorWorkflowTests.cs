using System.Reflection;
using System.Text;
using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.GameData;
using AoMDivineDataEditor.Windows;
using Xunit;

namespace AoMDivineDataEditor.Tests;

/// <summary>
/// Exercises the real TechnologyEditorView load/save pipeline. Policy-level tests
/// remain useful, but these scenarios protect the seams where document snapshots,
/// validation, strings, reference migration, and atomic file writes meet.
/// </summary>
[Trait("Category", TestCategories.EndToEnd)]
public sealed class TechnologyEditorWorkflowTests : IDisposable
{
    public void Dispose() => AvaloniaTestHost.CollectUiGarbage();

    [Fact]
    public void OpeningAnUntouchedCustomTechnologyPreservesExactFileBytesAndCleanState()
    {
        EnsureAvalonia();
        using var fixture = new TechnologyEditorFixture("""
            <techtreemods custom="root-value">
              <!-- preserved comment -->
              <tech name="Stable" custom="legacy">
                <displaynameid>STR_TECH_STABLE_NAME</displaynameid>
                <future payload="kept"><nested>value</nested></future>
                <effect type="FutureEffect" strange="yes"><payload /></effect>
              </tech>
            </techtreemods>
            """);
        var before = File.ReadAllBytes(fixture.TechtreePath);

        var editor = CreateEditor(fixture);

        Assert.False(editor.IsDirty);
        Assert.True(editor.CanModifyTechnologyDocument);
        Assert.Equal(before, File.ReadAllBytes(fixture.TechtreePath));
    }

    [Fact]
    public void SavingAnUntouchedTechnologyIsSemanticallyLosslessForUnknownXml()
    {
        EnsureAvalonia();
        using var fixture = new TechnologyEditorFixture("""
            <techtreemods custom="root-value">
              <futureRoot enabled="true" />
              <tech name="Imported" legacy="yes">
                <future payload="kept"><nested>value</nested></future>
                <effects marker="preserved">
                  <futureEffectMetadata code="7" />
                  <effect type="FutureEffect" strange="yes"><payload answer="42" /></effect>
                  <effect type="Data" subtype="CustomSubtype" custom="untouched" amount="-2.5" relativity="FutureMode">
                    <target type="CustomTarget">LegacyValue</target>
                  </effect>
                </effects>
              </tech>
            </techtreemods>
            """);
        var expected = XDocument.Load(fixture.TechtreePath);
        var editor = CreateEditor(fixture);

        Assert.True(CompleteOnDispatcher(editor.SaveAsync));

        Assert.True(XNode.DeepEquals(expected, XDocument.Load(fixture.TechtreePath)));
        Assert.False(editor.IsDirty);
    }

    [Fact]
    public void Creating_minor_god_age_upgrade_does_not_duplicate_default_costs()
    {
        EnsureAvalonia();
        using var fixture = new TechnologyEditorFixture("<techtreemods />");
        var editor = CreateEditor(fixture, (_, _) => Task.CompletedTask);

        Assert.True(CompleteOnDispatcher(() =>
            editor.AddAgeUpgradeTechnologyAsync("ClassicalAgeExampleGod")));

        var technology = FindTechnology(XDocument.Load(fixture.TechtreePath), "ClassicalAgeExampleGod");
        Assert.Single(technology.Elements("flag"), flag => flag.Value == "AgeUpgrade");
        Assert.Empty(technology.Elements("cost"));
    }

    [Fact]
    public void EditingOnePropertyDoesNotRewriteSiblingTechnologyOrUnknownPayload()
    {
        EnsureAvalonia();
        using var fixture = new TechnologyEditorFixture("""
            <techtreemods>
              <tech name="Edited" custom="keep">
                <researchpoints>1</researchpoints>
                <future payload="keep"><nested /></future>
                <effect type="FutureEffect" b="2" a="1" />
              </tech>
              <tech name="Untouched" custom="exact">
                <researchpoints>3</researchpoints>
                <unknown order="unchanged" />
              </tech>
            </techtreemods>
            """);
        var before = XDocument.Load(fixture.TechtreePath);
        var editor = CreateEditor(fixture);
        var draft = GetDraftDocument(editor);
        draft.Root!.Elements("tech").Single(technology =>
            (string?)technology.Attribute("name") == "Edited").Element("researchpoints")!.Value = "7";

        Assert.True(CompleteOnDispatcher(editor.SaveAsync));

        var saved = XDocument.Load(fixture.TechtreePath);
        var beforeEdited = FindTechnology(before, "Edited");
        var savedEdited = FindTechnology(saved, "Edited");
        Assert.Equal("7", savedEdited.Element("researchpoints")?.Value);
        Assert.True(XNode.DeepEquals(beforeEdited.Element("future"), savedEdited.Element("future")));
        Assert.True(XNode.DeepEquals(beforeEdited.Element("effect"), savedEdited.Element("effect")));
        Assert.True(XNode.DeepEquals(FindTechnology(before, "Untouched"), FindTechnology(saved, "Untouched")));
    }

    [Theory]
    [InlineData("<techtreemods><tech name='Broken'><effects/><effects/></tech></techtreemods>")]
    [InlineData("<techtreemods><tech name='Broken'><prereqs/><prereqs/></tech></techtreemods>")]
    [InlineData("<techtreemods><tech name='Broken'><cost resourcetype='Food'>1</cost><cost resourcetype='food'>2</cost></tech></techtreemods>")]
    [InlineData("<wrongroot><tech name='Broken'/></wrongroot>")]
    public void MalformedDocumentsAreReadOnlyAndCannotBeOverwritten(string xml)
    {
        EnsureAvalonia();
        using var fixture = new TechnologyEditorFixture(xml);
        var before = File.ReadAllBytes(fixture.TechtreePath);
        var editor = CreateEditor(fixture);

        Assert.False(editor.CanModifyTechnologyDocument);
        Assert.False(CompleteOnDispatcher(editor.SaveAsync));
        Assert.Equal(before, File.ReadAllBytes(fixture.TechtreePath));
    }

    [Fact]
    public void FailureDuringStringWriteRollsBackTechnologyAndStringsExactly()
    {
        EnsureAvalonia();
        using var fixture = new TechnologyEditorFixture("""
            <techtreemods>
              <tech name="Rollback"><researchpoints>1</researchpoints></tech>
            </techtreemods>
            """, "ID = Before\r\n");
        var technologyBefore = File.ReadAllBytes(fixture.TechtreePath);
        var stringsBefore = File.ReadAllBytes(fixture.StringsPath);
        var editor = CreateEditor(fixture, async (_, _) =>
        {
            await File.WriteAllTextAsync(fixture.StringsPath, "ID = After\r\n");
            throw new InvalidOperationException("simulated string write failure");
        });
        FindTechnology(GetDraftDocument(editor), "Rollback").Element("researchpoints")!.Value = "9";
        GetPrivateField<Dictionary<string, string>>(editor, "_pendingStringUpdates")["ID"] = "After";

        Assert.False(CompleteOnDispatcher(editor.SaveAsync));

        Assert.Equal(technologyBefore, File.ReadAllBytes(fixture.TechtreePath));
        Assert.Equal(stringsBefore, File.ReadAllBytes(fixture.StringsPath));
    }

    [Fact]
    public void SharedReferencedStringIsNeverPassedToTheRemovalWriter()
    {
        EnsureAvalonia();
        using var fixture = new TechnologyEditorFixture("""
            <techtreemods>
              <tech name="First"><displaynameid>STR_SHARED_NAME</displaynameid></tech>
              <tech name="Second"><rollovertextid>STR_SHARED_NAME</rollovertextid></tech>
            </techtreemods>
            """, "STR_SHARED_NAME = Shared\r\n");
        var writerCalled = false;
        var editor = CreateEditor(fixture, (_, _) =>
        {
            writerCalled = true;
            return Task.CompletedTask;
        });
        GetPrivateField<HashSet<string>>(editor, "_pendingStringRemovals").Add("STR_SHARED_NAME");

        Assert.True(CompleteOnDispatcher(editor.SaveAsync));

        Assert.False(writerCalled);
        Assert.Equal("STR_SHARED_NAME = Shared\r\n", File.ReadAllText(fixture.StringsPath));
    }

    [Fact]
    public void RenamingTechnologyMovesExclusivelyOwnedStringsInsteadOfLeavingDuplicates()
    {
        EnsureAvalonia();
        const string oldName = "MythicAgeNewMinorGodYeah2";
        const string newName = "MythicAgeNewMinorGodYeah3";
        const string oldDisplayId = "STR_TECH_MYTHIC_AGE_NEW_MINOR_GOD_YEAH2_NAME";
        const string oldRolloverId = "STR_TECH_MYTHIC_AGE_NEW_MINOR_GOD_YEAH2_LR";
        const string newDisplayId = "STR_TECH_MYTHIC_AGE_NEW_MINOR_GOD_YEAH3_NAME";
        const string newRolloverId = "STR_TECH_MYTHIC_AGE_NEW_MINOR_GOD_YEAH3_LR";
        using var fixture = new TechnologyEditorFixture($"""
            <techtreemods>
              <tech name="{oldName}">
                <displaynameid>{oldDisplayId}</displaynameid>
                <rollovertextid>{oldRolloverId}</rollovertextid>
                <flag>AgeUpgrade</flag>
              </tech>
            </techtreemods>
            """, $"{oldDisplayId} = New Minor God Yeah 2\r\n{oldRolloverId} = Rollover text\r\n");
        IReadOnlyDictionary<string, string>? savedUpdates = null;
        IReadOnlyCollection<string>? savedRemovals = null;
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [oldDisplayId] = "New Minor God Yeah 2",
            [oldRolloverId] = "Rollover text"
        };
        var editor = CreateEditor(
            fixture,
            (updates, removals) =>
            {
                savedUpdates = new Dictionary<string, string>(updates, StringComparer.OrdinalIgnoreCase);
                savedRemovals = removals.ToArray();
                var source = File.ReadAllText(fixture.StringsPath);
                var updated = StringTableDocument.Parse(source).Apply(
                    updates,
                    removals,
                    StringTableSection.Technologies);
                File.WriteAllText(fixture.StringsPath, updated, new UTF8Encoding(false));
                return Task.CompletedTask;
            },
            resolveStringAsync: id => Task.FromResult(values.GetValueOrDefault(id)));

        Assert.True(CompleteOnDispatcher(() => editor.RenameTechnologyAsync(oldName, newName)));

        Assert.NotNull(savedUpdates);
        Assert.Equal("New Minor God Yeah 2", savedUpdates![newDisplayId]);
        Assert.Equal("Rollover text", savedUpdates[newRolloverId]);
        Assert.DoesNotContain(oldDisplayId, savedUpdates.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(oldRolloverId, savedUpdates.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.NotNull(savedRemovals);
        Assert.Contains(oldDisplayId, savedRemovals!, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(oldRolloverId, savedRemovals!, StringComparer.OrdinalIgnoreCase);

        var savedTechnology = FindTechnology(XDocument.Load(fixture.TechtreePath), newName);
        Assert.Equal(newDisplayId, savedTechnology.Element("displaynameid")?.Value);
        Assert.Equal(newRolloverId, savedTechnology.Element("rollovertextid")?.Value);
        var savedStrings = StringTableDocument.Parse(File.ReadAllText(fixture.StringsPath)).ToDictionary();
        Assert.DoesNotContain(oldDisplayId, savedStrings.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(oldRolloverId, savedStrings.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("New Minor God Yeah 2", savedStrings[newDisplayId]);
        Assert.Equal("Rollover text", savedStrings[newRolloverId]);
    }

    [Fact]
    public void LifecycleStringRemovalPreservesAnIdReferencedOutsideTechnologyDocuments()
    {
        EnsureAvalonia();
        const string stringId = "STR_TECH_DELETE_ME_NAME";
        using var fixture = new TechnologyEditorFixture(
            "<techtreemods />",
            $"{stringId} = Keep me\r\n");
        IReadOnlyCollection<string>? savedRemovals = null;
        var editor = CreateEditor(
            fixture,
            (_, removals) =>
            {
                savedRemovals = removals.ToArray();
                return Task.CompletedTask;
            },
            externalStringReferenceProvider: ids =>
                ids.Contains(stringId, StringComparer.OrdinalIgnoreCase)
                    ? new HashSet<string>([stringId], StringComparer.OrdinalIgnoreCase)
                    : new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        GetPrivateField<HashSet<string>>(editor, "_pendingStringRemovals").Add(stringId);
        GetPrivateField<HashSet<string>>(editor, "_automaticStringRemovals").Add(stringId);

        Assert.True(CompleteOnDispatcher(editor.SaveAsync));

        Assert.Null(savedRemovals);
        Assert.Equal($"{stringId} = Keep me\r\n", File.ReadAllText(fixture.StringsPath));
    }

    [Fact]
    public void ConfirmedTechnologyDeletionRemovesItsExclusivelyOwnedStrings()
    {
        EnsureAvalonia();
        const string displayId = "STR_TECH_DELETE_ME_NAME";
        const string rolloverId = "STR_TECH_DELETE_ME_LR";
        using var fixture = new TechnologyEditorFixture(
            $"<techtreemods><tech name=\"DeleteMe\"><displaynameid>{displayId}</displaynameid><rollovertextid>{rolloverId}</rollovertextid></tech></techtreemods>",
            $"{displayId} = Delete me\r\n{rolloverId} = Delete rollover\r\n");
        var editor = CreateEditor(fixture, (updates, removals) =>
        {
            var source = File.ReadAllText(fixture.StringsPath);
            var updated = StringTableDocument.Parse(source).Apply(
                updates,
                removals,
                StringTableSection.Technologies);
            File.WriteAllText(fixture.StringsPath, updated, new UTF8Encoding(false));
            return Task.CompletedTask;
        });
        var draft = GetDraftDocument(editor);
        var technology = FindTechnology(draft, "DeleteMe");
        InvokePrivate(editor, "QueueTechnologyLifecycleStringRemovals", technology);
        technology.Remove();
        GetPrivateField<Dictionary<string, XElement>>(editor, "_modified").Remove("DeleteMe");

        Assert.True(CompleteOnDispatcher(editor.SaveAsync));

        var savedStrings = StringTableDocument.Parse(File.ReadAllText(fixture.StringsPath)).ToDictionary();
        Assert.DoesNotContain(displayId, savedStrings.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(rolloverId, savedStrings.Keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void TechnologyRenameCommitsMinorGodAndStringLifecycleWithoutLosingUnmanagedContent()
    {
        EnsureAvalonia();
        const string oldTechName = "MythicAgePreservedGod";
        const string newTechName = "MythicAgeRenamedGod";
        const string oldDisplayId = "STR_TECH_MYTHIC_AGE_PRESERVED_GOD_NAME";
        const string oldRolloverId = "STR_TECH_MYTHIC_AGE_PRESERVED_GOD_LR";
        using var fixture = new TechnologyEditorFixture($"""
            <techtreemods custom="keep-tech-root">
              <!-- keep technology comment -->
              <tech name="{oldTechName}" future="yes">
                <displaynameid>{oldDisplayId}</displaynameid>
                <rollovertextid>{oldRolloverId}</rollovertextid>
                <flag>AgeUpgrade</flag>
                <futuretechnology><payload /></futuretechnology>
              </tech>
            </techtreemods>
            """, $"// keep string comment\r\n{oldDisplayId} = Preserved God\r\n{oldRolloverId} = Preserved rollover\r\n");
        File.WriteAllText(fixture.MinorGodsPath, $"""
            <minorgodsmods custom="keep-minor-root">
              <!-- keep minor comment -->
              <{oldTechName} future="yes" portraitfilename="portrait.png">
                <futureminor>42</futureminor>
              </{oldTechName}>
            </minorgodsmods>
            """, new UTF8Encoding(false));
        var stringValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [oldDisplayId] = "Preserved God",
            [oldRolloverId] = "Preserved rollover"
        };
        var editor = CreateEditor(
            fixture,
            (updates, removals) =>
            {
                var encoded = PreservedTextEncoding.Decode(File.ReadAllBytes(fixture.StringsPath));
                var updated = StringTableDocument.Parse(encoded.Text).Apply(
                    updates,
                    removals,
                    StringTableSection.Technologies);
                File.WriteAllBytes(fixture.StringsPath, encoded.Encode(updated));
                return Task.CompletedTask;
            },
            resolveStringAsync: id => Task.FromResult(stringValues.GetValueOrDefault(id)),
            prepareMinorGodLifecycle: (technologyDocument, renames, removals) =>
            {
                var prepared = ModDocumentPersistence.Prepare(
                    fixture.MinorGodsPath,
                    ModXmlDocumentKind.MinorGods);
                MinorGodCatalog.ApplyTechnologyLifecycle(
                    prepared.Document,
                    technologyDocument,
                    renames,
                    removals);
                return prepared;
            });

        Assert.True(CompleteOnDispatcher(() => editor.RenameTechnologyAsync(oldTechName, newTechName)));

        var technologies = XDocument.Load(fixture.TechtreePath, LoadOptions.PreserveWhitespace);
        var renamedTechnology = FindTechnology(technologies, newTechName);
        Assert.Equal("keep-tech-root", (string?)technologies.Root?.Attribute("custom"));
        Assert.Equal("yes", (string?)renamedTechnology.Attribute("future"));
        Assert.NotNull(renamedTechnology.Element("futuretechnology")?.Element("payload"));
        Assert.Contains(technologies.Root!.Nodes().OfType<XComment>(), comment =>
            comment.Value.Contains("keep technology comment", StringComparison.Ordinal));

        var minorGods = XDocument.Load(fixture.MinorGodsPath, LoadOptions.PreserveWhitespace);
        var renamedMinorGod = MinorGodCatalog.FindDefinition(minorGods.Root!, newTechName);
        Assert.NotNull(renamedMinorGod);
        Assert.Equal("keep-minor-root", (string?)minorGods.Root?.Attribute("custom"));
        Assert.Equal("yes", (string?)renamedMinorGod!.Attribute("future"));
        Assert.Equal("42", renamedMinorGod.Element("futureminor")?.Value);
        Assert.Contains(minorGods.Root!.Nodes().OfType<XComment>(), comment =>
            comment.Value.Contains("keep minor comment", StringComparison.Ordinal));

        var stringText = File.ReadAllText(fixture.StringsPath);
        var strings = StringTableDocument.Parse(stringText).ToDictionary();
        Assert.Contains("// keep string comment", stringText, StringComparison.Ordinal);
        Assert.DoesNotContain(oldDisplayId, strings.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(oldRolloverId, strings.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Preserved God", strings["STR_TECH_MYTHIC_AGE_RENAMED_GOD_NAME"]);
        Assert.Equal("Preserved rollover", strings["STR_TECH_MYTHIC_AGE_RENAMED_GOD_LR"]);
    }

    [Fact]
    public void CollidingTechnologyStringNamespacesBlockTheRealSavePipeline()
    {
        EnsureAvalonia();
        using var fixture = new TechnologyEditorFixture("""
            <techtreemods>
              <tech name="CamelRider" />
              <tech name="Camel_Rider" />
            </techtreemods>
            """);
        var before = File.ReadAllBytes(fixture.TechtreePath);
        var editor = CreateEditor(fixture);

        Assert.False(CompleteOnDispatcher(editor.SaveAsync));
        Assert.Equal(before, File.ReadAllBytes(fixture.TechtreePath));
    }

    [Fact]
    public void ExternalTechnologyChangeIsNotOverwrittenWhenNoReloadDecisionCanBeShown()
    {
        EnsureAvalonia();
        using var fixture = new TechnologyEditorFixture("""
            <techtreemods><tech name="Local"><researchpoints>1</researchpoints></tech></techtreemods>
            """);
        var editor = CreateEditor(fixture);
        FindTechnology(GetDraftDocument(editor), "Local").Element("researchpoints")!.Value = "2";
        const string externalXml =
            "<techtreemods><tech name=\"Local\"><researchpoints>3</researchpoints><external /></tech></techtreemods>";
        File.WriteAllText(fixture.TechtreePath, externalXml, new UTF8Encoding(false));

        Assert.False(CompleteOnDispatcher(editor.SaveAsync));

        Assert.Equal(externalXml, File.ReadAllText(fixture.TechtreePath));
        Assert.Equal("2", FindTechnology(GetDraftDocument(editor), "Local").Element("researchpoints")?.Value);
    }

    [Fact]
    public void MixedDirectAndContainedEffectsRoundTripWithoutBeingCombinedOrReordered()
    {
        EnsureAvalonia();
        using var fixture = new TechnologyEditorFixture("""
            <techtreemods>
              <tech name="Mixed">
                <effect type="First" custom="direct" />
                <future between="yes" />
                <effects custom="container">
                  <futureMetadata />
                  <effect type="Second" custom="contained" />
                </effects>
                <effect type="Third" custom="last" />
              </tech>
            </techtreemods>
            """);
        var expected = XDocument.Load(fixture.TechtreePath);
        var editor = CreateEditor(fixture);

        Assert.True(CompleteOnDispatcher(editor.SaveAsync));

        var saved = XDocument.Load(fixture.TechtreePath);
        Assert.True(XNode.DeepEquals(expected, saved));
        var technology = FindTechnology(saved, "Mixed");
        Assert.Equal(["effect", "future", "effects", "effect"],
            technology.Elements().Select(element => element.Name.LocalName));
    }

    [Fact]
    public void PendingRenameMigratesTechnologyReferencesInTheCommittedCandidate()
    {
        EnsureAvalonia();
        using var fixture = new TechnologyEditorFixture("""
            <techtreemods>
              <tech name="OldName" />
              <tech name="Consumer">
                <effect type="Data" subtype="ResourceIfTechActive" resource="Food" tech="OldName" amount="1" relativity="Assign">
                  <target type="Player" />
                </effect>
              </tech>
            </techtreemods>
            """);
        var editor = CreateEditor(fixture);
        var document = GetDraftDocument(editor);
        var renamed = FindTechnology(document, "OldName");
        renamed.SetAttributeValue("name", "NewName");
        var modified = GetPrivateField<Dictionary<string, XElement>>(editor, "_modified");
        modified.Remove("OldName");
        modified.Add("NewName", renamed);
        GetPrivateField<List<TechnologyRenameOperation>>(editor, "_pendingTechnologyRenames")
            .Add(new TechnologyRenameOperation("OldName", "NewName"));

        Assert.True(CompleteOnDispatcher(editor.SaveAsync));

        var saved = XDocument.Load(fixture.TechtreePath);
        Assert.NotNull(FindTechnology(saved, "NewName"));
        Assert.Equal("NewName", FindTechnology(saved, "Consumer")
            .Descendants("effect").Single().Attribute("tech")?.Value);
        Assert.DoesNotContain(saved.Descendants("tech"), technology =>
            string.Equals((string?)technology.Attribute("name"), "OldName", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ReferencedTechnologyIsDetectedBeforeDeletionEvenAcrossEffectShapes()
    {
        var document = XDocument.Parse("""
            <techtreemods>
              <tech name="DeleteMe" />
              <tech name="Direct"><effect type="Data" subtype="ResourceIfTechActive" tech="DeleteMe" /></tech>
              <tech name="Contained"><effects><effect type="SetName" tech="DeleteMe" /></effects></tech>
            </techtreemods>
            """);
        var usages = TechnologyReferenceIndex.Build(
            [new TechnologyReferenceSource("Mod techtree", document, TechnologyReferenceDocumentKind.Techtree, CanMigrate: true)],
            ["DeleteMe"]);

        Assert.Equal(2, usages.Count(usage => usage.TechnologyName == "DeleteMe"));
        Assert.All(usages, usage =>
        {
            Assert.True(usage.IsRecognized);
            Assert.True(usage.CanMigrate);
        });
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void ConfiguredOriginalTechnologyCorpusBuildsEveryEditorWithoutMutation()
    {
        var dataBarPath = Environment.GetEnvironmentVariable("AOM_DATA_BAR");
        if (string.IsNullOrWhiteSpace(dataBarPath) || !File.Exists(dataBarPath))
            return;

        EnsureAvalonia();
        using var stream = File.OpenRead(dataBarPath);
        var archive = new BarArchive(stream);
        Assert.True(archive.Load(out var error), error.ToString());
        var entries = archive.Entries!
            .Where(entry => entry.Name.Equals("techtree.xml.XMB", StringComparison.OrdinalIgnoreCase) ||
                            entry.Name.Equals("aotg_techtree.techtree.XMB", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.NotEmpty(entries);
        var documents = entries.Select(entry => XDocument.Parse(
                XmbReader.ToFormattedXml(entry.ReadDataDecompressed(stream))!,
                LoadOptions.PreserveWhitespace))
            .ToList();
        var before = documents.Select(document => document.ToString(SaveOptions.DisableFormatting)).ToList();
        using var fixture = new TechnologyEditorFixture("<techtreemods />");
        var editor = CreateEditor(fixture, originalDocuments: documents);

        AvaloniaTestHost.Run(() => editor.SetModifiedMode(false));
        var technologyNames = editor.GetTechnologyNames(modified: false);
        Assert.NotEmpty(technologyNames);
        foreach (var technologyName in technologyNames)
        {
            AvaloniaTestHost.Run(() => editor.SelectTechnology(technologyName));
            AwaitActiveEditorBuild(editor);
            Assert.DoesNotContain("could not be built", GetStatusText(editor), StringComparison.OrdinalIgnoreCase);
        }

        Assert.Equal(before, documents.Select(document => document.ToString(SaveOptions.DisableFormatting)));
        Assert.False(editor.IsDirty);
    }

    private static void AwaitActiveEditorBuild(TechnologyEditorView editor)
    {
        while (true)
        {
            var build = GetPrivateField<Task>(editor, "_activeEditorBuildTask");
            AvaloniaTestHost.RunAsync(() => build);
            if (ReferenceEquals(build, GetPrivateField<Task>(editor, "_activeEditorBuildTask")))
                return;
        }
    }

    private static string GetStatusText(TechnologyEditorView editor)
        => AvaloniaTestHost.Run(() =>
            GetPrivateField<Avalonia.Controls.TextBlock>(editor, "_statusMessage").Text ?? "");

    private static TechnologyEditorView CreateEditor(
        TechnologyEditorFixture fixture,
        Func<IReadOnlyDictionary<string, string>, IReadOnlyCollection<string>, Task>? saveStringsAsync = null,
        IEnumerable<XDocument>? originalDocuments = null,
        Func<string, Task<string?>>? resolveStringAsync = null,
        Func<IEnumerable<string>, IReadOnlySet<string>>? externalStringReferenceProvider = null,
        Func<XDocument, IReadOnlyList<TechnologyRenameOperation>, IReadOnlyCollection<string>, PreparedModXmlDocument?>?
            prepareMinorGodLifecycle = null)
        => AvaloniaTestHost.Run(() => fixture.CreateEditor(
            saveStringsAsync,
            originalDocuments,
            resolveStringAsync,
            externalStringReferenceProvider,
            prepareMinorGodLifecycle));

    private static T CompleteOnDispatcher<T>(Func<Task<T>> action)
        => AvaloniaTestHost.RunAsync(action);

    private static XDocument GetDraftDocument(TechnologyEditorView editor)
        => GetPrivateField<XDocument>(editor, "_modDocument");

    private static XElement FindTechnology(XContainer document, string name)
        => document.Descendants().Single(element =>
            element.Name.LocalName.Equals("tech", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(element.Attributes().FirstOrDefault(attribute =>
                    attribute.Name.LocalName.Equals("name", StringComparison.OrdinalIgnoreCase))?.Value,
                name,
                StringComparison.OrdinalIgnoreCase));

    private static T GetPrivateField<T>(object instance, string name)
        => (T)(instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                   ?.GetValue(instance)
               ?? throw new MissingFieldException(instance.GetType().FullName, name));

    private static void InvokePrivate(object instance, string name, params object[] arguments)
    {
        var method = instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
                     ?? throw new MissingMethodException(instance.GetType().FullName, name);
        method.Invoke(instance, arguments);
    }

    private static void EnsureAvalonia()
        => AvaloniaTestHost.EnsureStarted();

    private sealed class TechnologyEditorFixture : IDisposable
    {
        private readonly TestWorkspace _workspace;

        public TechnologyEditorFixture(string technologyXml, string strings = "")
        {
            _workspace = new TestWorkspace(nameof(TechnologyEditorFixture));
            DirectoryPath = _workspace.RootPath;
            TechtreePath = _workspace.WriteText("techtree_mods.xml", technologyXml);
            StringsPath = _workspace.WriteText("stringmods.txt", strings);
            MinorGodsPath = _workspace.WriteText("minor_gods_mods.xml", "<minorgodsmods />");
        }

        public string DirectoryPath { get; }
        public string TechtreePath { get; }
        public string StringsPath { get; }
        public string MinorGodsPath { get; }

        public TechnologyEditorView CreateEditor(
            Func<IReadOnlyDictionary<string, string>, IReadOnlyCollection<string>, Task>? saveStringsAsync = null,
            IEnumerable<XDocument>? originalDocuments = null,
            Func<string, Task<string?>>? resolveStringAsync = null,
            Func<IEnumerable<string>, IReadOnlySet<string>>? externalStringReferenceProvider = null,
            Func<XDocument, IReadOnlyList<TechnologyRenameOperation>, IReadOnlyCollection<string>, PreparedModXmlDocument?>?
                prepareMinorGodLifecycle = null)
            => new(
                originalDocuments,
                baseGameplayDirectory: null,
                TechtreePath,
                resolveStringAsync,
                saveStringsAsync: saveStringsAsync,
                modStringsPath: StringsPath,
                prepareMinorGodLifecycle: prepareMinorGodLifecycle,
                externalStringReferenceProvider: externalStringReferenceProvider);

        public void Dispose() => _workspace.Dispose();
    }
}
