namespace AoMDivineDataEditor.Classes;

public static class InternalNamePolicy
{
    public const string AllowedCharactersDescription = "letters, digits, '_' and '-'";

    public static bool IsValid(string? value)
    {
        var name = value?.Trim() ?? "";
        return name.Length > 0 && name.All(IsAllowedCharacter);
    }

    public static bool IsAllowedCharacter(char value)
        => value is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-';

    public static bool IsValidOrUnchangedLegacy(string? candidate, string? original)
    {
        var normalized = candidate?.Trim() ?? "";
        return IsValid(normalized) || normalized.Equals(original?.Trim() ?? "", StringComparison.Ordinal);
    }

    public static bool IsValidFileName(string? value, string extension)
    {
        var name = value?.Trim() ?? "";
        if (name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            name = name[..^extension.Length];
        return IsValid(name);
    }
}
