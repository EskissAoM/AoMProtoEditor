using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class TechnologyStringMergePolicyTests
{
    [Fact]
    public void Reload_uses_external_value_only_for_ids_changed_on_disk()
    {
        var loaded = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["STR_SAME_ID"] = "loaded same",
            ["STR_EXTERNAL_ID"] = "loaded external",
            ["STR_DELETED_ID"] = "loaded deleted"
        };
        var external = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["STR_SAME_ID"] = "loaded same",
            ["STR_EXTERNAL_ID"] = "manually changed",
            ["STR_ADDED_ID"] = "manually added"
        };
        var pendingUpdates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["STR_SAME_ID"] = "editor-only change",
            ["STR_EXTERNAL_ID"] = "conflicting editor change",
            ["STR_NEW_TECH_ID"] = "new generated string"
        };
        var pendingRemovals = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "STR_DELETED_ID"
        };

        var externallyChanged = TechnologyStringMergePolicy.GetExternallyChangedIds(loaded, external);
        var replaced = TechnologyStringMergePolicy.UseExternalVersionsForChangedIds(
            externallyChanged,
            pendingUpdates,
            pendingRemovals);

        Assert.Equal(
            new[] { "STR_ADDED_ID", "STR_DELETED_ID", "STR_EXTERNAL_ID" },
            externallyChanged.OrderBy(id => id, StringComparer.OrdinalIgnoreCase));
        Assert.Equal(new[] { "STR_DELETED_ID", "STR_EXTERNAL_ID" }, replaced.OrderBy(id => id, StringComparer.OrdinalIgnoreCase));
        Assert.Equal("editor-only change", pendingUpdates["STR_SAME_ID"]);
        Assert.Equal("new generated string", pendingUpdates["STR_NEW_TECH_ID"]);
        Assert.False(pendingUpdates.ContainsKey("STR_EXTERNAL_ID"));
        Assert.Empty(pendingRemovals);
    }
}
