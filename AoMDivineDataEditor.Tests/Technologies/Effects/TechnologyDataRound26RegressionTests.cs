using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Windows;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class TechnologyDataRound26RegressionTests
{
    private static readonly string[] DeprecatedEffectNames =
    [
        "ShowWorldView", "AddTrain", "Armor", "ArmorSpecific", "BlockTrainCount",
        "FreeBuildPoints", "FreeBuildRate", "InvestmentAmount", "InvestmentCap",
        "InvestmentEnable", "Scale", "SendRandomCard", "SetProtoMaxArmor",
        "TradeRouteBonus", "TradeRouteBonusTeam", "UpgradeLevel"
    ];

    [Fact]
    public void CreatePowerUsesTheSharedGodPowerCatalog()
    {
        var method = ExtractMethod(ReadTechnologyEditor(), "AddCreatePowerEffectEditor");

        Assert.Contains("CreateStrictEffectSelector(", method, StringComparison.Ordinal);
        Assert.Contains("_godPowerNames", method, StringComparison.Ordinal);
        Assert.Contains("GetCaseInsensitiveAttribute(effect, \"protoPower\")", method, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyNewEffectDoesNotShowRawXmlEditor()
    {
        var method = ExtractMethod(ReadTechnologyEditor(), "AddEffectEditorAsync", returnType: "Task", isAsync: true);

        Assert.Contains("ShouldShowRawEffectXmlEditor(effect, currentType, structured)", method, StringComparison.Ordinal);
    }

    [Fact]
    public void DataEffectDoesNotShowRawXmlUntilSubtypeIsChosen()
    {
        Assert.False(TechnologyEditorView.ShouldShowRawEffectXmlEditor(
            XElement.Parse("<effect type='Data' />"),
            "Data",
            structured: false));
        Assert.True(TechnologyEditorView.ShouldShowRawEffectXmlEditor(
            XElement.Parse("<effect type='Data' subtype='CustomImportedSubtype' />"),
            "Data",
            structured: false));
    }

    [Fact]
    public void DeprecatedEffectsAreAbsentFromTechnologyAutocompleteCatalogs()
    {
        foreach (var name in DeprecatedEffectNames)
        {
            Assert.DoesNotContain(name, TechnologyEffectCatalogInventory.CreatableEffectTypes,
                StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain(name, TechnologyEffectCatalogInventory.CreatableDataSubtypes,
                StringComparer.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void ImportOnlyAndUnimplementedDataSubtypesAreNotOfferedForCreation()
    {
        Assert.DoesNotContain("Flag", TechnologyEffectCatalogInventory.CreatableDataSubtypes);
        Assert.DoesNotContain("ActionAdd", TechnologyEffectCatalogInventory.CreatableDataSubtypes);
        Assert.DoesNotContain("SquareAura", TechnologyEffectCatalogInventory.CreatableDataSubtypes);
        Assert.Contains("ActionEnable", TechnologyEffectCatalogInventory.CreatableDataSubtypes);
    }

    [Fact]
    public void ActionEnableUsesTheStructuredUnitActionEnableDisableEditor()
    {
        var code = ReadTechnologyEditor();
        var family = ExtractField(code, "EnableDisableActionUnitDataSubtypes", "MovementTypeDataSubtypes");
        var dispatcher = ExtractMethod(code, "AddStructuredEffectBodyAsync", returnType: "Task<bool>", isAsync: true);
        var editor = ExtractMethod(code, "AddEnableDisableUnitDataEffectEditor");

        Assert.Contains("\"ActionEnable\"", family, StringComparison.Ordinal);
        Assert.Contains("EnableDisableActionUnitDataSubtypes.Contains(subtype)", dispatcher, StringComparison.Ordinal);
        Assert.Contains("allowAllActions: !subtype.Equals(\"ActionEnable\", StringComparison.OrdinalIgnoreCase)", dispatcher, StringComparison.Ordinal);
        Assert.Contains("AddDataTargetEditor(effect, row)", editor, StringComparison.Ordinal);
        Assert.Contains("if (includeAction && allowAllActions) EnsureDefaultDataAction(effect)", editor, StringComparison.Ordinal);
        Assert.Contains("CreateDataActionSelector(effect, allowAllActions)", editor, StringComparison.Ordinal);
        Assert.Contains("EnsureDefaultEnableDisableDataState(effect)", editor, StringComparison.Ordinal);
        Assert.Contains("selected == \"Disable\" ? \"0\" : \"1\"", editor, StringComparison.Ordinal);
        Assert.Contains("SetCaseInsensitiveAttribute(effect, \"relativity\", \"Assign\")", editor, StringComparison.Ordinal);

        var actionSelector = ExtractMethod(code, "CreateDataActionSelector", returnType: "AutoCompleteBox");
        Assert.Contains("allowAllActions ? new[] { \"All\" } : []", actionSelector, StringComparison.Ordinal);
        Assert.Contains("allowAllActions && value.Equals(\"All\"", actionSelector, StringComparison.Ordinal);
    }

    private static string ExtractMethod(string source, string methodName, string returnType = "void", bool isAsync = false)
    {
        var signature = $"private {(isAsync ? "async " : "")}{returnType} {methodName}(";
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find {methodName}.");
        var next = source.IndexOf("\n    private ", start + signature.Length, StringComparison.Ordinal);
        return next < 0 ? source[start..] : source[start..next];
    }

    private static string ExtractField(string source, string startName, string nextName)
    {
        var start = source.IndexOf(startName, StringComparison.Ordinal);
        var end = source.IndexOf(nextName, start + startName.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        return source[start..end];
    }

    private static string ReadTechnologyEditor()
        => File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Windows", "TechnologyEditorView.axaml.cs"));

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
