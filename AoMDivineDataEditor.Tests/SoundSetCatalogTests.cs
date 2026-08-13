using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class SoundSetCatalogTests
{
    [Fact]
    public void FilterArchiveEntries_KeepsXmlSoundSetsAndExcludesRootMetadataFiles()
    {
        var result = SoundSetCatalog.FilterArchiveEntries(
        [
            "ambient_sounds.xml.XMB",
            "playlist.xml.XMB",
            "soundmanifest.xml.XMB",
            "soundsets_greek.soundset.XMB",
            "greek/vo/hoplite/hoplite.xml.XMB",
            "shared/sfx/buildings/house.XML.XMB",
            "shared/audio.wav"
        ]);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, item => item.Path == "greek\\vo\\hoplite\\hoplite.xml");
        Assert.Contains(result, item => item.Path == "shared\\sfx\\buildings\\house.XML");
        Assert.All(result, item => Assert.Equal("Sound.bar", item.ArchiveName));
    }

    [Fact]
    public void FilterArchiveEntries_DeduplicatesPathsCaseInsensitively()
    {
        var result = SoundSetCatalog.FilterArchiveEntries(
        [
            "greek/vo/hoplite/hoplite.xml.XMB",
            "GREEK\\VO\\HOPLITE\\HOPLITE.XML.xmb"
        ]);

        Assert.Single(result);
    }
}
