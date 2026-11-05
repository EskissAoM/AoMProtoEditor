using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AoMDivineDataEditor.Classes;

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

    internal static string StoragePath => MetadataPath;

    internal static string PrepareUnitReplacements(string modPath,
        IReadOnlyDictionary<string, IReadOnlyList<AbilityBindingMetadataRecord>> replacements, string path)
    {
        var records = File.Exists(path)
            ? JsonNode.Parse(File.ReadAllText(path)) as JsonArray
                ?? throw new InvalidDataException("Ability binding metadata must contain a JSON array. No files were written.")
            : new JsonArray();
        var normalized = NormalizeModPath(modPath);
        foreach (var record in records.OfType<JsonObject>().Where(record =>
                     string.Equals(record["ModPath"]?.GetValue<string>(), normalized, StringComparison.OrdinalIgnoreCase) &&
                     replacements.Keys.Contains(record["UnitName"]?.GetValue<string>() ?? "", StringComparer.OrdinalIgnoreCase)).ToArray())
            records.Remove(record);
        foreach (var (unit, bindings) in replacements)
            foreach (var binding in bindings.Where(binding => !string.IsNullOrWhiteSpace(binding.AbilityName) &&
                         (!string.IsNullOrWhiteSpace(binding.MainAction) || !string.IsNullOrWhiteSpace(binding.AuxAction) ||
                          binding.MainOwnsChargeAction || binding.AuxOwnsChargeAction)))
            {
                binding.ModPath = normalized;
                binding.UnitName = unit;
                records.Add(JsonSerializer.SerializeToNode(binding, JsonOptions));
            }
        return records.ToJsonString(JsonOptions);
    }

    internal static string? PrepareAbilityRenames(string modPath,
        IReadOnlyList<KeyValuePair<string, string>> renames, string path)
    {
        if (!File.Exists(path)) return null;
        // Rename is a write operation: unlike read-only ownership lookup, malformed
        // metadata must not be silently replaced with an empty collection.
        var records = JsonNode.Parse(File.ReadAllText(path)) as JsonArray
            ?? throw new InvalidDataException("Ability binding metadata must contain a JSON array. No files were written.");
        var normalized = NormalizeModPath(modPath);
        var changed = false;
        foreach (var rename in renames)
        {
            var matching = records.OfType<JsonObject>().Where(record =>
                string.Equals(record["ModPath"]?.GetValue<string>(), normalized, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var record in matching.Where(record =>
                         string.Equals(record["AbilityName"]?.GetValue<string>(), rename.Key, StringComparison.OrdinalIgnoreCase)))
            {
                if (matching.Any(other => !ReferenceEquals(record, other) &&
                    string.Equals(other["UnitName"]?.GetValue<string>(), record["UnitName"]?.GetValue<string>(), StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(other["AbilityName"]?.GetValue<string>(), rename.Value, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("The renamed Ability already has binding metadata for this Unit. No files were written.");
                record["AbilityName"] = rename.Value;
                changed = true;
            }
        }
        return changed ? records.ToJsonString(JsonOptions) : null;
    }

    internal static void SavePrepared(string path, string contents)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = Path.Combine(Path.GetDirectoryName(path)!, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temp, contents);
            File.Move(temp, path, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

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

    public static void RenameUnit(string? modPath, string oldUnitName, string newUnitName)
    {
        if (string.IsNullOrWhiteSpace(modPath) || string.IsNullOrWhiteSpace(oldUnitName) ||
            string.IsNullOrWhiteSpace(newUnitName) || oldUnitName.Equals(newUnitName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var normalized = NormalizeModPath(modPath);
        var records = LoadAll();
        records.RemoveAll(record =>
            record.ModPath.Equals(normalized, StringComparison.OrdinalIgnoreCase) &&
            record.UnitName.Equals(newUnitName, StringComparison.OrdinalIgnoreCase));
        foreach (var record in records.Where(record =>
                     record.ModPath.Equals(normalized, StringComparison.OrdinalIgnoreCase) &&
                     record.UnitName.Equals(oldUnitName, StringComparison.OrdinalIgnoreCase)))
        {
            record.UnitName = newUnitName;
        }
        SaveAll(records);
    }
}
