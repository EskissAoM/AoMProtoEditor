using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace CryBarEditor.Classes;

public sealed class AbilityBindingMetadataRecord
{
    public string ModPath { get; set; } = "";
    public string UnitName { get; set; } = "";
    public string AbilityName { get; set; } = "";
    public string MainAction { get; set; } = "";
    public bool MainOwnsChargeAction { get; set; }
    public string AuxAction { get; set; } = "";
    public bool AuxOwnsChargeAction { get; set; }
}

/// <summary>
/// Editor-only provenance for ability/action recharge bindings. This deliberately
/// lives outside the game's XML so the editor can distinguish manual/tactics flags
/// from chargeaction/auxchargeaction flags it owns across sessions.
/// </summary>
public static class AbilityBindingMetadataStore
{
    private const string MetadataFilename = "aom_editor_ability_bindings.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string MetadataPath => ProtoEditorSettings.GetAppDataPath(MetadataFilename);

    private static string NormalizeModPath(string modPath)
    {
        try { return Path.GetFullPath(modPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
        catch { return modPath.Trim(); }
    }

    private static List<AbilityBindingMetadataRecord> LoadAll()
    {
        var path = MetadataPath;
        if (!File.Exists(path)) return [];
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<AbilityBindingMetadataRecord>>(json, JsonOptions) ?? [];
        }
        catch
        {
            // Metadata must never make game/mod XML unusable. If the editor metadata
            // itself is damaged, fall back conservatively: nothing is considered owned.
            return [];
        }
    }

    private static void SaveAll(List<AbilityBindingMetadataRecord> records)
    {
        var path = MetadataPath;
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var temp = Path.Combine(directory ?? ".", $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            var json = JsonSerializer.Serialize(records, JsonOptions);
            File.WriteAllText(temp, json);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }
    }

    public static AbilityBindingMetadataRecord? Get(string? modPath, string unitName, string abilityName)
    {
        if (string.IsNullOrWhiteSpace(modPath) || string.IsNullOrWhiteSpace(unitName) || string.IsNullOrWhiteSpace(abilityName))
            return null;
        var normalized = NormalizeModPath(modPath);
        return LoadAll().FirstOrDefault(record =>
            record.ModPath.Equals(normalized, StringComparison.OrdinalIgnoreCase) &&
            record.UnitName.Equals(unitName, StringComparison.OrdinalIgnoreCase) &&
            record.AbilityName.Equals(abilityName, StringComparison.OrdinalIgnoreCase));
    }

    public static void ReplaceUnit(string? modPath, string unitName, IEnumerable<AbilityBindingMetadataRecord> bindings)
    {
        if (string.IsNullOrWhiteSpace(modPath) || string.IsNullOrWhiteSpace(unitName)) return;
        var normalized = NormalizeModPath(modPath);
        var records = LoadAll();
        records.RemoveAll(record =>
            record.ModPath.Equals(normalized, StringComparison.OrdinalIgnoreCase) &&
            record.UnitName.Equals(unitName, StringComparison.OrdinalIgnoreCase));

        foreach (var binding in bindings)
        {
            if (string.IsNullOrWhiteSpace(binding.AbilityName)) continue;
            if (string.IsNullOrWhiteSpace(binding.MainAction) && string.IsNullOrWhiteSpace(binding.AuxAction) &&
                !binding.MainOwnsChargeAction && !binding.AuxOwnsChargeAction)
                continue;
            binding.ModPath = normalized;
            binding.UnitName = unitName;
            records.Add(binding);
        }
        SaveAll(records);
    }

    public static void CopyUnit(string? modPath, string sourceUnitName, string newUnitName)
    {
        if (string.IsNullOrWhiteSpace(modPath) || string.IsNullOrWhiteSpace(sourceUnitName) || string.IsNullOrWhiteSpace(newUnitName)) return;
        var normalized = NormalizeModPath(modPath);
        var records = LoadAll();
        var copies = records
            .Where(record => record.ModPath.Equals(normalized, StringComparison.OrdinalIgnoreCase) &&
                             record.UnitName.Equals(sourceUnitName, StringComparison.OrdinalIgnoreCase))
            .Select(record => new AbilityBindingMetadataRecord
            {
                ModPath = normalized,
                UnitName = newUnitName,
                AbilityName = record.AbilityName,
                MainAction = record.MainAction,
                MainOwnsChargeAction = record.MainOwnsChargeAction,
                AuxAction = record.AuxAction,
                AuxOwnsChargeAction = record.AuxOwnsChargeAction
            }).ToList();
        records.RemoveAll(record => record.ModPath.Equals(normalized, StringComparison.OrdinalIgnoreCase) &&
                                    record.UnitName.Equals(newUnitName, StringComparison.OrdinalIgnoreCase));
        records.AddRange(copies);
        SaveAll(records);
    }
}
