using Avalonia.Platform;
using CryBarEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class ApplicationResourceTests
{
    [Fact]
    public void Window_icon_is_embedded_under_the_current_assembly_name()
    {
        string assemblyName = typeof(SimpleWindow).Assembly.GetName().Name!;
        var iconUri = new Uri($"avares://{assemblyName}/Assets/editor_icon.png");

        Assert.True(AssetLoader.Exists(iconUri));
        using var icon = AssetLoader.Open(iconUri);
        Assert.True(icon.Length > 0);
    }
}
