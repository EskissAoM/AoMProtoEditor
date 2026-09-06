using AoMDivineDataEditor.Windows;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class TechnologyDataRound23RegressionTests
{
    [Theory]
    [InlineData("BuffIconOverride", "STR_BUFFICON_MY_TECH_OVERRIDE")]
    [InlineData("PowerIconOverride", "STR_POWERICON_MY_TECH_OVERRIDE")]
    public void IconOverrideIdsUseRequestedNamespaces(string subtype, string expected)
        => Assert.Equal(expected, TechnologyEditorView.BuildIconOverrideStringBase("MyTech", subtype));

    [Fact]
    public void DuplicateIconOverrideIdsStartAtOne()
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "STR_BUFFICON_MY_TECH_OVERRIDE",
            "STR_BUFFICON_MY_TECH_OVERRIDE1"
        };

        Assert.Equal(
            "STR_BUFFICON_MY_TECH_OVERRIDE2",
            TechnologyEditorView.BuildNextIconOverrideStringId("MyTech", "BuffIconOverride", used));
    }

    [Theory]
    [InlineData("STR_BUFFICON_MY_TECH_OVERRIDE")]
    [InlineData("STR_BUFFICON_MY_TECH_OVERRIDE3")]
    [InlineData("STR_POWERICON_MY_TECH_OVERRIDE1")]
    public void GeneratedIconOverrideIdsAreOwned(string id)
        => Assert.True(TechnologyEditorView.IsOwnedIconOverrideStringId(id));

    [Fact]
    public void UnrelatedImportedIconStringIsNotOwned()
        => Assert.False(TechnologyEditorView.IsOwnedIconOverrideStringId("STR_CIV_SHENNONG_GIFT_ICON_AGE_HEROIC"));

    [Fact]
    public void IconOverrideEditorsUseIconAndPowerCatalogs()
    {
        var code = ReadTechnologyEditor();

        Assert.Contains("AddIconOverrideDataEffectEditorAsync", code, StringComparison.Ordinal);
        Assert.Contains("ProtoEditorWindow.NormalizeIconCatalogValue(iconPath, _iconPaths)", code, StringComparison.Ordinal);
        Assert.Contains("iconEditor.Configure(", code, StringComparison.Ordinal);
        Assert.Contains("GetCaseInsensitiveAttribute(effect, \"protopower\")", code, StringComparison.Ordinal);
        Assert.Contains("SetCaseInsensitiveAttribute(effect, \"pathstrid\", stringId)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void IconOverrideStringsParticipateInEveryLifecyclePath()
    {
        var code = ReadTechnologyEditor();

        Assert.Contains("QueueDataIconOverrideStringForRemoval(effect);", code, StringComparison.Ordinal);
        Assert.Contains("BuildTechnologyOwnedStringPrefixes", code, StringComparison.Ordinal);
        Assert.Contains("BuildNextIconOverrideStringId(technologyName, subtype, used)", code, StringComparison.Ordinal);
        Assert.Contains("_pendingStringUpdates[newId] = text", code, StringComparison.Ordinal);
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
