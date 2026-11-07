using AoMDivineDataEditor.Classes;

namespace AoMDivineDataEditor.Windows;

public partial class ProtoEditorWindow
{
    // Reuse the same domain producers as initial editor composition. Refreshing
    // an existing view must not replace its document, form or raw XML session.
    private PowerEditorCatalogs CreatePowerSelectorCatalogs() => new(
        iconPaths: _baseGameIconPaths.Concat(_customIconPaths),
        protoUnitNames: GetTechnologyProtoUnitNames(),
        unitTypeNames: GetKnownUnitTypeNames(),
        terrainCreepNames: GetKnownTerrainCreepNames(),
        protoActionNames: GetTechnologyProtoActionNames(),
        technologyNames: GetAvailableTechNames(),
        animationNames: GetGlobalProtoActionAnimationNames(),
        boneNames: GetAvailableProtoActionModelAttachmentBones(),
        tacticNames: GetTechnologyTacticDefinitionNames());

    private void RefreshPowerSelectorCatalogs()
    {
        if (_powerView != null) _powerView.UpdateReferenceCatalogs(CreatePowerSelectorCatalogs());
    }

    private void RefreshMajorGodSelectorCatalogs()
    {
        if (_majorGodView != null) _majorGodView.UpdateReferenceCatalogs(
            GetTechnologyProtoUnitNames(), GetKnownUnitTypeNames(), GetMajorGodTechnologyNames(),
            GetMajorGodTechnologyFlagNames(), GetTechnologyTechTypeNames());
    }

    private void RefreshTechnologySelectorCatalogs()
    {
        if (_technologyView != null) _technologyView.UpdateReferenceCatalogs(
            GetTechnologyProtoUnitNames(), GetTechnologyPrerequisiteUnitNames(), GetKnownCultureNames(),
            GetTechnologyPrerequisiteMajorGodNames(), GetTechnologyTechTypeNames(),
            GetTechnologyProtoActionNames(), GetTechnologyTacticDefinitionNames(),
            GetAvailableCommandNames(), GetTechnologyGodPowerNames(), GetKnownUnitTypeNames());
    }

    private void RefreshRetainedSelectorCatalogs()
    {
        RefreshPowerSelectorCatalogs();
        RefreshMajorGodSelectorCatalogs();
        RefreshTechnologySelectorCatalogs();
    }

    private void ReloadIconCatalogsAfterManager()
    {
        LoadCurrentModAssetCatalogs();
        var icons = _baseGameIconPaths.Concat(_customIconPaths).ToArray();
        _technologyView?.UpdateIconCatalog(icons);
        _powerView?.UpdateIconCatalog(icons);
        _majorGodView?.UpdateIconCatalog(icons);
    }

    private void RefreshEditorsAfterTypeManager(bool unitTypes)
    {
        if (unitTypes)
        {
            _cachedUnitTypeDefinitions = null;
            _cachedBuildLimitTargets = null;
        }
        else _cachedTechTypeDefinitions = null;
        RefreshTechnologyViewAfterTechTypeChanges();
        RefreshRetainedSelectorCatalogs();
        if (unitTypes && !string.IsNullOrWhiteSpace(_currentUnitName))
        {
            var tabIndex = _editorTabs.SelectedIndex;
            BuildEditorPanel(_currentUnitName, resetScroll: false);
            if (tabIndex >= 0) _editorTabs.SelectedIndex = tabIndex;
        }
    }

    private void RefreshEditorsAfterCommandManager()
    {
        EnsureProtoUnitCommandCatalogLoaded(force: true);
        _cachedCommandNames = null;
        RefreshTechnologySelectorCatalogs();
        if (!string.IsNullOrWhiteSpace(_currentUnitName)) BuildEditorPanel(_currentUnitName, resetScroll: false);
    }

    private void RefreshEditorsAfterTacticsManager()
    {
        _cachedTacticsNames = null;
        RefreshRetainedSelectorCatalogs();
        if (!string.IsNullOrWhiteSpace(_currentUnitName)) BuildEditorPanel(_currentUnitName, resetScroll: false);
    }
}
