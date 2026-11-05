using System.Collections;
using System.Reflection;
using System.Text;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Windows;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class AssetCreateManagerTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static IList Items(object manager) => (IList)manager.GetType().GetField("_items", Private)!.GetValue(manager)!;
    private static Task Commit(object manager, string method, params object[] arguments)
        => (Task)manager.GetType().GetMethod(method, Private)!.Invoke(manager, arguments)!;

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void AnimationAndSoundManagersOnlyAdoptPublishedFiles(bool sound, bool collision)
    {
        using var workspace = new TestWorkspace();
        var root = workspace.CreateDirectory(sound ? "sound" : "art");
        Assert.True(AssetDestinationPolicy.TryResolve(root, "custom", "copy", ".xml", "", out var destination, out _));
        const string xml = "<asset><!--keep--><future attr='é'/></asset>";
        if (collision) { Directory.CreateDirectory(Path.GetDirectoryName(destination!.AbsolutePath)!); File.WriteAllText(destination.AbsolutePath, "competitor"); }
        AvaloniaTestHost.RunAsync(async () =>
        {
            var manager = new AnimFileManagerWindow([new AnimFileCatalogEntry("original.xml", "Data.bar")],
                _ => Task.FromResult<string?>(xml), root, profile: sound ? XmlAssetManagerProfile.SoundSet : XmlAssetManagerProfile.AnimFile);
            try
            {
                if (collision)
                {
                    await Assert.ThrowsAsync<IOException>(() => Commit(manager, "DuplicateToDestinationAsync", xml, destination!));
                    Assert.Single(Items(manager).Cast<object>());
                    Assert.Equal("competitor", File.ReadAllText(destination!.AbsolutePath));
                }
                else
                {
                    await Commit(manager, "DuplicateToDestinationAsync", xml, destination!);
                    Assert.Equal(2, Items(manager).Count);
                    Assert.Contains(Items(manager).Cast<AnimFileCatalogEntry>(), item => item.Path == destination!.XmlValue && item.IsCustom);
                    Assert.Equal(Encoding.UTF8.GetBytes(xml), File.ReadAllBytes(destination!.AbsolutePath));
                }
            }
            finally { manager.Close(); }
        });
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("invalid")]
    [InlineData("collision")]
    public void IconManagerOnlyAdoptsAValidatedPublishedPng(string mode)
    {
        using var workspace = new TestWorkspace();
        var root = workspace.CreateDirectory("resources");
        var source = workspace.GetPath("source.png");
        var bytes = mode == "invalid" ? "fake PNG"u8.ToArray() : AssetCreateServiceTests.Png();
        File.WriteAllBytes(source, bytes);
        Assert.True(AssetDestinationPolicy.TryResolve(root, "", "imported", ".png", "resources", out var destination, out _));
        if (mode == "collision") File.WriteAllText(destination!.AbsolutePath, "competitor");
        AvaloniaTestHost.RunAsync(async () =>
        {
            var manager = new IconManagerWindow(["resources\\original.png"], [], root);
            try
            {
                if (mode == "valid")
                {
                    await Commit(manager, "ImportToDestinationAsync", source, destination!);
                    Assert.Equal(2, Items(manager).Count);
                    Assert.Contains(Items(manager).Cast<IconManagerItem>(), item => item.FullPath == destination!.XmlValue && item.IsCustom);
                    Assert.Equal(bytes, File.ReadAllBytes(destination!.AbsolutePath));
                }
                else
                {
                    var error = await Record.ExceptionAsync(() => Commit(manager, "ImportToDestinationAsync", source, destination!));
                    if (mode == "invalid") Assert.IsType<InvalidDataException>(error);
                    else Assert.IsType<IOException>(error);
                    Assert.Single(Items(manager).Cast<object>());
                    if (mode == "invalid") Assert.False(File.Exists(destination!.AbsolutePath));
                    else Assert.Equal("competitor", File.ReadAllText(destination!.AbsolutePath));
                }
                Assert.Equal(bytes, File.ReadAllBytes(source));
            }
            finally { manager.Close(); }
        });
    }
}
