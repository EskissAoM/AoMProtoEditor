namespace AoMDivineDataEditor.Classes;

// Missing is an absent optional source. A required source that cannot be
// loaded must report Unavailable, not Missing.
public enum CatalogLoadState { Complete, Missing, Partial, Unavailable }

/// <summary>Availability of one catalog snapshot, separate from its suggestions.</summary>
public sealed record CatalogLoadResult<T>(IReadOnlyList<T> Entries, CatalogLoadState State,
    string Source, IReadOnlyList<string> Diagnostics)
{
    public bool IsComplete => State is CatalogLoadState.Complete or CatalogLoadState.Missing;
    public string Warning => IsComplete ? "" :
        $"The asset list for '{Source}' is incomplete. {string.Join(" ", Diagnostics)}";
    public CatalogLoadResult<TResult> Map<TResult>(Func<IReadOnlyList<T>, IReadOnlyList<TResult>> map)
        => new(map(Entries), State, Source, Diagnostics);
}
