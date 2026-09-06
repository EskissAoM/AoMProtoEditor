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

        Assert.Contains("if (!structured && !string.IsNullOrWhiteSpace(currentType))", method, StringComparison.Ordinal);
    }

    [Fact]
    public void DeprecatedEffectsAreAbsentFromTechnologyAutocompleteCatalogs()
    {
        var code = ReadTechnologyEditor();
        var effectTypes = ExtractField(code, "TechnologyEffectTypes", "StructuredTechnologyEffectTypes");
        var dataSubtypes = ExtractField(code, "TechnologyDataEffectSubtypes", "KbStatNames");

        foreach (var name in DeprecatedEffectNames)
        {
            Assert.DoesNotContain($"\"{name}\"", effectTypes, StringComparison.Ordinal);
            Assert.DoesNotContain($"\"{name}\"", dataSubtypes, StringComparison.Ordinal);
        }
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
