using System.Text;
using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests.Gods;

public sealed class GodModulesTransactionSafetyTests
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(false);

    [Fact]
    public void Major_god_mod_safety_rejects_duplicate_definitions_and_managed_singletons()
    {
        var valid = ModXmlDocumentSafety.Validate(
            XDocument.Parse("<civsmods><civ><name>Gaia</name><culture>Atlantean</culture></civ></civsmods>"),
            ModXmlDocumentKind.MajorGods);
        var duplicateName = ModXmlDocumentSafety.Validate(
            XDocument.Parse("<civsmods><civ><name>Gaia</name></civ><civ><name>gaia</name></civ></civsmods>"),
            ModXmlDocumentKind.MajorGods);
        var duplicateField = ModXmlDocumentSafety.Validate(
            XDocument.Parse("<civsmods><civ><name>Gaia</name><culture>A</culture><Culture>B</Culture></civ></civsmods>"),
            ModXmlDocumentKind.MajorGods);

        Assert.True(valid.IsSafeToEdit);
        Assert.False(duplicateName.IsSafeToEdit);
        Assert.False(duplicateField.IsSafeToEdit);
    }

    [Fact]
    public async Task Async_document_transaction_rejects_a_stale_loaded_file_before_commit()
    {
        var directory = Path.Combine(Path.GetTempPath(), "aom-god-stale-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, CultureCatalog.ModFileName);
            File.WriteAllText(path, "<culturesmods><culture><name>Old</name></culture></culturesmods>", Utf8WithoutBom);
            var prepared = ModDocumentPersistence.Prepare(path, ModXmlDocumentKind.Cultures);
            CultureCatalog.SetField(prepared.Document.Root!.Element("culture")!, "compendiumtopic", "Local");
            const string external = "<culturesmods><!-- external --><culture><name>Old</name></culture></culturesmods>";
            File.WriteAllText(path, external, Utf8WithoutBom);
            var commitCalled = false;

            await Assert.ThrowsAsync<IOException>(() => ModDocumentPersistence.ExecuteAsync(
                [prepared],
                null,
                null,
                () =>
                {
                    commitCalled = true;
                    return Task.CompletedTask;
                }));

            Assert.False(commitCalled);
            Assert.Equal(external, File.ReadAllText(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Async_document_transaction_rolls_back_culture_and_strings_together()
    {
        var directory = Path.Combine(Path.GetTempPath(), "aom-god-rollback-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var culturePath = Path.Combine(directory, CultureCatalog.ModFileName);
            var stringsPath = Path.Combine(directory, "stringmods.txt");
            File.WriteAllText(culturePath, "<culturesmods><!-- keep --></culturesmods>", Utf8WithoutBom);
            File.WriteAllText(stringsPath, "Language = \"English\"\r\n// keep\r\n", Utf8WithoutBom);
            var cultureBefore = File.ReadAllBytes(culturePath);
            var stringsBefore = File.ReadAllBytes(stringsPath);
            var prepared = ModDocumentPersistence.Prepare(culturePath, ModXmlDocumentKind.Cultures);
            CultureCatalog.AddDefinition(prepared.Document, "NewCulture");
            var expectedStrings = FileIntegrityTransaction.CaptureState(stringsPath);

            await Assert.ThrowsAsync<InvalidOperationException>(() => ModDocumentPersistence.ExecuteAsync(
                [prepared],
                [stringsPath],
                new Dictionary<string, FileIntegrityTransaction.FileState>(StringComparer.OrdinalIgnoreCase)
                {
                    [stringsPath] = expectedStrings
                },
                () =>
                {
                    PreservedXmlFile.SaveAtomic(prepared.Document, prepared.Path);
                    File.WriteAllText(stringsPath, "changed", Utf8WithoutBom);
                    throw new InvalidOperationException("simulated second-file failure");
                }));

            Assert.Equal(cultureBefore, File.ReadAllBytes(culturePath));
            Assert.Equal(stringsBefore, File.ReadAllBytes(stringsPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void New_manager_writes_use_opening_fingerprints_and_shared_transactions()
    {
        var root = FindRepositoryRoot();
        var host = File.ReadAllText(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml.cs"));
        var majorGodEditor = File.ReadAllText(Path.Combine(root, "Windows", "MajorGodEditorView.axaml.cs"));
        var cultureManager = File.ReadAllText(Path.Combine(root, "Windows", "CultureManagerWindow.cs"));
        var minorGodManager = File.ReadAllText(Path.Combine(root, "Windows", "MinorGodManagerWindow.cs"));

        Assert.Contains("openedCultureState", host, StringComparison.Ordinal);
        Assert.Contains("openedStringsState", host, StringComparison.Ordinal);
        Assert.Contains("openedMinorGodState", host, StringComparison.Ordinal);
        Assert.Contains("ModDocumentPersistence.ExecuteAsync", host, StringComparison.Ordinal);
        Assert.Contains("[minorGodFile]", host, StringComparison.Ordinal);
        Assert.Contains("CaptureMinorGodTechnologyDependency", host, StringComparison.Ordinal);
        Assert.Contains("ModDocumentPersistence.ExecuteAsync", majorGodEditor, StringComparison.Ordinal);
        Assert.Contains("_saveGate", majorGodEditor, StringComparison.Ordinal);
        Assert.Contains("RunExclusiveAsync", cultureManager, StringComparison.Ordinal);
        Assert.Contains("RunExclusiveAsync", minorGodManager, StringComparison.Ordinal);
    }

    [Fact]
    public void MajorGod_external_change_reload_uses_the_host_window_when_the_editor_is_detached()
    {
        var root = FindRepositoryRoot();
        var host = File.ReadAllText(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml.cs"));
        var majorGodEditor = File.ReadAllText(Path.Combine(root, "Windows", "MajorGodEditorView.axaml.cs"));

        Assert.Contains("Window? dialogOwner = null", majorGodEditor, StringComparison.Ordinal);
        Assert.Contains("TopLevel.GetTopLevel(this) as Window ?? _dialogOwner", majorGodEditor, StringComparison.Ordinal);
        Assert.Contains("Major Gods changed outside the editor", majorGodEditor, StringComparison.Ordinal);
        Assert.Contains("confirmButtonText: \"Reload\"", majorGodEditor, StringComparison.Ordinal);
        Assert.Contains("EnsureMajorGodCanBeDeletedAsync,\n            this);", host.Replace("\r\n", "\n"), StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AoMDivineDataEditor.csproj")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
