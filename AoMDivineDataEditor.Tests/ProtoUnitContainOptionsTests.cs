using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class ProtoUnitContainOptionsTests
{
    [Fact]
    public void SetContainList_WritesExternalOneOnEveryContainWhenEnabled()
    {
        var unit = XElement.Parse("<unit><contain>Old</contain></unit>");

        ProtoXmlHandler.SetContainList(unit, ["LogicalTypePickable", "AbstractArcher"], external: true);

        var containElements = unit.Elements("contain").ToList();
        Assert.Equal(2, containElements.Count);
        Assert.All(containElements, element => Assert.Equal("1", (string?)element.Attribute("external")));
    }

    [Fact]
    public void SetContainList_OmitsExternalAttributeWhenDisabled()
    {
        var unit = XElement.Parse("<unit><contain external=\"1\">Old</contain></unit>");

        ProtoXmlHandler.SetContainList(unit, ["LogicalTypePickable"]);

        var contain = Assert.Single(unit.Elements("contain"));
        Assert.Null(contain.Attribute("external"));
    }

    [Fact]
    public void StatsEditor_OffersBothSharedRechargeModulesAndSynchronizesEjectCommand()
    {
        var source = File.ReadAllText(Path.Combine(FindProjectRoot(), "Windows", "ProtoEditorWindow.axaml.cs"));

        Assert.Contains("\"recharge\",\n        \"auxrecharge\"", source.Replace("\r\n", "\n"), StringComparison.Ordinal);
        Assert.Contains("Content = \"External\"", source, StringComparison.Ordinal);
        Assert.Contains("Content = \"Eject command\"", source, StringComparison.Ordinal);
        Assert.Contains("Value = \"Eject\"", source, StringComparison.Ordinal);
        Assert.Contains("Row = \"3\"", source, StringComparison.Ordinal);
        Assert.Contains("Column = \"1\"", source, StringComparison.Ordinal);
        Assert.Contains("_removeUnitCommandRows?.Invoke(\"Eject\")", source, StringComparison.Ordinal);
    }

    [Fact]
    public void StatsEditor_RefreshesContainChipsAndUsesSharedRechargeControls()
    {
        var source = File.ReadAllText(Path.Combine(FindProjectRoot(), "Windows", "ProtoEditorWindow.axaml.cs"));
        var normalized = source.Replace("\r\n", "\n");
        var containStart = normalized.IndexOf("StackPanel CreateContainPicker", StringComparison.Ordinal);
        var containEnd = normalized.IndexOf("var containPickersRow", containStart, StringComparison.Ordinal);
        var containEditor = normalized[containStart..containEnd];

        Assert.True(containEditor.Split("CommitChipAdditionAndRefreshXmlPreview();").Length - 1 >= 2);
        Assert.Contains("Content = $\"Add {itemLabel}\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Content = $\"+ Add {itemLabel}\"", source, StringComparison.Ordinal);
        Assert.Contains("GetRechargeModeDisplayValue", source, StringComparison.Ordinal);
        Assert.Contains("? \"ResourceDropoff\"", source, StringComparison.Ordinal);
        Assert.Contains("RegisterStatsRechargeEditors(key, modeCb, valueTb, startChargedCheckBox);", source, StringComparison.Ordinal);
        Assert.Contains("SynchronizeSharedRechargeEditors(prefix, modeCb, valueTb, startChargedCb);", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RechargeTypeFilters_AreCompactAndUsedModulesCannotBeRemoved()
    {
        var source = File.ReadAllText(Path.Combine(FindProjectRoot(), "Windows", "ProtoEditorWindow.axaml.cs"))
            .Replace("\r\n", "\n");
        var mainStart = source.IndexOf("void AddRechargeEditor()", StringComparison.Ordinal);
        var mainEnd = source.IndexOf("void AddAuxRechargeEditor()", mainStart, StringComparison.Ordinal);
        var auxEnd = source.IndexOf("void AddMinimapColorEditor()", mainEnd, StringComparison.Ordinal);
        var main = source[mainStart..mainEnd];
        var aux = source[mainEnd..auxEnd];

        foreach (var editor in new[] { main, aux })
        {
            Assert.Contains("selector.Width = 150;", editor, StringComparison.Ordinal);
            Assert.Contains("selectorRow.Children.Add(selector);", editor, StringComparison.Ordinal);
            Assert.Contains("Margin = new Thickness(150, 0, 0, 0)", editor, StringComparison.Ordinal);
            Assert.DoesNotContain("var addButton = new Button { Content = \"Add\"", editor, StringComparison.Ordinal);
            Assert.Contains("if (IsRechargeUsedByAnyAbility(key))", editor, StringComparison.Ordinal);
        }

        Assert.Contains("RefreshStatsRechargeRemoveAvailability();", source, StringComparison.Ordinal);
        Assert.Contains("used by an ability and cannot be removed", source, StringComparison.Ordinal);
    }

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AoMDivineDataEditor.csproj")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Project root not found.");
    }
}
