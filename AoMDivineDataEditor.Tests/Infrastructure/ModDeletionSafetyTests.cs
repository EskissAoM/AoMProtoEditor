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
            var customPath = Path.Combine(directory, "custom.txt");
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
            File.WriteAllText(Path.Combine(directory, "custom.txt"), "TargetUnitElite");

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
            File.WriteAllText(Path.Combine(directory, "custom.txt"), "label = STR_CUSTOM");
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

    [Fact]
    public void FindReferencedStringIds_FindsAWholeTokenAcrossStreamingBufferBoundaries()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var token = "STR_BOUNDARY_REFERENCE";
            var prefixLength = 64 * 1024 - token.Length / 2;
            File.WriteAllText(Path.Combine(directory, "large.txt"), new string('x', prefixLength) + " " + token + " ");

            var referenced = ModDeletionSafety.FindReferencedStringIds(directory, [token]);

            Assert.Contains(token, referenced);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void InspectIdentifier_RejectsExternalEntityDocumentsWithoutReadingTheEntity()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var secretPath = Path.Combine(directory, "secret.txt");
            var xmlPath = Path.Combine(directory, "custom.xml");
            File.WriteAllText(secretPath, "TargetUnit");
            File.WriteAllText(xmlPath,
                $"<!DOCTYPE root [<!ENTITY external SYSTEM '{new Uri(secretPath).AbsoluteUri}'>]>" +
                "<root><futureReference>&external;</futureReference></root>");

            var exception = Assert.Throws<InvalidOperationException>(() =>
                ModDeletionSafety.InspectIdentifier(
                    ModReferenceFileCatalog.Discover(directory),
                    GameIdentifierKind.ProtoUnit,
                    "TargetUnit",
                    directory));

            Assert.Contains("could not be inspected safely", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void FindReferencedStringIds_StreamsLargeFilesOnceForAllCandidates()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(Path.Combine(directory, "large.txt"), new string('x', 4 * 1024 * 1024));
            var candidates = Enumerable.Range(0, 128).Select(index => $"STR_MISSING_{index}").ToArray();

            var before = GC.GetAllocatedBytesForCurrentThread();
            var referenced = ModDeletionSafety.FindReferencedStringIds(directory, candidates);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Empty(referenced);
            Assert.True(allocated < 64L * 1024 * 1024,
                $"Scanning one 4 MiB file allocated {allocated:N0} managed bytes; candidates must share one streaming pass.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void UnknownExtensionFilesAreNotInspectedAsGameReferences()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            foreach (var extension in new[] { ".json", ".lua", ".xs", ".unsupported" })
                File.WriteAllText(Path.Combine(directory, "unrelated" + extension), "TargetUnit STR_UNKNOWN");

            var deletion = ModDeletionSafety.InspectIdentifier(
                [], GameIdentifierKind.ProtoUnit, "TargetUnit", directory);
            var strings = ModDeletionSafety.FindReferencedStringIds(directory, ["STR_UNKNOWN"]);

            Assert.False(deletion.IsBlocked);
            Assert.Empty(strings);
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
