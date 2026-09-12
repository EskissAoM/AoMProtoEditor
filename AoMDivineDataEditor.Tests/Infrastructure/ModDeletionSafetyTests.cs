using System.Text;
using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class ModDeletionSafetyTests
{
    [Fact]
    public void InspectIdentifier_FindsRecognizedAndUnknownXmlReferencesButNotTheDefinition()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var definitionPath = Path.Combine(directory, "proto_mods.xml");
            var customPath = Path.Combine(directory, "custom_rules.xml");
            File.WriteAllText(definitionPath, "<protomods><unit name=\"TargetUnit\" /></protomods>");
            File.WriteAllText(customPath,
                "<rules><train>TargetUnit</train><futureReference>TargetUnit</futureReference></rules>");

            var result = ModDeletionSafety.InspectIdentifier(
                ModReferenceFileCatalog.Discover(directory),
                GameIdentifierKind.ProtoUnit,
                "TargetUnit",
                directory);

            Assert.Equal(2, result.References.Count);
            Assert.Contains(result.References, reference => reference.IsRecognized && reference.Field == "<train>");
            Assert.Contains(result.References, reference =>
                !reference.IsRecognized && reference.Field.Contains("<futureReference>", StringComparison.Ordinal));
            Assert.DoesNotContain(result.References, reference => reference.Path == definitionPath);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void InspectIdentifier_FindsTacticsAndUnknownCustomFileReferences()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var tacticsPath = Path.Combine(directory, "custom.tactics");
            var customPath = Path.Combine(directory, "custom.rule");
            File.WriteAllText(tacticsPath, "<tactics><action><projectile>TargetUnit</projectile></action></tactics>");
            File.WriteAllText(customPath, "spawn_target = TargetUnit");

            var result = ModDeletionSafety.InspectIdentifier(
                ModReferenceFileCatalog.Discover(directory),
                GameIdentifierKind.ProtoUnit,
                "TargetUnit",
                directory);

            Assert.Contains(result.References, reference => reference.Path == tacticsPath && reference.IsRecognized);
            Assert.Contains(result.References, reference => reference.Path == customPath && !reference.IsRecognized);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void InspectIdentifier_PreservesEveryOwningEntityForRepeatedFields()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "proto_mods.xml");
            File.WriteAllText(path,
                "<protomods><unit name=\"BarracksA\"><train>TargetUnit</train></unit>" +
                "<unit name=\"BarracksB\"><train>TargetUnit</train></unit></protomods>");

            var result = ModDeletionSafety.InspectIdentifier(
                ModReferenceFileCatalog.Discover(directory),
                GameIdentifierKind.ProtoUnit,
                "TargetUnit",
                directory);

            Assert.Equal(2, result.References.Count);
            Assert.Contains(result.References, reference => reference.Owner == "BarracksA");
            Assert.Contains(result.References, reference => reference.Owner == "BarracksB");
            Assert.All(result.References, reference => Assert.Contains("— <train>", reference.DisplayLocation));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void InspectIdentifier_UsesWholeTokensInsteadOfSubstrings()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(Path.Combine(directory, "custom.rule"), "TargetUnitElite");

            var result = ModDeletionSafety.InspectIdentifier(
                [], GameIdentifierKind.ProtoUnit, "TargetUnit", directory);

            Assert.False(result.IsBlocked);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void FindReferencedStringIds_CoversSharedXmlTacticsCustomAndUtf16Files()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(Path.Combine(directory, "shared.xml"), "<root><displaynameid>STR_SHARED</displaynameid></root>");
            File.WriteAllText(Path.Combine(directory, "unit.tactics"), "<root><label>STR_TACTICS</label></root>");
            File.WriteAllText(Path.Combine(directory, "custom.rule"), "label = STR_CUSTOM");
            File.WriteAllBytes(Path.Combine(directory, "utf16.txt"), Encoding.Unicode.GetBytes("label = STR_UTF16"));

            var referenced = ModDeletionSafety.FindReferencedStringIds(
                directory,
                ["STR_SHARED", "STR_TACTICS", "STR_CUSTOM", "STR_UTF16", "STR_ORPHAN"]);

            Assert.Equal(4, referenced.Count);
            Assert.DoesNotContain("STR_ORPHAN", referenced);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void FindReferencedStringIds_UsesCandidateDocumentInsteadOfStaleDiskCopy()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "proto_mods.xml");
            File.WriteAllText(path, "<root><displaynameid>STR_REMOVED</displaynameid></root>");
            var candidate = XDocument.Parse("<root />");

            var referenced = ModDeletionSafety.FindReferencedStringIds(
                directory,
                ["STR_REMOVED"],
                [candidate],
                [path]);

            Assert.Empty(referenced);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "AoMDivineDataEditor.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
