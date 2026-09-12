using AoMDivineDataEditor.Controls;
using AoMDivineDataEditor.Classes;
using System.Xml.Linq;
using Xunit;

namespace AoMDivineDataEditor.Tests;

[Trait("Category", TestCategories.Architecture)]
public sealed class PerformanceAndManagerArchitectureTests
{
    private sealed record Item(string Name, bool Original);

    [Fact]
    public void Shared_manager_filter_applies_source_search_and_stable_name_order_in_one_pass()
    {
        var items = new[]
        {
            new Item("Zulu", true),
            new Item("beta custom", false),
            new Item("Alpha custom", false),
            new Item("Beta original", true)
        };

        var result = ManagerListShell.FilterItems(
            items,
            item => item.Name,
            item => item.Original,
            "custom",
            "Custom");

        Assert.Equal(new[] { "Alpha custom", "beta custom" }, result.Select(item => item.Name));
    }

    [Fact]
    public void Command_catalog_and_browser_search_have_explicit_fast_paths()
    {
        var source = ProjectSource.Read("Windows", "ProtoEditorWindow.axaml.cs");
        var commandNames = ProjectSource.ExtractBetween(source, "private List<string> GetAvailableCommandNames()", "private static string GetProtoUnitCommandName");
        var manager = ProjectSource.ExtractBetween(source, "private async Task OpenProtoUnitCommandsManagerAsync()", "private async Task OpenUnitTypeManagerAsync()");
        var browserSearch = ProjectSource.ExtractBetween(source, "private void SearchBox_TextChanged", "private async void UnitsEntity_Click");

        Assert.True(commandNames.IndexOf("if (_cachedCommandNames != null)", StringComparison.Ordinal) <
                    commandNames.IndexOf("EnsureProtoUnitCommandCatalogLoaded();", StringComparison.Ordinal));
        Assert.Contains("var usageCounts = GetProtoUnitCommandUsageCounts();", manager, StringComparison.Ordinal);
        Assert.Contains("usageCounts.GetValueOrDefault(name)", manager, StringComparison.Ordinal);
        Assert.Contains("_entitySearchRefreshTimer.Start();", browserSearch, StringComparison.Ordinal);
        Assert.DoesNotContain("FilterUnitList();", browserSearch, StringComparison.Ordinal);
    }

    [Fact]
    public void Bulk_tech_type_usage_matches_property_and_effect_semantics()
    {
        var document = XDocument.Parse("""
            <techtree>
              <tech><techtype>A</techtype><techtype>A</techtype><effects>
                <effect techtype="A"><target type="TechType">B</target></effect>
                <effect><target excludetypes="A|B|A" /></effect>
              </effects></tech>
              <tech><techtype>B</techtype></tech>
            </techtree>
            """);

        var usage = TechTypeCatalog.GetTechnologyUsageCounts(document);

        Assert.Equal(new TechTypeUsage(1, 2), usage["A"]);
        Assert.Equal(new TechTypeUsage(1, 2), usage["B"]);
        Assert.Equal(usage["A"], TechTypeCatalog.GetTechnologyUsage(document, "a"));
    }

    [Fact]
    public void Manager_rows_do_not_rescan_documents_during_filter_refresh()
    {
        foreach (var file in new[] { "AbilitiesManagerWindow.cs", "ProtoUnitCommandsManagerWindow.cs" })
        {
            var source = ProjectSource.Read("Windows", file);
            var refresh = ProjectSource.ExtractBetween(source, "private void RefreshList()", "private Control CreateRow");
            Assert.DoesNotContain("_resolveUsageCount", refresh, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Cold_catalogs_and_technology_view_are_prepared_before_startup_overlay_closes()
    {
        var source = ProjectSource.Read("Windows", "ProtoEditorWindow.axaml.cs");
        var startup = ProjectSource.ExtractBetween(source, "private async Task InitializeProtoEditorAsync()", "private async Task ShowStartupLoadingAsync");
        var warmup = ProjectSource.ExtractBetween(source, "private void WarmInitialUnitEditorData()", "private async Task EnsureInitialModSelectionAsync()");

        var loadMod = startup.IndexOf("EnsureInitialModSelectionAsync", StringComparison.Ordinal);
        var warm = startup.IndexOf("WarmInitialUnitEditorData", StringComparison.Ordinal);
        var technologies = startup.IndexOf("EnsureTechnologyView", StringComparison.Ordinal);
        var hide = startup.IndexOf("HideStartupLoading", StringComparison.Ordinal);
        Assert.True(loadMod >= 0 && loadMod < warm && warm < technologies && technologies < hide);

        Assert.Contains("GetAvailableCommandNames", warmup, StringComparison.Ordinal);
        Assert.Contains("GetGlobalProtoActionAnimationNames", warmup, StringComparison.Ordinal);
        Assert.Contains("GetAvailableProtoActionModelAttachmentBones", warmup, StringComparison.Ordinal);
        Assert.Contains("EnsureAbilityCatalogLoadedForManager", warmup, StringComparison.Ordinal);
        Assert.Contains("GetBaseAbilityDocumentsFromLoadedBar", source, StringComparison.Ordinal);
        Assert.Contains("_cachedTacticsNames", source, StringComparison.Ordinal);

        var technologyInitialization = ProjectSource.ExtractBetween(
            source,
            "private void EnsureTechnologyView()",
            "private IReadOnlyList<TechnologyReferenceUsage> GetExternalTechnologyReferences()");
        Assert.DoesNotContain("LoadCurrentModAssetCatalogs", technologyInitialization, StringComparison.Ordinal);

        var modLoad = ProjectSource.ExtractBetween(
            source,
            "private bool TryLoadModFile(string path)",
            "private async Task<bool> TryLoadModFileWithFeedbackAsync(string path)");
        Assert.Contains("LoadCurrentModAssetCatalogs();", modLoad, StringComparison.Ordinal);
    }

}
