using System.Threading.Tasks;
using CryBar.Bar;

namespace CryBarEditor.Classes;

/// <summary>
/// Supplies the optional game context used by the Proto Editor.  Keeping this
/// small lets the editor run either inside CryBarEditor or as its own desktop
/// application.
/// </summary>
public interface IProtoEditorHost
{
    BarFile? CurrentBarFile { get; }
    string? CurrentBarPath { get; }
    string? RootDirectory { get; }
    ValueTask<string?> LookupStringKeyAsync(string key);
}

/// <summary>Design-time/default host when no surrounding application is present.</summary>
public sealed class EmptyProtoEditorHost : IProtoEditorHost
{
    public static EmptyProtoEditorHost Instance { get; } = new();

    public BarFile? CurrentBarFile => null;
    public string? CurrentBarPath => null;
    public string? RootDirectory => null;

    public ValueTask<string?> LookupStringKeyAsync(string key) => ValueTask.FromResult<string?>(null);
}
