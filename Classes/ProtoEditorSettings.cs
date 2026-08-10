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
    private const string SettingsFilename = "aom_editor_settings.json";

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

    private static string GetSettingsPath()
        => GetAppDataPath(SettingsFilename);

    public static ProtoEditorConfig LoadSettings()
    {
        string path = GetSettingsPath();
        if (!File.Exists(path))
        {
            return new ProtoEditorConfig();
        }

        try
        {
            string json = File.ReadAllText(path);
            var config = JsonSerializer.Deserialize(json, ProtoEditorJsonContext.Default.ProtoEditorConfig);
            return config ?? new ProtoEditorConfig();
        }
        catch
        {
            return new ProtoEditorConfig();
        }
    }

    public static void SaveSettings(ProtoEditorConfig config)
    {
        string path = GetSettingsPath();
        try
        {
            string json = JsonSerializer.Serialize(config, ProtoEditorJsonContext.Default.ProtoEditorConfig);
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Could not save settings to {path}: {ex.Message}");
        }
    }
}
