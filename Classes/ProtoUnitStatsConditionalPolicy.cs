namespace AoMDivineDataEditor.Classes;

public static class ProtoUnitStatsConditionalPolicy
{
    public static bool RequiresDamageType(string? modifyType)
        => ProtoConstants.GetModifyTypeValue(modifyType?.Trim() ?? "") is "ArmorSpecific" or "DamageSpecific";

    public static bool CanOfferBuildingOnlyAttribute(IEnumerable<string> unitTypes)
        => unitTypes.Any(value => value.Trim().Equals("Building", StringComparison.OrdinalIgnoreCase));

    public static string ResolvePointsTag(
        IEnumerable<string> unitTypes,
        bool hadTrainPoints,
        bool hadBuildPoints)
    {
        var types = unitTypes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var isUnit = types.Contains("Unit");
        var isBuilding = types.Contains("Building");
        if (isUnit && isBuilding)
            return hadTrainPoints ? "trainpoints" : hadBuildPoints ? "buildpoints" : "trainpoints";
        return isBuilding ? "buildpoints" : "trainpoints";
    }

    public static string ResolveModifyTypeOrLegacy(string? input, string? originalValue)
    {
        var known = ProtoConstants.GetModifyTypeValue(input?.Trim() ?? "");
        if (!string.IsNullOrWhiteSpace(known))
            return known;
        var normalized = input?.Trim() ?? "";
        var original = originalValue?.Trim() ?? "";
        return normalized.Equals(original, StringComparison.Ordinal) ? original : "";
    }
}
