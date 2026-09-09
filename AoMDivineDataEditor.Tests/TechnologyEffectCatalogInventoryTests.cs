using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.GameData;
using AoMDivineDataEditor.Windows;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class TechnologyEffectCatalogInventoryTests
{
    [Fact]
    public void AuthoritativeInventoryCreatesTheRuntimeCatalogSnapshot()
    {
        var snapshot = TechnologyEditorView.GetEffectCatalogRuntimeSnapshot();
        var result = TechnologyEffectCatalogAudit.Compare(snapshot);

        Assert.True(result.RuntimeCatalogsMatch, FormatDiscrepancies(result));
        Assert.Equal(TechnologyEffectCatalogInventory.CreatableEffectTypes, snapshot.EffectTypes);
        Assert.Equal(TechnologyEffectCatalogInventory.CreatableDataSubtypes, snapshot.DataSubtypes);
        Assert.Equal(["Cost / cost"], result.CaseAliases);
    }

    [Fact]
    public void EditorUsesTheInventoryWithoutDuplicatedCreationCatalogs()
    {
        var source = ReadTechnologyEditor();

        Assert.DoesNotContain("private static readonly string[] TechnologyEffectTypes", source, StringComparison.Ordinal);
        Assert.DoesNotContain("private static readonly string[] TechnologyDataEffectSubtypes", source, StringComparison.Ordinal);
        Assert.DoesNotContain("private static readonly HashSet<string> StructuredTechnologyEffectTypes", source, StringComparison.Ordinal);
        Assert.Contains("TechnologyEffectCatalogInventory.CreatableEffectTypes", source, StringComparison.Ordinal);
        Assert.Contains("TechnologyEffectCatalogInventory.CreatableDataSubtypes", source, StringComparison.Ordinal);
        Assert.Contains("TechnologyEffectCatalogInventory.IsStructuredDataSubtype(subtype)", source, StringComparison.Ordinal);
        Assert.Contains("TechnologyEffectCatalogInventory.IsStructuredEffectType(currentType)", source, StringComparison.Ordinal);
        Assert.Contains("TechnologyEffectCatalogInventory.StructuredEffectTypes", source, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryCreatableAndImportOnlyEntryHasAnExplicitPolicy()
    {
        var entries = TechnologyEffectCatalogInventory.Entries;
        Assert.All(entries, entry =>
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Name));
            Assert.False(string.IsNullOrWhiteSpace(entry.LegacyRoute));
        });

        var exactKeys = entries.Select(entry => (entry.Scope, entry.Name)).ToList();
        Assert.Equal(exactKeys.Count, exactKeys.Distinct().Count());

        var caseCollisions = entries
            .Where(entry => entry.Scope == TechnologyEffectCatalogScope.DataSubtype &&
                            entry.Visibility == TechnologyEffectCatalogVisibility.Creatable)
            .GroupBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .ToList();
        var alias = Assert.Single(caseCollisions);
        Assert.Equal(["Cost", "cost"], alias.Select(entry => entry.Name).ToArray());
        Assert.Equal("Cost", alias.Single(entry => entry.Name == "cost").AliasOf);
    }

    [Fact]
    public void ImportOnlyAndDeprecatedEntriesCannotLeakIntoCreationCatalogs()
    {
        var snapshot = TechnologyEditorView.GetEffectCatalogRuntimeSnapshot();
        var hidden = TechnologyEffectCatalogInventory.Entries.Where(entry =>
            entry.Visibility != TechnologyEffectCatalogVisibility.Creatable);

        foreach (var entry in hidden)
        {
            Assert.DoesNotContain(snapshot.EffectTypes,
                value => value.Equals(entry.Name, StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(snapshot.DataSubtypes,
                value => value.Equals(entry.Name, StringComparison.OrdinalIgnoreCase));
        }

        var flag = Assert.Single(hidden, entry => entry.Name == "Flag");
        Assert.Equal(TechnologyEffectCatalogEditorMode.Structured, flag.EditorMode);
        Assert.Equal(TechnologyEffectCatalogValidationMode.PreserveOnly, flag.ValidationMode);
    }

    [Fact]
    public void EveryEntryDeclaredStructuredStillReachesTheLegacyStructuredDispatcher()
    {
        AvaloniaTestHost.EnsureStarted();
        var editor = AvaloniaTestHost.Run(() => new TechnologyEditorView());
        var structured = TechnologyEffectCatalogInventory.Entries.Where(entry =>
            entry.EditorMode == TechnologyEffectCatalogEditorMode.Structured).ToList();

        foreach (var entry in structured)
        {
            var routed = AvaloniaTestHost.RunAsync(() =>
                entry.Scope == TechnologyEffectCatalogScope.DataSubtype
                    ? editor.ProbeStructuredEffectEditorForAuditAsync("Data", entry.Name)
                    : entry.Name == "Data"
                        ? Task.FromResult(true)
                        : editor.ProbeStructuredEffectEditorForAuditAsync(entry.Name));
            Assert.True(routed, $"'{entry.Name}' did not reach its declared structured route '{entry.LegacyRoute}'.");
        }
    }

    [Fact]
    public void CorpusAuditSeparatesUnknownImportOnlyAndDeprecatedEntries()
    {
        var document = XDocument.Parse("""
            <techtree>
              <tech name="Audit">
                <effect type="Data" subtype="Flag" />
                <effect type="Data" subtype="ActionAdd" />
                <effect type="Data" subtype="ShowWorldView" />
                <effect type="FutureEffect" />
              </tech>
            </techtree>
            """);

        var result = TechnologyEffectCatalogAudit.Compare(
            TechnologyEditorView.GetEffectCatalogRuntimeSnapshot(),
            TechnologyEffectCatalogAudit.CountCorpus([document]));

        Assert.Contains(result.ImportOnlyCorpusEntries, item => item.Name == "Flag" && item.Count == 1);
        Assert.Contains(result.ImportOnlyCorpusEntries, item => item.Name == "ActionAdd" && item.Count == 1);
        Assert.Contains(result.DeprecatedCorpusEntries, item => item.Name == "ShowWorldView" && item.Count == 1);
        Assert.Contains(result.UnclassifiedCorpusEntries, item => item.Name == "FutureEffect" && item.Count == 1);
        Assert.DoesNotContain(result.UnclassifiedCorpusEntries, item => item.Name == "Flag");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void ConfiguredOriginalCorpusMatchesTheCheckedInAudit()
    {
        var dataBarPath = Environment.GetEnvironmentVariable("AOM_DATA_BAR");
        if (string.IsNullOrWhiteSpace(dataBarPath) || !File.Exists(dataBarPath))
            return;

        using var stream = File.OpenRead(dataBarPath);
        var archive = new BarArchive(stream);
        Assert.True(archive.Load(out var error), error.ToString());
        var documents = archive.Entries!
            .Where(entry => entry.Name.Equals("techtree.xml.XMB", StringComparison.OrdinalIgnoreCase) ||
                            entry.Name.Equals("aotg_techtree.techtree.XMB", StringComparison.OrdinalIgnoreCase))
            .Select(entry => XDocument.Parse(XmbReader.ToFormattedXml(entry.ReadDataDecompressed(stream))!))
            .ToList();
        var occurrences = TechnologyEffectCatalogAudit.CountCorpus(documents);
        var result = TechnologyEffectCatalogAudit.Compare(
            TechnologyEditorView.GetEffectCatalogRuntimeSnapshot(), occurrences);

        Assert.Empty(result.UnclassifiedCorpusEntries);
        Assert.Empty(result.DeprecatedCorpusEntries);
        Assert.Equal(1455, documents.Sum(document => document.Descendants().Count(element =>
            element.Name.LocalName.Equals("tech", StringComparison.OrdinalIgnoreCase))));
        Assert.Equal(6985, occurrences
            .Where(item => item.Scope == TechnologyEffectCatalogScope.EffectType)
            .Sum(item => item.Count));
        Assert.Equal(12, occurrences.Count(item => item.Scope == TechnologyEffectCatalogScope.EffectType));
        Assert.Equal(126, occurrences.Count(item => item.Scope == TechnologyEffectCatalogScope.DataSubtype));
        var flag = Assert.Single(result.ImportOnlyCorpusEntries, item => item.Name == "Flag");
        Assert.Equal(4, flag.Count);
    }

    private static string FormatDiscrepancies(TechnologyEffectCatalogAuditResult result)
        => $"Missing types: {string.Join(", ", result.MissingEffectTypes)}; " +
           $"extra types: {string.Join(", ", result.ExtraEffectTypes)}; " +
           $"missing Data subtypes: {string.Join(", ", result.MissingDataSubtypes)}; " +
           $"extra Data subtypes: {string.Join(", ", result.ExtraDataSubtypes)}; " +
           $"missing structured types: {string.Join(", ", result.MissingStructuredEffectTypes)}; " +
           $"extra structured types: {string.Join(", ", result.ExtraStructuredEffectTypes)}";

    private static string ReadTechnologyEditor()
        => File.ReadAllText(Path.Combine(FindProjectRoot(), "Windows", "TechnologyEditorView.axaml.cs"));

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AoMDivineDataEditor.csproj")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate project root.");
    }
}
