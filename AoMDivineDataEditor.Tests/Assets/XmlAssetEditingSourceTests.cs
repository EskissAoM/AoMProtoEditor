using System.Text;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.GameData;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class XmlAssetEditingSourceTests
{
    public static IEnumerable<object[]> Encodings()
    {
        foreach (var sound in new[] { false, true })
        foreach (var kind in new[] { "utf8", "utf8-bom", "utf16", "utf16-be", "utf32" }) yield return [sound, kind];
    }

    [Theory]
    [MemberData(nameof(Encodings))]
    public async Task SavesPreserveEncodingBomNewlinesAndRawDocumentContent(bool sound, string kind)
    {
        using var workspace = new TestWorkspace("xml-asset-source");
        Encoding encoding = kind switch
        {
            "utf8-bom" => new UTF8Encoding(true, true), "utf16" => new UnicodeEncoding(false, true, true),
            "utf16-be" => new UnicodeEncoding(true, true, true), "utf32" => new UTF32Encoding(false, true, true), _ => new UTF8Encoding(false, true)
        };
        var root = sound ? "soundset" : "animfile";
        var newline = kind == "utf8" ? "\n" : "\r\n";
        var text = $"<?xml version='1.0' encoding='{encoding.WebName}'?>{newline}<!--document--><?future keep?><{root} future='é'>{newline}  <attachment keep='yes'>Helmet<include>Old</include><!--inside--></attachment>{newline}  <empty custom='blank'/>{newline}</{root}>";
        var path = workspace.WriteText("nested/unit.xml", "");
        var sourceEncoding = new PreservedTextEncoding(text, encoding, encoding.GetPreamble());
        File.WriteAllBytes(path, sourceEncoding.Encode(text));
        var source = await XmlAssetEditingSource.OpenAsync(workspace.RootPath, "nested\\unit.xml");
        Assert.True(source.CanEdit); Assert.Equal(text, source.Text);
        var changed = text.Replace("Old", "New");
        Assert.Equal(changed, await source.SaveAsync(changed.Replace("\r\n", "\n")));
        Assert.Equal(sourceEncoding.Encode(changed), File.ReadAllBytes(path));
        Assert.Equal(changed.Replace("New", "Second"), await source.SaveAsync(changed.Replace("New", "Second")));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)!));
    }

    [Theory]
    [InlineData("<broken")]
    [InlineData("<other/>")]
    [InlineData("<animfile xmlns='future'/>")]
    [InlineData("<animfile/><animfile/>")]
    [InlineData("<!DOCTYPE animfile [<!ENTITY a 'text'>]><animfile>&a;</animfile>")]
    [InlineData("<?xml version='1.0' encoding='utf-16'?><animfile/>")]
    public async Task InvalidOrDifferentDocumentCannotReplaceSource(string candidate)
    {
        using var workspace = new TestWorkspace();
        var path = workspace.WriteText("unit.xml", "<animfile><!--keep--><future/></animfile>");
        var before = File.ReadAllBytes(path);
        var source = await XmlAssetEditingSource.OpenAsync(workspace.RootPath, "unit.xml");
        await Assert.ThrowsAnyAsync<Exception>(() => source.SaveAsync(candidate));
        Assert.Equal(before, File.ReadAllBytes(path)); Assert.Equal(Encoding.UTF8.GetString(before), source.Text);
        Assert.Single(Directory.GetFiles(workspace.RootPath));
    }

    [Theory]
    [InlineData("../outside.xml")]
    [InlineData("sub/../../outside.xml")]
    [InlineData("unit.xml.xmb")]
    [InlineData("C:\\outside.xml")]
    public async Task SourceMustBeLooseXmlInsideItsRoot(string relative)
    {
        using var workspace = new TestWorkspace();
        await Assert.ThrowsAsync<IOException>(() => XmlAssetEditingSource.OpenAsync(workspace.RootPath, relative));
        Assert.Empty(Directory.GetFiles(workspace.RootPath));
    }

    [Theory]
    [InlineData("changed")]
    [InlineData("deleted")]
    [InlineData("moved")]
    public async Task ExternalChangesAreNeverOverwrittenOrRecreated(string change)
    {
        using var workspace = new TestWorkspace();
        var path = workspace.WriteText("unit.xml", "<soundset><future>Original</future></soundset>");
        var source = await XmlAssetEditingSource.OpenAsync(workspace.RootPath, "unit.xml");
        if (change == "changed") File.WriteAllText(path, "<soundset><external>Keep</external></soundset>");
        else if (change == "deleted") File.Delete(path);
        else File.Move(path, workspace.GetPath("moved.xml"));
        var before = File.Exists(path) ? File.ReadAllBytes(path) : null;
        await Assert.ThrowsAsync<XmlAssetSourceChangedException>(() => source.SaveAsync("<soundset><future>Edited</future></soundset>"));
        Assert.Equal(before, File.Exists(path) ? File.ReadAllBytes(path) : null);
        if (change == "changed")
        {
            var reloaded = await source.ReloadAsync();
            Assert.Contains("external", reloaded.Text);
            await reloaded.SaveAsync("<soundset><external>Reviewed</external></soundset>");
        }
        Assert.DoesNotContain(Directory.GetFiles(workspace.RootPath), item => item.Contains(".tmp.", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false, false)] [InlineData(true, false)]
    [InlineData(false, true)] [InlineData(true, true)]
    public async Task CompiledAliasesBlockEditingAndAreNeverModified(bool shortAlias, bool late)
    {
        using var workspace = new TestWorkspace();
        var path = workspace.WriteText("unit.xml", "<animfile/>");
        var alias = workspace.GetPath(shortAlias ? "unit.xmb" : "unit.xml.xmb");
        if (!late) File.WriteAllText(alias, "compiled bytes");
        var source = await XmlAssetEditingSource.OpenAsync(workspace.RootPath, "unit.xml");
        if (late)
        {
            File.WriteAllText(alias, "compiled bytes");
            await Assert.ThrowsAsync<XmlAssetSourceChangedException>(() => source.SaveAsync("<animfile><future/></animfile>"));
            Assert.False((await source.ReloadAsync()).CanEdit);
        }
        else { Assert.False(source.CanEdit); await Assert.ThrowsAsync<InvalidOperationException>(() => source.SaveAsync("<animfile/>")); }
        Assert.Equal("<animfile/>", File.ReadAllText(path)); Assert.Equal("compiled bytes", File.ReadAllText(alias));
    }

    [Fact]
    public async Task IndependentWindowsCannotOverwriteEachOthersSaves()
    {
        using var workspace = new TestWorkspace(); workspace.WriteText("unit.xml", "<animfile/>");
        var first = await XmlAssetEditingSource.OpenAsync(workspace.RootPath, "unit.xml");
        var second = await XmlAssetEditingSource.OpenAsync(workspace.RootPath, "unit.xml");
        await first.SaveAsync("<animfile><first/></animfile>");
        await Assert.ThrowsAsync<XmlAssetSourceChangedException>(() => second.SaveAsync("<animfile><second/></animfile>"));
        Assert.Equal("<animfile><first/></animfile>", File.ReadAllText(first.Path));
    }

    [Fact]
    public async Task ReadOnlyFileCannotBeEditedAndLaterReadOnlyChangeKeepsDraft()
    {
        using var workspace = new TestWorkspace(); var path = workspace.WriteText("unit.xml", "<animfile/>");
        var source = await XmlAssetEditingSource.OpenAsync(workspace.RootPath, "unit.xml");
        File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
        try
        {
            Assert.False((await source.ReloadAsync()).CanEdit);
            await Assert.ThrowsAsync<IOException>(() => source.SaveAsync("<animfile><future/></animfile>"));
            Assert.Equal("<animfile/>", File.ReadAllText(path));
        }
        finally { File.SetAttributes(path, FileAttributes.Normal); }
    }

    [Fact]
    public async Task EditedXmlRetainsTheExistingPreviewDepthBound()
    {
        using var workspace = new TestWorkspace(); var path = workspace.WriteText("unit.xml", "<animfile/>");
        var source = await XmlAssetEditingSource.OpenAsync(workspace.RootPath, "unit.xml");
        var text = "<animfile>" + string.Concat(Enumerable.Repeat("<nested>", 258)) + string.Concat(Enumerable.Repeat("</nested>", 258)) + "</animfile>";
        await Assert.ThrowsAsync<AssetPayloadLimitException>(() => source.SaveAsync(text));
        Assert.Equal("<animfile/>", File.ReadAllText(path));
    }
}
