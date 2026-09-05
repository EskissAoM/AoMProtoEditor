using System.Xml.Linq;
using AoMDivineDataEditor.Windows;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class TechnologyDataRound19RegressionTests
{
    [Fact]
    public void GodPowerCatalogUsesAllOriginalPowersAndFiltersModPowersByType()
    {
        var originalDocuments = new[]
        {
            XDocument.Parse("""
                <powers>
                  <power name="AOTGMeteor" type="Meteor" />
                  <power name="OriginalUnitAction" type="UnitAction" />
                </powers>
                """)
        };
        var modDocuments = new[]
        {
            XDocument.Parse("""
                <powersmod>
                  <power name="CustomGodPower" type="Custom" />
                  <power name="ExcludedUnitAction" type="UnitAction" godpower="" />
                  <power name="IncludedGeneralEffect" type="GeneralEffect" godpower=" " />
                  <power name="ExcludedGeneralEffect" type="GeneralEffect" />
                  <power name="aotgmeteor" type="Override" />
                </powersmod>
                """)
        };

        Assert.Equal(
            ["AOTGMeteor", "CustomGodPower", "IncludedGeneralEffect", "OriginalUnitAction"],
            ProtoEditorWindow.ExtractTechnologyGodPowerNames(originalDocuments, modDocuments));
    }

    [Fact]
    public void PowerRofAndMaxUsesUseSharedForcedPlayerGodPowerLayout()
    {
        var root = FindRepositoryRoot();
        var editorCode = File.ReadAllText(Path.Combine(root, "Windows", "TechnologyEditorView.axaml.cs"));
        var windowCode = File.ReadAllText(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml.cs"));

        Assert.Contains("subtype.Equals(\"PowerROF\"", editorCode, StringComparison.Ordinal);
        Assert.Contains("subtype.Equals(\"PowerMaxUses\"", editorCode, StringComparison.Ordinal);
        Assert.Contains("AddPlayerPowerAmountDataEffectEditor", editorCode, StringComparison.Ordinal);
        Assert.Contains("CreateStrictEffectSelector(\n            _godPowerNames", NormalizeNewlines(editorCode), StringComparison.Ordinal);
        Assert.Contains("GetCaseInsensitiveAttribute(effect, \"protopower\")", editorCode, StringComparison.Ordinal);
        Assert.Contains("MigrateDataAttribute(effect, \"power\", \"protopower\")", editorCode, StringComparison.Ordinal);
        Assert.Contains("GetTechnologyGodPowerNames()", windowCode, StringComparison.Ordinal);
        Assert.Contains("god_powers", windowCode, StringComparison.Ordinal);
        Assert.Contains(".godpowers.xmb", windowCode, StringComparison.Ordinal);
        Assert.Contains("GetCurrentModGameplayFilePath(\"powers_mods.xml\")", windowCode, StringComparison.Ordinal);
    }

    private static string NormalizeNewlines(string value) => value.Replace("\r\n", "\n");

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AoMDivineDataEditor.csproj")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the AoMDivineDataEditor repository root.");
    }
}
