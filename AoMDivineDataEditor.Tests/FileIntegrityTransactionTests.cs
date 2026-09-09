using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class FileIntegrityTransactionTests
{
    [Fact]
    public void Execute_RestoresExistingFilesAndRemovesNewFilesWhenCommitFails()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var existingPath = Path.Combine(directory, "existing.xml");
            var newPath = Path.Combine(directory, "new.xml");
            File.WriteAllText(existingPath, "before");

            var exception = Assert.Throws<InvalidOperationException>(() =>
                FileIntegrityTransaction.Execute([existingPath, newPath], () =>
                {
                    File.WriteAllText(existingPath, "after");
                    File.WriteAllText(newPath, "created");
                    throw new InvalidOperationException("commit failed");
                }));

            Assert.Equal("commit failed", exception.Message);
            Assert.Equal("before", File.ReadAllText(existingPath));
            Assert.False(File.Exists(newPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Execute_KeepsEveryCommittedFileWhenCommitSucceeds()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var firstPath = Path.Combine(directory, "first.xml");
            var secondPath = Path.Combine(directory, "second.xml");
            File.WriteAllText(firstPath, "before");

            FileIntegrityTransaction.Execute([firstPath, secondPath, firstPath], () =>
            {
                File.WriteAllText(firstPath, "after");
                File.WriteAllText(secondPath, "created");
            });

            Assert.Equal("after", File.ReadAllText(firstPath));
            Assert.Equal("created", File.ReadAllText(secondPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Execute_BlocksAnExternalChangeBeforeRunningTheCommit()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "technology.xml");
            File.WriteAllText(path, "loaded");
            var expected = FileIntegrityTransaction.CaptureState(path);
            File.WriteAllText(path, "changed elsewhere");
            var commitRan = false;

            var exception = Assert.Throws<IOException>(() =>
                FileIntegrityTransaction.Execute(
                    [path],
                    new Dictionary<string, FileIntegrityTransaction.FileState>(StringComparer.OrdinalIgnoreCase)
                    {
                        [Path.GetFullPath(path)] = expected
                    },
                    () => commitRan = true));

            Assert.False(commitRan);
            Assert.Contains("changed outside the editor", exception.Message, StringComparison.Ordinal);
            Assert.Equal("changed elsewhere", File.ReadAllText(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_RestoresAllFilesWhenTheSecondWriteFails()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var technologyPath = Path.Combine(directory, "techtree_mods.xml");
            var stringsPath = Path.Combine(directory, "stringmods.txt");
            File.WriteAllText(technologyPath, "technology before");
            File.WriteAllText(stringsPath, "strings before");

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                FileIntegrityTransaction.ExecuteAsync([technologyPath, stringsPath], null, async () =>
                {
                    File.WriteAllText(technologyPath, "technology after");
                    File.WriteAllText(stringsPath, "strings after");
                    await Task.Yield();
                    throw new InvalidOperationException("string write failed");
                }));

            Assert.Equal("technology before", File.ReadAllText(technologyPath));
            Assert.Equal("strings before", File.ReadAllText(stringsPath));
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
