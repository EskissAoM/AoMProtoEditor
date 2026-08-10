using System.Threading.Tasks;
using AoMDivineDataEditor.GameData;

namespace CryBarEditor.Classes;

/// <summary>
/// Supplies the optional game context used by the data editor.
/// </summary>
public interface IProtoEditorHost
{
    BarArchive? CurrentBarFile { get; }
    string? CurrentBarPath { get; }
    string? RootDirectory { get; }
    ValueTask<string?> LookupStringKeyAsync(string key);
}

/// <summary>Design-time/default host when no surrounding application is present.</summary>
public sealed class EmptyProtoEditorHost : IProtoEditorHost
{
    public static EmptyProtoEditorHost Instance { get; } = new();

    public BarArchive? CurrentBarFile => null;
    public string? CurrentBarPath => null;
    public string? RootDirectory => null;

    public ValueTask<string?> LookupStringKeyAsync(string key) => ValueTask.FromResult<string?>(null);
}
