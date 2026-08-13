namespace AoMDivineDataEditor.Classes;

public readonly record struct StrictReferenceValidation(bool IsValid, string CanonicalValue, string ErrorMessage);

/// <summary>
/// Shared policy for catalog-backed editor values. New values must exist in the
/// current catalog, while an unavailable value loaded from XML may remain unchanged.
/// </summary>
public static class StrictReferencePolicy
{
    public static StrictReferenceValidation Validate(
        string? candidate,
        string? originalValue,
        IEnumerable<string> authoritativeOptions,
        bool allowEmpty,
        string label)
    {
        var value = candidate?.Trim() ?? "";
        if (value.Length == 0)
        {
            return allowEmpty
                ? new StrictReferenceValidation(true, "", "")
                : Invalid($"{label} is required.");
        }

        var canonical = authoritativeOptions.FirstOrDefault(option =>
            option.Equals(value, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(canonical))
            return new StrictReferenceValidation(true, canonical, "");

        // Deliberately use ordinal equality here: legacy preservation permits no edit,
        // including a casing-only change to a value absent from the current catalog.
        var original = originalValue?.Trim() ?? "";
        if (value.Equals(original, StringComparison.Ordinal) && original.Length > 0)
            return new StrictReferenceValidation(true, original, "");

        return Invalid($"{label} must match an existing value.");
    }

    private static StrictReferenceValidation Invalid(string message) => new(false, "", message);
}
