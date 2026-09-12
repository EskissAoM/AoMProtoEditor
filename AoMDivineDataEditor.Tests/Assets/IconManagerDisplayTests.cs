using AoMDivineDataEditor.Windows;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class IconManagerDisplayTests
{
    [Theory]
    [InlineData("resources\\greek\\units\\hoplite_icon.png", "greek\\units\\hoplite_icon.png")]
    [InlineData("RESOURCES/aztec/unit_icon.png", "aztec\\unit_icon.png")]
    [InlineData("custom\\unit_icon.png", "custom\\unit_icon.png")]
    public void RemoveResourcesPrefix_ChangesOnlyTheDisplayedPath(string fullPath, string expectedDisplay)
    {
        Assert.Equal(expectedDisplay, IconManagerWindow.RemoveResourcesPrefix(fullPath));
    }
}
