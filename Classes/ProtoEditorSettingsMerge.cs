using System.Text.Json;
using System.Text.Json.Nodes;

namespace AoMDivineDataEditor.Classes;

/// <summary>Merge the five known settings fields, retaining the latest unrelated JSON.</summary>
internal static class ProtoEditorSettingsMerge
{
    private static readonly string[] Fields = [nameof(ProtoEditorConfig.DataBarPath),
        nameof(ProtoEditorConfig.UserFolderPath), nameof(ProtoEditorConfig.LastModFilePath),
        nameof(ProtoEditorConfig.DataBarMtime), nameof(ProtoEditorConfig.CategoriesByModPath)];

    internal static (string? Json, bool Changed, string? Conflict) Merge(
        ProtoEditorSettingsLoadResult observed, ProtoEditorSettingsLoadResult current, ProtoEditorConfig edited)
    {
        static string EffectiveJson(ProtoEditorSettingsLoadResult load) =>
            (load.State == ProtoEditorSettingsLoadState.Recovered ? load.BackupBaseline?.Json : load.Baseline?.Json) ?? "{}";
        static JsonObject Normalize(string json) => JsonSerializer.SerializeToNode(
            JsonSerializer.Deserialize(json, ProtoEditorJsonContext.Default.ProtoEditorConfig)!,
            ProtoEditorJsonContext.Default.ProtoEditorConfig)!.AsObject();

        var before = Normalize(EffectiveJson(observed));
        var now = Normalize(EffectiveJson(current));
        var desired = JsonSerializer.SerializeToNode(edited, ProtoEditorJsonContext.Default.ProtoEditorConfig)!.AsObject();
        var output = JsonNode.Parse(EffectiveJson(current))!.AsObject();
        var changed = false;
        foreach (var field in Fields)
        {
            if (JsonNode.DeepEquals(before[field], desired[field]) || JsonNode.DeepEquals(now[field], desired[field])) continue;
            if (!JsonNode.DeepEquals(before[field], now[field]))
                return (null, false, $"Settings field '{field}' changed in another editor. Reload before retrying; no settings file was replaced.");
            if (!desired.ContainsKey(field)) output.Remove(field);
            else if (field == nameof(ProtoEditorConfig.CategoriesByModPath) && desired[field] is JsonObject categories)
            {
                var merged = new JsonObject();
                foreach (var category in categories)
                {
                    if (category.Value is not JsonObject values) { merged[category.Key] = category.Value?.DeepClone(); continue; }
                    var retained = (output[field]?[category.Key] as JsonObject)?.DeepClone().AsObject() ?? new JsonObject();
                    foreach (var key in new[] { nameof(ProtoEditorCategoryData.Categories), nameof(ProtoEditorCategoryData.UnitCategories) })
                        retained[key] = values[key]?.DeepClone();
                    merged[category.Key] = retained;
                }
                output[field] = merged;
            }
            else output[field] = desired[field]?.DeepClone();
            changed = true;
        }
        return (output.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), changed, null);
    }
}
