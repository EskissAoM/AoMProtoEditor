using System.Collections;
using System.Reflection;
using System.Xml.Linq;
using AoMDivineDataEditor.Windows;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class UnitRenameRecoveryTests
{
    private static object Field(BaselineHost host, string name) => typeof(ProtoEditorWindow)
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;

    [Theory]
    [InlineData("Old", "New", "Owned", true)]
    [InlineData("Other", "New", "Owned", false)]
    [InlineData("Old", "Unrelated", "Owned", false)]
    [InlineData("Old", "New", "BuiltInOnly", false)]
    public void OnlyPersistedTransformMatchingPendingRenameIsAccepted(string from, string unit, string command, bool expected)
    {
        using var fixture = new BaselineSafetyFixture();
        var path = fixture.Workspace.WriteXml("TestMod/game/data/gameplay/unit_transform_mods.xml",
            "<unittransformmods><transform><command>Owned</command><from>Old</from><to>Target</to></transform></unittransformmods>");
        var before = File.ReadAllBytes(path);
        var host = AvaloniaTestHost.Run(() => fixture.CreateHost(false));
        try
        {
            AvaloniaTestHost.Run(() =>
            {
                ((IDictionary)Field(host, "_pendingUnitRenames"))["Old"] = "New";
                Assert.Equal(expected, host.Call("IsPendingTransformSourceRename", command, from, unit));
            });
            Assert.Equal(before, File.ReadAllBytes(path));
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void InvalidUnitCanBeDiscardedOrKeptWithoutWriting(bool mainReplacement, bool discard)
    {
        using var fixture = new BaselineSafetyFixture();
        File.WriteAllText(fixture.ProtoPath, "<protomods><unit name='Old'/></protomods>");
        var before = File.ReadAllBytes(fixture.ProtoPath);
        var host = AvaloniaTestHost.Run(() => fixture.CreateHost(false));
        try
        {
            AvaloniaTestHost.RunAsync(async () =>
            {
                host.Show();
                var kind = Enum.Parse(typeof(ProtoEditorWindow).GetNestedType("EditorEntityKind", BindingFlags.NonPublic)!, "Units");
                host.Set("_activeEntityKind", kind);
                host.Set("_currentUnitName", "Old");
                var tab = host.Call("CreateDocumentTab", true, kind, true, "Old")!;
                tab.GetType().GetProperty("SavedElement")!.SetValue(tab, XElement.Parse("<unit name='Old'/>"));
                host.Set("_activeDocumentTab", tab);
                ((HashSet<string>)Field(host, "_dirtyUnitNames")).Add("Old");
                host.Set("_validateTransformAssignmentsBeforeUnitSaveAsync", (Func<Task<bool>>)(() =>
                    throw new InvalidOperationException("Discard must not validate the invalid transform.")));
                var task = (Task<bool>)(mainReplacement
                    ? host.Call("ConfirmMainDocumentReplacementAsync", kind, true, "Other")!
                    : host.Call("ConfirmDiscardInvalidUnitDraftAsync", tab)!);
                var prompt = Assert.Single(host.OwnedWindows.OfType<Prompt>());
                Assert.True(prompt.PromptIsConfirm);
                typeof(Prompt).GetProperty(nameof(Prompt.Confirmed))!.SetValue(prompt, discard);
                prompt.Close();
                Assert.Equal(discard, await task);
                Assert.Equal(!discard, ((HashSet<string>)Field(host, "_dirtyUnitNames")).Contains("Old"));
            });
            Assert.Equal(before, File.ReadAllBytes(fixture.ProtoPath));
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }
}
