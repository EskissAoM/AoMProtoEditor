using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AoMDivineDataEditor.Classes;

public class ProtoEditorConfig
{
    public string? DataBarPath { get; set; }
    public string? UserFolderPath { get; set; }
    public string? LastModFilePath { get; set; }
    public double DataBarMtime { get; set; }
    public Dictionary<string, ProtoEditorCategoryData>? CategoriesByModPath { get; set; }
}

/// <summary>
/// Editor-only grouping metadata. This is intentionally kept out of
/// proto_mods.xml because the Retold XML writer normalizes its root contents.
/// </summary>
public class ProtoEditorCategoryData
{
    public List<string> Categories { get; set; } = [];
    public Dictionary<string, string> UnitCategories { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

[JsonSourceGenerationOptions(WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ProtoEditorConfig))]
[JsonSerializable(typeof(ProtoEditorCategoryData))]
[JsonSerializable(typeof(Dictionary<string, ProtoEditorCategoryData>))]
internal partial class ProtoEditorJsonContext : JsonSerializerContext { }

public static class ProtoEditorSettings
{
    internal const string SettingsFilename = "aom_editor_settings.json";
    private static readonly Lazy<ProtoEditorSettingsStore> Store = new(() => new(
        AppDomain.CurrentDomain.BaseDirectory,
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));

    internal static string GetAppDataPath(string filename)
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string path = Path.Combine(baseDir, filename);

        try
        {
            // Test if baseDir is writable
            string testFile = Path.Combine(baseDir, $".test_{Guid.NewGuid()}");
            File.WriteAllText(testFile, "test");
            File.Delete(testFile);
            return path;
        }
        catch
        {
            // Fallback to user profile directory
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), filename);
        }
    }

    public static ProtoEditorSettingsLoadResult LoadSettingsResult() => Store.Value.Load();
    public static ProtoEditorSettingsLoadResult LoadCachedSettingsResult() => Store.Value.LoadCached();
    public static ProtoEditorSettingsLoadResult SelectSettingsSource(string path) => Store.Value.SelectSource(path);
    public static ProtoEditorSettingsSaveResult TryRememberLastModPath(string path) => Store.Value.RememberLastModPath(path);
    public static ProtoEditorSettingsSaveResult TryRememberDataBar(ProtoEditorSettingsLoadResult observed, string path, double mtime)
        => Store.Value.RememberDataBar(observed, path, mtime);

    public static ProtoEditorSettingsSaveResult TrySaveSettings(ProtoEditorSettingsLoadResult observed,
        ProtoEditorConfig config, bool confirmRecovery = false)
        => Store.Value.TrySave(observed, config, confirmRecovery);

    // Compatibility for external readers; editor callers use explicit load/save outcomes.
    public static ProtoEditorConfig LoadSettings() => LoadSettingsResult().Config ?? new ProtoEditorConfig();

    public static void SaveSettings(ProtoEditorConfig config)
    {
        try
        {
            Store.Value.Save(config);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Could not save settings: {ex.Message}");
        }
    }
}
