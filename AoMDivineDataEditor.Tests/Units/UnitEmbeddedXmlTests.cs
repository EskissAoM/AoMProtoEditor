using System.Collections;
using System.Reflection;
using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Windows;
using AvaloniaEdit;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed partial class UnitXmlEditingTests
{
    private const string SnippetEffect = "<onhiteffect type='FutureEffect' marker='old'><payload keep='yes'/></onhiteffect>";
    private static string SnippetUnit(string effect)
        => $"<unit name='A'><maxhitpoints>10</maxhitpoints><protoaction><name>HandAttack</name><type>Attack</type>{effect}</protoaction></unit>";
    private static string SnippetDocument => $"<protomods>{SnippetUnit(SnippetEffect)}<unit name='B'/></protomods>";
    private static void PrepareSnippetActions(BaselineHost host)
    {
        // The minimal host skips full startup; populate the ordinary action catalog
        // from its mod before constructing action bodies, just as startup does.
        host.Call("RefreshProtoActionMetadata", (object)null!);
        host.Call("BuildEditorPanel", "A", false);
    }
    private static TextEditor[] OnHitSnippets(BaselineHost host)
        => Field<IList>(host, "_protoActionWidgets").Cast<object>()
            .SelectMany(widget => ((IList)widget.GetType().GetProperty("OnHitEffectRows")!.GetValue(widget)!).Cast<object>())
            .Select(row => (TextEditor)row.GetType().GetProperty("RawXmlTb")!.GetValue(row)!)
            .Where(editor => editor.IsVisible).ToArray();

    [Fact]
    public void UnsupportedUnitOnHitSnippetRefreshesOnParentSaveDiscardAndRemoval()
        => WithHost(async (fixture, host) =>
        {
            PrepareSnippetActions(host);
            var snippet = Assert.Single(OnHitSnippets(host));
            Assert.True(snippet.IsReadOnly); Assert.False(snippet.IsTabStop);
            Assert.False(Field<bool>(host, "_isDirty"));
            await Unlock(host);
            Raw(host).Text = SnippetUnit(SnippetEffect.Replace("'old'", "'saved'", StringComparison.Ordinal));
            Assert.Empty(await Save(host));
            Assert.True(Raw(host).IsReadOnly); Assert.False(Field<bool>(host, "_isDirty"));
            snippet = Assert.Single(OnHitSnippets(host));
            Assert.True(snippet.IsReadOnly); Assert.Contains("saved", snippet.Text);
            Assert.Equal("saved", (string?)XDocument.Load(fixture.ProtoPath).Descendants("onhiteffect").Single().Attribute("marker"));
            var savedBytes = File.ReadAllBytes(fixture.ProtoPath);

            await Unlock(host); Raw(host).Text = SnippetUnit(SnippetEffect.Replace("'old'", "'discarded'", StringComparison.Ordinal));
            var kind = typeof(ProtoEditorWindow).GetNestedType("EditorEntityKind", BindingFlags.NonPublic)!;
            var tab = host.Call("CreateDocumentTab", true, Enum.Parse(kind, "Units"), true, "A")!;
            tab.GetType().GetProperty("SavedElement")!.SetValue(tab, XDocument.Load(fixture.ProtoPath).Root!.Element("unit"));
            host.Call("DiscardUnitDocumentChanges", tab);
            host.Call("BuildEditorPanel", "B", false); Assert.Empty(OnHitSnippets(host));
            host.Call("BuildEditorPanel", "A", false);
            Assert.Contains("saved", Assert.Single(OnHitSnippets(host)).Text);
            Assert.DoesNotContain("discarded", Raw(host).Text);
            Assert.False(Field<bool>(host, "_isDirty")); Assert.Equal(savedBytes, File.ReadAllBytes(fixture.ProtoPath));

            await Unlock(host); Raw(host).Text = SnippetUnit("");
            Assert.Empty(await Save(host)); Assert.Empty(OnHitSnippets(host));
            Assert.Empty(XDocument.Load(fixture.ProtoPath).Descendants("onhiteffect"));
            host.Call("BuildEditorPanel", "B", false); host.Call("BuildEditorPanel", "A", false);
            Assert.Empty(OnHitSnippets(host)); Assert.False(Field<bool>(host, "_isDirty"));
        }, SnippetDocument);

    [Fact]
    public void UnitOnHitFallbackAndStructuredFormsFollowParentXmlSave()
        => WithHost(async (fixture, host) =>
        {
            PrepareSnippetActions(host);
            Assert.Single(OnHitSnippets(host));
            await Unlock(host); Raw(host).Text = SnippetUnit("<onhiteffect type='Stun' duration='2'/>");
            Assert.Empty(await Save(host)); Assert.Empty(OnHitSnippets(host));
            Assert.Equal("Stun", (string?)XDocument.Load(fixture.ProtoPath).Descendants("onhiteffect").Single().Attribute("type"));
            var widget = Assert.Single(Field<IList>(host, "_protoActionWidgets").Cast<object>());
            var row = Assert.Single(((IList)widget.GetType().GetProperty("OnHitEffectRows")!.GetValue(widget)!).Cast<object>());
            Assert.True((bool)row.GetType().GetProperty("IsSupported")!.GetValue(row)!);
            Assert.True(Raw(host).IsReadOnly); Assert.False(Field<bool>(host, "_isDirty"));
            await Unlock(host);
            Raw(host).Text = SnippetUnit(SnippetEffect.Replace("'old'", "'returned'", StringComparison.Ordinal));
            Assert.Empty(await Save(host));
            Assert.Contains("returned", Assert.Single(OnHitSnippets(host)).Text);
            Assert.True(Assert.Single(OnHitSnippets(host)).IsReadOnly);
            Assert.False(Field<bool>(host, "_isDirty"));
        }, SnippetDocument);

    [Fact]
    public void OriginalUnitOnHitFallbackCannotBeUnlocked()
        => WithHost((_, host) =>
        {
            var original = $"<proto>{SnippetUnit(SnippetEffect).Replace("name='A'", "name='Original'", StringComparison.Ordinal)}</proto>";
            var parsed = ProtoDataExtractor.ExtractProtoData(original);
            host.Set("_barXmlRoot", parsed.Root);
            host.Set("_barData", parsed.Data);
            host.Call("RefreshProtoActionMetadata", (object)null!);
            host.Call("BuildEditorPanel", "Original", false);
            Assert.True(Assert.Single(OnHitSnippets(host)).IsReadOnly);
            Assert.True(Raw(host).IsReadOnly); Assert.False(Field<bool>(host, "_isDirty"));
            return Task.CompletedTask;
        });
}
