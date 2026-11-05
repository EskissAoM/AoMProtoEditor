using System.Reflection;
using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Windows;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BCnEncoder.Shared;
using Xunit;

namespace AoMDivineDataEditor.Tests;

// Exercise the actual confirmation callback, physical move and host adoption.
public sealed class IconMoveHostTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private static async Task<Prompt> PromptAsync(BaselineHost host)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var prompt = host.OwnedWindows.OfType<Prompt>().SingleOrDefault();
            if (prompt != null) return prompt;
            await Task.Delay(10);
        }
        throw new TimeoutException("Expected the icon move confirmation/error dialog.");
    }

    [Theory]
    [InlineData("png", "save")]
    [InlineData("png", "layout")]
    [InlineData("dds", "save")]
    [InlineData("dds", "cancel")]
    [InlineData("dds", "dirty")]
    [InlineData("dds", "late-counterpart")]
    [InlineData("dds", "original")]
    public void ActualIconCallbackUsesPhysicalIdentityAndKeepsExistingSavePolicies(string extension, string outcome)
    {
        using var fixture = new BaselineSafetyFixture();
        File.WriteAllText(fixture.ProtoPath, "<protomods><unit name='IconUnit'><icon>resources/old.png</icon><future keep='yes'/></unit></protomods>");
        var protoBefore = File.ReadAllBytes(fixture.ProtoPath);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var host = fixture.CreateHost(false);
            host.Call("InitializeEditorTabs");
            host.Call("BuildEditorPanel", "IconUnit", true);
            host.Set("_isReadOnly", false);
            host.Show();
            Dispatcher.UIThread.RunJobs();
            var root = (string)host.Call("GetCurrentModIconResourcesDirectory")!;
            Directory.CreateDirectory(root);
            var source = Path.Combine(root, "old." + extension);
            var imageBytes = extension == "dds" ? DdsIconDecoderTests.CreateDds(CompressionFormat.Rgba) : AssetCreateServiceTests.Png();
            File.WriteAllBytes(source, imageBytes);
            var retained = IconSourceResolver.ResolveCustom(root, "old.png")!;
            var item = new IconManagerItem("old.png", retained.LogicalPath, "old.png", outcome != "original") { Source = retained };
            Assert.True(AssetDestinationPolicy.TryResolve(root, outcome == "layout" ? "myfolder\\deeperfolder" : "nested",
                outcome == "layout" ? "editor_iconTRUEB" : "renamed", ".png", "resources", out var destination, out _));
            var physicalDestination = Path.ChangeExtension(destination!.AbsolutePath, "." + extension);
            try
            {
                if (outcome == "dirty") host.Set("_isDirty", true);
                var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                Dispatcher.UIThread.Post(async () =>
                {
                    try { completion.TrySetResult(await (Task<bool>)host.Call("MoveCustomIconAsync", item, destination)!); }
                    catch (Exception error) { completion.TrySetException(error); }
                });
                var operation = completion.Task;
                var prompt = await PromptAsync(host);
                if (outcome is "dirty" or "original")
                {
                    Assert.Equal(outcome == "dirty" ? "Save changes first" : "Icon move blocked", prompt.PromptTitle);
                    prompt.Close();
                }
                else
                {
                    Assert.Equal("Rename/Move Icon", prompt.PromptTitle);
                    if (outcome == "layout")
                    {
                        prompt.UpdateLayout();
                        var content = Assert.Single(prompt.GetVisualDescendants().OfType<ScrollViewer>());
                        Assert.True(content.Viewport.Height > 0);
                        Assert.True(content.Extent.Height <= content.Viewport.Height + 1,
                            $"Confirmation text requires scrolling: {content.Extent} versus {content.Viewport}");
                        Assert.True(content.Extent.Width <= content.Viewport.Width + 1);
                        Assert.InRange(prompt.Width, 320, Math.Min(600, host.ClientSize.Width));
                        Assert.InRange(prompt.Height, 160, Math.Min(360, host.ClientSize.Height));
                        prompt.Close();
                    }
                    else if (outcome == "cancel") prompt.Close();
                    else
                    {
                        if (outcome == "late-counterpart") File.WriteAllText(Path.ChangeExtension(source, ".png"), "external counterpart");
                        typeof(Prompt).GetMethod("ConfirmButton_Click", Private)!.Invoke(prompt, [null, new RoutedEventArgs()]);
                        if (outcome == "late-counterpart")
                        {
                            var error = await PromptAsync(host);
                            Assert.Equal("Asset move failed", error.PromptTitle);
                            error.Close();
                        }
                    }
                }
                Assert.Equal(outcome == "save", await operation.WaitAsync(TimeSpan.FromSeconds(3)));
                if (outcome == "save")
                {
                    Assert.False(File.Exists(source));
                    Assert.Equal(imageBytes, File.ReadAllBytes(physicalDestination));
                    var saved = XDocument.Load(fixture.ProtoPath);
                    Assert.Equal("resources\\nested\\renamed.png", saved.Descendants("icon").Single().Value);
                    Assert.Equal("yes", (string?)saved.Descendants("future").Single().Attribute("keep"));
                    var adopted = (XDocument)typeof(ProtoEditorWindow).GetField("_modXmlDoc", Private)!.GetValue(host)!;
                    Assert.Equal("resources\\nested\\renamed.png", adopted.Descendants("icon").Single().Value);
                }
                else
                {
                    Assert.Equal(imageBytes, File.ReadAllBytes(source));
                    Assert.Equal(protoBefore, File.ReadAllBytes(fixture.ProtoPath));
                    Assert.False(File.Exists(physicalDestination));
                    if (outcome == "dirty") Assert.True((bool)typeof(ProtoEditorWindow).GetField("_isDirty", Private)!.GetValue(host)!);
                    if (outcome == "late-counterpart") Assert.Equal("external counterpart", File.ReadAllText(Path.ChangeExtension(source, ".png")));
                }
            }
            finally
            {
                foreach (var prompt in host.OwnedWindows.OfType<Prompt>().ToArray()) prompt.Close();
                host.DisposeFixtureWindow();
            }
        });
    }
}
