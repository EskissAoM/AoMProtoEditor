using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class LocalModsPathResolverTests
{
    [Fact]
    public void Resolve_AcceptsSteamUserDirectory()
    {
        var userDirectory = Path.Combine(Path.GetTempPath(), "76561198000000000");

        Assert.Equal(
            Path.Combine(Path.GetFullPath(userDirectory), "mods", "local"),
            LocalModsPathResolver.Resolve(userDirectory));
    }

    [Fact]
    public void Resolve_AcceptsModsDirectory()
    {
        var modsDirectory = Path.Combine(Path.GetTempPath(), "76561198000000000", "mods");

        Assert.Equal(
            Path.Combine(Path.GetFullPath(modsDirectory), "local"),
            LocalModsPathResolver.Resolve(modsDirectory));
    }

    [Fact]
    public void Resolve_DoesNotAppendToLocalModsDirectoryAgain()
    {
        var localDirectory = Path.Combine(Path.GetTempPath(), "76561198000000000", "mods", "local");

        Assert.Equal(Path.GetFullPath(localDirectory), LocalModsPathResolver.Resolve(localDirectory));
    }

    [Fact]
    public void Resolve_IsCaseInsensitiveAndIgnoresTrailingSeparators()
    {
        var localDirectory = Path.Combine(Path.GetTempPath(), "76561198000000000", "MODS", "LOCAL");
        var configured = localDirectory + Path.DirectorySeparatorChar;

        Assert.Equal(Path.GetFullPath(localDirectory), LocalModsPathResolver.Resolve(configured));
    }
}
