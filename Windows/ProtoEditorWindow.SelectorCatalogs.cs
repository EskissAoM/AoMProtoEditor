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
            GetAvailableCommandNames(), GetTechnologyGodPowerNames());
    }

    private void RefreshRetainedSelectorCatalogs()
    {
        RefreshPowerSelectorCatalogs();
        RefreshMajorGodSelectorCatalogs();
        RefreshTechnologySelectorCatalogs();
    }
}
