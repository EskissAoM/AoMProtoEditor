using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Windows;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class AnimFileAnimationCatalogTests
{
    [Fact]
    public void AnimFileCatalog_ResolvesAnUnambiguousShortStockReference()
    {
        AnimFileCatalogEntry[] entries =
        [
            new("greek\\units\\cavalry\\contarius\\contarius.xml", "ArtGreek.bar"),
            new("greek\\units\\infantry\\hoplite\\hoplite.xml", "ArtGreek.bar")
        ];

        var result = AnimFileCatalog.ResolveReference(entries, "contarius.xml");

        Assert.NotNull(result);
        Assert.Equal(entries[0], result);
    }

    [Fact]
    public void AnimFileCatalog_DoesNotGuessAnAmbiguousShortReference()
    {
        AnimFileCatalogEntry[] entries =
        [
            new("greek\\units\\shared\\unit.xml", "ArtGreek.bar"),
            new("egyptian\\units\\shared\\unit.xml", "ArtEgyptian.bar")
        ];

        Assert.Null(AnimFileCatalog.ResolveReference(entries, "unit.xml"));
    }

    [Fact]
    public void AnimFileCatalog_DeduplicatesSubmodelsAndExcludesAttachmentAnimationsFromUnitView()
    {
        const string xml = """
            <animfile>
              <submodel>
                <attachment>
                  <anim>AttachmentOnly<component>Vfx</component></anim>
                  <anim>Idle</anim>
                </attachment>
                <anim>Idle<assetreference><file>male_idle</file></assetreference></anim>
                <anim>GatherChop<assetreference><file>male_gather</file></assetreference></anim>
              </submodel>
              <submodel>
                <anim>idle<assetreference><file>female_idle</file></assetreference></anim>
                <anim>GatherChop<assetreference><file>female_gather</file></assetreference></anim>
                <anim>GatherFarm</anim>
              </submodel>
            </animfile>
            """;

        var result = AnimFileAnimationCatalog.ParseAnimFileXml(xml);

        Assert.Equal(["GatherChop", "GatherFarm", "Idle"], result.UnitAnimations);
        Assert.DoesNotContain("AttachmentOnly", result.UnitAnimations);
        Assert.DoesNotContain(result.UnitAnimations, value => value.Contains("male_idle", StringComparison.Ordinal));
    }

    [Fact]
    public void SimDataCatalog_IndexesOnlyRequestedAnimfilesAndDeduplicatesNames()
    {
        const string xml = """
            <simdatabase>
              <animxml file="greek/units/test.xml">
                <animations>
                  <animinfo><name>Idle</name></animinfo>
                  <animinfo><name>idle</name></animinfo>
                  <animinfo><name>Attack</name></animinfo>
                </animations>
              </animxml>
              <animxml file="other.xml">
                <animations><animinfo><name>Other</name></animinfo></animations>
              </animxml>
            </simdatabase>
            """;

        var result = AnimFileAnimationCatalog.ParseSimDataXml(xml, ["greek\\units\\test.xml"]);

        var catalog = Assert.Single(result).Value;
        Assert.Equal(["Attack", "Idle"], catalog.UnitAnimations);
    }

    [Fact]
    public void ProtoActionEditor_UsesGlobalListForAnimOverrideAndBlocksInvalidUnitAnimations()
    {
        var source = File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "Windows", "ProtoEditorWindow.axaml.cs")));

        Assert.Contains("GetAvailableProtoActionAnimationNames(useGlobalCatalog: true)", source, StringComparison.Ordinal);
        Assert.Contains("if (IsProtoActionAnimationValueTag(tag))", source, StringComparison.Ordinal);
        Assert.Contains("!await EnsureValidProtoActionAnimationsBeforeSaveAsync()", source, StringComparison.Ordinal);
        Assert.Contains("RefreshProtoActionAnimationValidationVisuals();", source, StringComparison.Ordinal);
        Assert.Contains("ReplaceObservableValues(_currentUnitAnimationSuggestions, []);", source, StringComparison.Ordinal);
        Assert.Contains(
            "editor.FullValueChanged += (_, _) => RefreshCurrentUnitAnimationCatalogFromEditors();",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "InitializeCurrentUnitAnimationCatalog(normalizedPaths, _editorBuildGeneration);",
            source,
            StringComparison.Ordinal);

        var refreshIndex = source.IndexOf("RefreshCurrentUnitProtoActionMetadata(unit);", StringComparison.Ordinal);
        var initializeIndex = source.IndexOf(
            "InitializeCurrentUnitAnimationCatalog(unit, buildGeneration);",
            StringComparison.Ordinal);
        Assert.True(refreshIndex >= 0 && initializeIndex > refreshIndex);
    }

    [Fact]
    public void GlobalAnimationCatalog_IncludesTacticSpecialAnimationsAndActionAnimations()
    {
        var document = System.Xml.Linq.XDocument.Parse("""
            <tactics>
              <walkanim>LandWalk</walkanim>
              <idleanim>LandIdle</idleanim>
              <action>
                <name>Attack</name>
                <anim>LandAttack</anim>
              </action>
            </tactics>
            """);

        var result = ProtoEditorWindow.ExtractGlobalAnimationNames(document);

        Assert.Equal(["LandAttack", "LandIdle", "LandWalk"], result);
    }
}
