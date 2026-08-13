using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class AnimFileCatalogTests
{
    [Fact]
    public void FilterArchiveEntries_ExposesOnlyAnimationXmlPaths()
    {
        var result = AnimFileCatalog.FilterArchiveEntries("ArtGreek.bar",
        [
            "greek/units/infantry/hoplite/hoplite.xml.XMB",
            "greek/units/infantry/hoplite/hoplite.material.XMB",
            "greek/units/infantry/hoplite/readme.txt",
            "greek/units/infantry/hoplite/alternate.xml"
        ]);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, entry => entry.Path == "greek\\units\\infantry\\hoplite\\hoplite.xml");
        Assert.Contains(result, entry => entry.Path == "greek\\units\\infantry\\hoplite\\alternate.xml");
        Assert.All(result, entry => Assert.Equal("ArtGreek.bar", entry.ArchiveName));
    }

    [Fact]
    public void FilterArchiveEntries_NormalizesAndDeduplicatesCaseInsensitively()
    {
        var result = AnimFileCatalog.FilterArchiveEntries("ArtGreek.bar",
        [
            "greek/units/hoplite.xml.XMB",
            "greek\\units\\hoplite.xml.XMB",
            "GREEK/UNITS/HOPLITE.XML.xmb"
        ]);

        Assert.Single(result);
        Assert.Equal("greek\\units\\hoplite.xml", result[0].Path);
    }

    [Theory]
    [InlineData("greek/units/hoplite.xml.XMB", true, "greek\\units\\hoplite.xml")]
    [InlineData("greek/units/hoplite.XML", true, "greek\\units\\hoplite.XML")]
    [InlineData("greek/units/hoplite.material.XMB", false, "")]
    [InlineData("greek/units/hoplite.dds", false, "")]
    public void TryGetGamePath_AcceptsOnlyXmlPayloads(string archivePath, bool expected, string expectedPath)
    {
        Assert.Equal(expected, AnimFileCatalog.TryGetGamePath(archivePath, out var gamePath));
        Assert.Equal(expectedPath, gamePath);
    }

    [Theory]
    [InlineData("ArtExamplesForModders.bar", false)]
    [InlineData("ArtUI.bar", false)]
    [InlineData("artui.BAR", false)]
    [InlineData("ArtGreek.bar", true)]
    public void ShouldScanArchive_ExcludesOnlyConfiguredArchives(string archiveName, bool expected)
    {
        Assert.Equal(expected, AnimFileCatalog.ShouldScanArchive(archiveName));
    }
}
