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
public sealed class TechnologyEditorWorkflowTests
{
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
        IEnumerable<XDocument>? originalDocuments = null)
        => AvaloniaTestHost.Run(() => fixture.CreateEditor(saveStringsAsync, originalDocuments));

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
        }

        public string DirectoryPath { get; }
        public string TechtreePath { get; }
        public string StringsPath { get; }

        public TechnologyEditorView CreateEditor(
            Func<IReadOnlyDictionary<string, string>, IReadOnlyCollection<string>, Task>? saveStringsAsync = null,
            IEnumerable<XDocument>? originalDocuments = null)
            => new(
                originalDocuments,
                baseGameplayDirectory: null,
                TechtreePath,
                saveStringsAsync: saveStringsAsync,
                modStringsPath: StringsPath);

        public void Dispose() => _workspace.Dispose();
    }
}
