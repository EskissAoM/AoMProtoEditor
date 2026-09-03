using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class TechnologyResourceDataEffectTests
{
    [Fact]
    public void NormalizeResourceEffect_ForcesPlayerAndPreservesPercent()
    {
        var effect = XElement.Parse("""
            <effect type="Data" subtype="Resource" relativity="Percent" resource="Gold">
              <target type="ProtoUnit" ignoreNature="">Villager</target>
              <target type="UnitType">MilitaryUnit</target>
            </effect>
            """);

        var changed = TechnologyDataEffectRules.NormalizeResourceEffect(effect);

        Assert.True(changed);
        Assert.Equal("Percent", (string?)effect.Attribute("relativity"));
        Assert.Equal("0", (string?)effect.Attribute("amount"));
        Assert.Equal("Gold", (string?)effect.Attribute("resource"));
        var target = Assert.Single(effect.Elements("target"));
        Assert.Equal("Player", (string?)target.Attribute("type"));
        Assert.Single(target.Attributes());
        Assert.True(target.IsEmpty);
    }

    [Fact]
    public void NormalizeResourceEffect_PreservesAssignAndExistingAmount()
    {
        var effect = XElement.Parse("""
            <effect type="Data" subtype="Resource" relativity="Assign" amount="-25" resource="Favor">
              <target type="Player" />
            </effect>
            """);

        var changed = TechnologyDataEffectRules.NormalizeResourceEffect(effect);

        Assert.False(changed);
        Assert.Equal("Assign", (string?)effect.Attribute("relativity"));
        Assert.Equal("-25", (string?)effect.Attribute("amount"));
    }

    [Fact]
    public void NormalizeResourceEffect_PreservesBasePercent()
    {
        var effect = XElement.Parse("""
            <effect type="Data" subtype="Resource" relativity="BasePercent" amount="1.5" resource="Food">
              <target type="Player" />
            </effect>
            """);

        var changed = TechnologyDataEffectRules.NormalizeResourceEffect(effect);

        Assert.False(changed);
        Assert.Equal("BasePercent", (string?)effect.Attribute("relativity"));
    }

    [Fact]
    public void ResourceEditor_ExposesPlayerAndAllStandardRelativities()
    {
        Assert.Equal(["Player"], TechnologyDataEffectRules.ResourceTargetOptions);
        Assert.Equal(["Add", "Multiply", "Multiply base", "Set to"], TechnologyDataEffectRules.ResourceRelativityDisplayOptions);

        var root = FindProjectRoot();
        var code = File.ReadAllText(Path.Combine(root, "Windows", "TechnologyEditorView.axaml.cs"));
        Assert.Contains("AddPlayerResourceDataEffectEditor", code, StringComparison.Ordinal);
        Assert.Contains("CreateSignedFloatEffectBox(effect, \"amount\"", code, StringComparison.Ordinal);
        Assert.Contains("CreateResourceCombo(effect, \"resource\")", code, StringComparison.Ordinal);
    }

    private static string FindProjectRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AoMDivineDataEditor.csproj"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate AoMDivineDataEditor.csproj.");
    }
}
