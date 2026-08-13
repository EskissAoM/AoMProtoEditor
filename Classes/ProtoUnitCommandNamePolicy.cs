using System;
using System.Collections.Generic;
using System.Linq;

namespace AoMDivineDataEditor.Classes;

public static class ProtoUnitCommandNamePolicy
{
    public static bool IsAvailable(string candidate, IEnumerable<string> existingNames, string? currentName = null)
    {
        var normalized = candidate?.Trim() ?? "";
        if (!InternalNamePolicy.IsValid(normalized))
            return false;

        return !existingNames.Any(existing =>
            !string.IsNullOrWhiteSpace(existing) &&
            !existing.Equals(currentName, StringComparison.OrdinalIgnoreCase) &&
            existing.Equals(normalized, StringComparison.OrdinalIgnoreCase));
    }
}
