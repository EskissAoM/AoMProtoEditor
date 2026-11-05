using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class ReferenceCatalogSafetyTests
{
    [Theory]
    [InlineData("<abstractunittypesmods/>", true)]
    [InlineData("<abstractunittypesmods><abstractunittype>Other</abstractunittype></abstractunittypesmods>", true)]
    [InlineData("<broken", false)]
    [InlineData("<abstractunittypes/>", false)]
    [InlineData("<abstractunittypesmods xmlns='urn:extension'/>", false)]
    [InlineData("<abstractunittypesmods><abstractunittype/></abstractunittypesmods>", false)]
    public void ModReadinessControlsMixedRenameWithoutWrites(string xml, bool complete)
    {
        using var workspace = new TestWorkspace("catalog-mod-readiness");
        var catalog = workspace.WriteText("abstract_unit_types_mods.xml", xml);
        var path = workspace.WriteXml("powers_mods.xml", "<powersmod><power name='P'><effect type='Damage'><target type='ProtoUnit'>Old</target></effect></power></powersmod>");
        var before = File.ReadAllBytes(path);
        var definitions = UnitTypeCatalog.ReadForMigration(catalog, isBuiltIn: false);
        Assert.Equal(complete, definitions != null);
        var files = ModReferenceFileCatalog.Discover(workspace.RootPath).Where(file => file.Path == path);
        GameIdentifierReferenceMigrationPlan Prepare() => GameIdentifierReferenceMigrationPlan.Prepare(files,
            GameIdentifierKind.ProtoUnit, [new("Old", "New")], oppositeUnitKindNames: definitions?.Select(item => item.Name));
        if (complete) Assert.Single(Prepare().Paths);
        else Assert.Throws<InvalidOperationException>(Prepare);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void MissingOptionalModIsDistinctFromMissingBaseAndUnreadableFile()
    {
        using var workspace = new TestWorkspace("catalog-missing");
        var missing = workspace.GetPath("missing.xml");
        Assert.Empty(UnitTypeCatalog.ReadForMigration(missing, isBuiltIn: false)!);
        Assert.Null(UnitTypeCatalog.ReadForMigration(missing, isBuiltIn: true));
        Assert.Null(UnitTypeCatalog.ReadForMigration(null, isBuiltIn: false));
        var path = workspace.WriteXml("locked.xml", "<abstractunittypesmods/>");
        using var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.Null(UnitTypeCatalog.ReadForMigration(path, isBuiltIn: false));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<broken")]
    [InlineData("<proto xmlns='urn:extension'><unit name='Hidden'/></proto>")]
    [InlineData("<proto><unit/></proto>")]
    public void OneFailedProtoPartInvalidatesOtherwiseUsableNames(string? badPart)
    {
        Assert.Null(ReferenceCatalogSafety.ProtoUnitNames(["<proto><unit name='Good'/></proto>", badPart], new XElement("protomods")));
    }

    [Fact]
    public void CompleteProtoCatalogIncludesAllPartsAndModDefinitions()
    {
        var names = ReferenceCatalogSafety.ProtoUnitNames(
            ["<proto><unit name='Base'/></proto>", "<proto><unit name='Dlc'/><unit name='base'/></proto>"],
            XElement.Parse("<protomods><unit name='Custom'/></protomods>"));
        Assert.NotNull(names);
        Assert.Equal(["Base", "Dlc", "Custom"], names);
        var document = XDocument.Parse("<techtreemods><tech name='T'><effect><target type='ProtoUnit'>Old</target></effect></tech></techtreemods>");
        GameIdentifierReferenceMigrationPlan.ValidateUnitReferenceIdentity(document, GameIdentifierKind.UnitType, "Old", "New", names, "local");
        Assert.Throws<InvalidOperationException>(() => GameIdentifierReferenceMigrationPlan.ValidateUnitReferenceIdentity(
            document, GameIdentifierKind.UnitType, "Old", "Dlc", names, "local"));
        Assert.Throws<InvalidOperationException>(() => GameIdentifierReferenceMigrationPlan.ValidateUnitReferenceIdentity(
            document, GameIdentifierKind.UnitType, "Old", "New", null, "local"));
        Assert.Equal("Old", document.Descendants("target").Single().Value);
    }

    [Fact]
    public void MissingProtoSourcesCannotMasqueradeAsAnEmptyCatalog()
    {
        Assert.Null(ReferenceCatalogSafety.ProtoUnitNames([], new XElement("protomods")));
        Assert.Null(ReferenceCatalogSafety.ProtoUnitNames(["<proto/>"], new XElement("protomods")));
        Assert.Null(ReferenceCatalogSafety.ProtoUnitNames(["<proto><unit name='U'/></proto>"], null));
    }

    [Fact]
    public void ImplicitTypesRequireEveryProtoPartToLoad()
    {
        var valid = "<proto><unit name='U'><unittype>Implicit</unittype></unit></proto>";
        Assert.Equal(["Implicit"], ReferenceCatalogSafety.UsedUnitTypeNames([valid]));
        Assert.Null(ReferenceCatalogSafety.UsedUnitTypeNames([valid, null]));
        Assert.Null(ReferenceCatalogSafety.UsedUnitTypeNames([valid, "<broken"]));
        Assert.Null(ReferenceCatalogSafety.UsedUnitTypeNames([]));
        Assert.Empty(ReferenceCatalogSafety.UsedUnitTypeNames(["<proto><unit name='U'/></proto>"])!);
    }
}
