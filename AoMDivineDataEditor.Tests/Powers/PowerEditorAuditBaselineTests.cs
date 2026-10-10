using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.GameData;
using AoMDivineDataEditor.Windows;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class PowerEditorAuditBaselineTests
{
    private const string BaselineRelativePath = "AoMDivineDataEditor.Tests/Powers/Baselines/power-schema-baseline.json";

    [Fact]
    public void CheckedInCorpusDeclaresAllOriginalPowerTypesAndFields()
    {
        var snapshot = LoadBaseline();
        var types = snapshot.Powers.Select(power => power.Type)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var policies = PowerEditorAuditInventory.Classify(snapshot);

        Assert.Equal(161, snapshot.Powers.Count);
        Assert.Equal(63, types.Count);
        Assert.Equal(509, snapshot.ElementNames.Count); // 508 descendant names plus the <power> root.
        Assert.Equal(119, snapshot.AttributeNames.Count);
        Assert.Equal(PowerEditorAuditInventory.KnownPowerTypes.OrderBy(value => value,
            StringComparer.OrdinalIgnoreCase), types);
        Assert.Equal(snapshot.ElementNames.Count + snapshot.AttributeNames.Count, policies.Count);
        Assert.All(snapshot.Powers, power =>
        {
            Assert.False(string.IsNullOrWhiteSpace(power.Name));
            Assert.False(string.IsNullOrWhiteSpace(power.Type));
            Assert.Equal(power.Fields.Count,
                power.Fields.Select(field => field.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.All(power.Fields, field =>
            {
                Assert.True(field.Count > 0, $"{power.Name}: {field.Path}");
                Assert.NotEmpty(field.Shapes);
            });
        });
    }

    [Fact]
    public void EveryBaseFieldHasAnExplicitCoverageRoute()
    {
        var snapshot = LoadBaseline();
        var policies = PowerEditorAuditInventory.Classify(snapshot);
        var keys = policies.Select(policy => $"{policy.Scope}:{policy.Name}").ToList();

        Assert.Equal(keys.Count, keys.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(policies, policy =>
        {
            Assert.False(string.IsNullOrWhiteSpace(policy.Route));
            Assert.True(Enum.IsDefined(policy.Mode));
        });
        Assert.Equal(4, policies.Count(policy => policy.Mode == PowerEditorCoverageMode.Unsupported));
        Assert.DoesNotContain(policies, policy => policy.Mode == PowerEditorCoverageMode.PreserveOnly);

        var expectedGaps = PowerEditorAuditInventory.KnownGaps
            .Select(policy => $"{policy.Scope}:{policy.Name}")
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase);
        var actualGaps = policies.Where(policy => policy.Mode == PowerEditorCoverageMode.Unsupported)
            .Select(policy => $"{policy.Scope}:{policy.Name}")
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(expectedGaps, actualGaps);
    }

    [Fact]
    public void EveryFieldDeclaredSupportedStillHasAnEditorImplementationToken()
    {
        var snapshot = LoadBaseline();
        var source = string.Concat(
            ProjectSource.Read("Windows", "PowerEditorView.axaml.cs"),
            ProjectSource.Read("Windows", "PowerEditorView.axaml"),
            ProjectSource.Read("Classes", "PowerCatalog.cs"),
            ProjectSource.Read("Classes", "MajorGodCatalog.cs"));
        var policies = PowerEditorAuditInventory.Classify(snapshot)
            .Where(policy => policy.Mode is PowerEditorCoverageMode.Structured or PowerEditorCoverageMode.Shared);

        Assert.All(policies, policy => Assert.Contains(policy.Name, source,
            StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void NestedAttributesCannotBeCoveredOnlyByAnUnrelatedEditorToken()
    {
        var snapshot = LoadBaseline();
        var powerSource = string.Concat(
            ProjectSource.Read("Windows", "PowerEditorView.axaml.cs"),
            ProjectSource.Read("Windows", "PowerEditorView.axaml"),
            ProjectSource.Read("Classes", "PowerCatalog.cs"));
        var sharedSource = ProjectSource.Read("Classes", "MajorGodCatalog.cs");

        foreach (var attribute in snapshot.AttributeNames)
        {
            if (powerSource.Contains(attribute, StringComparison.OrdinalIgnoreCase))
                continue;
            Assert.True(PowerEditorAuditInventory.SharedNestedAttributeRoutes.TryGetValue(
                    attribute, out var route),
                $"Base Power attribute '{attribute}' has no Power Editor route.");
            Assert.Equal("MajorGodCatalog.TerrainCreepFlags", route);
            Assert.Contains(attribute, sharedSource, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Equal(["allowdisconnectedgrowth", "avoidimpassable"],
            PowerEditorAuditInventory.SharedNestedAttributeRoutes.Keys
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void EveryBaseNestedPathHasItsOwnTypeSpecificEditorRoute()
    {
        var snapshot = LoadBaseline();
        var nestedFields = snapshot.Powers
            .SelectMany(power => power.Fields
                .Where(field => field.Path.Contains('/'))
                .Select(field => (Key: $"{power.Type}/{field.Path}", Field: field)))
            .GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Select(item => item.Field).ToList(),
                StringComparer.OrdinalIgnoreCase);
        var routes = PowerEditorAuditInventory.KnownNestedFieldRoutes;
        var source = ProjectSource.Read("Windows", "PowerEditorView.axaml.cs");

        Assert.Equal(nestedFields.Keys.OrderBy(value => value, StringComparer.OrdinalIgnoreCase),
            routes.Keys.OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
        foreach (var (key, fields) in nestedFields)
        {
            var route = routes[key];
            Assert.False(string.IsNullOrWhiteSpace(route.EditorToken));
            Assert.Contains(route.EditorToken, source, StringComparison.Ordinal);

            var baseAttributes = fields.SelectMany(field => field.Attributes)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase);
            Assert.Equal(baseAttributes, route.SupportedAttributes
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void KnownNearSpellingsCannotMasqueradeAsBaseFieldCoverage()
    {
        var snapshot = LoadBaseline();
        var source = ProjectSource.Read("Windows", "PowerEditorView.axaml.cs");

        foreach (var (baseName, editorName) in PowerEditorAuditInventory.KnownSpellingMismatches)
        {
            Assert.Contains(baseName, snapshot.ElementNames.Concat(snapshot.AttributeNames),
                StringComparer.OrdinalIgnoreCase);
            Assert.False(baseName.Equals(editorName, StringComparison.OrdinalIgnoreCase));
            Assert.Contains(editorName, source, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(PowerEditorAuditInventory.KnownGaps, policy =>
                policy.Name.Equals(baseName, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void ShiftingSandsMalformedOriginalKeepsOnlyItsLastRangeIndicatorInTheEditor()
    {
        var document = XDocument.Parse("""
            <powers>
              <power name="ShiftingSands" type="ShiftingSands">
                <rangeindicator>First</rangeindicator>
                <rangeindicator>Last</rangeindicator>
              </power>
            </powers>
            """, LoadOptions.PreserveWhitespace);

        var definition = Assert.Single(PowerCatalog.ExtractDefinitions([document], isBuiltIn: true));
        var indicator = Assert.Single(definition.SourceElement.Elements(), element =>
            element.Name.LocalName.Equals("rangeindicator", StringComparison.OrdinalIgnoreCase));

        Assert.Equal("Last", indicator.Value);
        Assert.Contains("ShiftingSands/rangeindicator", PowerEditorAuditInventory.KnownSourceNormalizations.Keys);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void InstalledOriginalCorpusMatchesTheCheckedInSchemaBaseline()
    {
        var documents = LoadInstalledCorpus();
        if (documents.Count == 0)
            return;

        var actual = PowerSchemaCorpusSnapshot.Capture(documents);
        if (Environment.GetEnvironmentVariable("UPDATE_POWER_SCHEMA_BASELINE") == "1")
        {
            var outputPath = Path.Combine(ProjectSource.RootPath,
                BaselineRelativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.WriteAllText(outputPath, actual.ToJson());
        }

        var expected = LoadBaseline();
        Assert.Equal(expected.ToJson(), actual.ToJson());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void EveryInstalledOriginalOpensWithoutPopulationFailure()
    {
        var documents = LoadInstalledCorpus();
        if (documents.Count == 0)
            return;
        var definitions = PowerCatalog.ExtractDefinitions(documents, isBuiltIn: true);
        Assert.Equal(161, definitions.Count);

        AvaloniaTestHost.EnsureStarted();
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView(definitions, null));
        try
        {
            foreach (var definition in definitions)
            {
                var exception = Record.Exception(() => AvaloniaTestHost.RunAsync(
                    () => editor.SelectPowerAsync(definition.Name)));
                Assert.True(exception == null,
                    $"Opening original power '{definition.Name}' ({GetType(definition.SourceElement)}) failed: {exception}");
            }
        }
        finally
        {
            AvaloniaTestHost.Run(() => editor.SelectPower(null));
            editor = null!;
            AvaloniaTestHost.CollectUiGarbage();
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void UntouchedModEligibleOriginalClonesRemainSemanticallyIdenticalAfterOpenAndSave()
    {
        var documents = LoadInstalledCorpus();
        if (documents.Count == 0)
            return;
        var definitions = PowerCatalog.ExtractDefinitions(documents, isBuiltIn: true);
        Assert.Equal(161, definitions.Count);
        var modEligibleDefinitions = definitions.Where(definition =>
            PowerCatalog.IsModCatalogPower(definition.SourceElement)).ToList();
        var abilityCatalogRecords = definitions.Except(modEligibleDefinitions)
            .Select(definition => definition.Name).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList();
        Assert.Equal(["ShieldBlessingTechree", "SpawnRewardTechree", "YinAndYangTechree"],
            abilityCatalogRecords);
        Assert.Equal(158, modEligibleDefinitions.Count);

        using var workspace = new TestWorkspace(nameof(UntouchedModEligibleOriginalClonesRemainSemanticallyIdenticalAfterOpenAndSave));
        var modPath = workspace.WriteText(PowerCatalog.ModFileName,
            new XDocument(new XElement(PowerCatalog.ModRootName,
                    modEligibleDefinitions.Select(definition => new XElement(definition.SourceElement))))
                .ToString(SaveOptions.DisableFormatting));
        var stringsPath = workspace.WriteText("string_table.txt", "");
        var before = XDocument.Load(modPath);
        var queuedStringUpdates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var queuedStringRemovals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var corpusValues = modEligibleDefinitions.SelectMany(definition => definition.SourceElement.Descendants())
            .Where(element => !element.HasElements)
            .Select(element => element.Value.Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        AvaloniaTestHost.EnsureStarted();
        var editor = AvaloniaTestHost.Run(() => new PowerEditorView([], modPath,
            saveStringsAsync: (updates, removals) =>
            {
                foreach (var update in updates) queuedStringUpdates[update.Key] = update.Value;
                queuedStringRemovals.UnionWith(removals);
                return Task.CompletedTask;
            },
            modStringsPath: stringsPath,
            protoUnitNames: corpusValues,
            unitTypeNames: corpusValues,
            terrainCreepNames: corpusValues,
            protoActionNames: corpusValues,
            techNames: corpusValues,
            animationNames: corpusValues,
            boneNames: corpusValues,
            tacticNames: corpusValues));
        try
        {
            AvaloniaTestHost.Run(() => editor.SetModifiedMode(true));
            foreach (var definition in modEligibleDefinitions)
            {
                AvaloniaTestHost.RunAsync(() => editor.SelectPowerAsync(definition.Name));
                Assert.False(AvaloniaTestHost.Run(() => editor.IsPowerDirty(definition.Name)),
                    $"Opening untouched clone '{definition.Name}' marked it dirty.");
            }
            var invalidEffectOwners = modEligibleDefinitions.Where(definition =>
                    definition.SourceElement.Descendants().Any(element =>
                        (element.Name.LocalName.Equals("effect", StringComparison.OrdinalIgnoreCase) ||
                         (GetType(definition.SourceElement).Equals("EffectInArea", StringComparison.OrdinalIgnoreCase) &&
                          element.Name.LocalName.Equals("onhiteffect", StringComparison.OrdinalIgnoreCase))) &&
                        string.IsNullOrWhiteSpace(element.Attributes().FirstOrDefault(attribute =>
                            attribute.Name.LocalName.Equals("type", StringComparison.OrdinalIgnoreCase))?.Value)))
                .Select(definition => definition.Name);
            var saved = AvaloniaTestHost.RunAsync(() => editor.SaveAsync());
            var status = AvaloniaTestHost.Run(() => editor.StatusMessage);
            Assert.True(saved,
                $"Save was blocked: {status}. Originals containing an untyped <effect>: " +
                string.Join(", ", invalidEffectOwners));
        }
        finally
        {
            AvaloniaTestHost.Run(() => editor.SelectPower(null));
            editor = null!;
            AvaloniaTestHost.CollectUiGarbage();
        }

        var after = XDocument.Load(modPath);
        var changedPowers = before.Root!.Elements(PowerCatalog.EntryName)
            .Select(original => (Original: original, Saved: after.Root!.Elements(PowerCatalog.EntryName)
                .FirstOrDefault(saved => PowerCatalog.GetName(saved).Equals(PowerCatalog.GetName(original),
                    StringComparison.OrdinalIgnoreCase))))
            .Where(pair => pair.Saved == null || !XNode.DeepEquals(pair.Original, pair.Saved))
            .Select(pair => PowerCatalog.GetName(pair.Original))
            .ToList();
        Assert.True(XNode.DeepEquals(before, after),
            "Opening and saving untouched original clones changed their XML semantics: " +
            string.Join(", ", changedPowers));
        Assert.Empty(queuedStringUpdates);
        Assert.Empty(queuedStringRemovals);
    }

    private static PowerSchemaCorpusSnapshot LoadBaseline()
    {
        var path = Path.Combine(ProjectSource.RootPath,
            BaselineRelativePath.Replace('/', Path.DirectorySeparatorChar));
        return PowerSchemaCorpusSnapshot.Parse(File.ReadAllText(path));
    }

    private static IReadOnlyList<XDocument> LoadInstalledCorpus()
    {
        var dataBarPath = FindDataBar();
        if (dataBarPath == null)
            return [];
        using var stream = File.OpenRead(dataBarPath);
        var archive = new BarArchive(stream);
        Assert.True(archive.Load(out var error), error.ToString());
        var documents = PowerCatalog.ExtractBaseDocumentsFromBar(archive, dataBarPath);
        Assert.Equal(10, documents.Count);
        return documents;
    }

    private static string? FindDataBar()
    {
        var configured = Environment.GetEnvironmentVariable("AOM_DATA_BAR");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return configured;
        var workspacePath = Path.GetFullPath(Path.Combine(ProjectSource.RootPath, "..",
            "Age of Mythology Retold", "game", "data", "Data.bar"));
        return File.Exists(workspacePath) ? workspacePath : null;
    }

    private static string GetType(XElement power)
        => power.Attributes().FirstOrDefault(attribute =>
            attribute.Name.LocalName.Equals("type", StringComparison.OrdinalIgnoreCase))?.Value ?? "";
}
