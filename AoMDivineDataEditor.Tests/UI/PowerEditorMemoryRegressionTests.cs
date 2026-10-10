using System.Runtime.CompilerServices;
using AoMDivineDataEditor.Windows;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class PowerEditorMemoryRegressionTests
{
    [Fact]
    public void CompletedPowerEditorViewsAreCollectible()
    {
        using var workspace = new TestWorkspace(nameof(CompletedPowerEditorViewsAreCollectible));
        var path = workspace.WriteText("powers_mods.xml",
            "<powersmod><power name=\"MemoryProbe\" type=\"Rain\"><effect type=\"CannotSnare\"><target type=\"ProtoUnit\">Hero</target></effect></power></powersmod>");
        var references = Enumerable.Range(0, 8)
            .Select(_ => AvaloniaTestHost.Run(() => CreateEditorReference(path)))
            .ToArray();

        AvaloniaTestHost.CollectUiGarbage();

        Assert.True(references.Count(reference => reference.IsAlive) <= 1,
            $"{references.Count(reference => reference.IsAlive)} completed PowerEditorView instances remained alive.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateEditorReference(string path)
    {
        var editor = new PowerEditorView([], path, protoUnitNames: ["Hero"]);
        editor.SetModifiedMode(true);
        editor.SelectPower("MemoryProbe");
        return new WeakReference(editor);
    }
}
