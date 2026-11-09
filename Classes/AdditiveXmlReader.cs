using System.Text.Json;
using System.Xml.Linq;

namespace AoMDivineDataEditor.Classes;

public enum AdditiveFamily { Proto, TechTree, Powers, MajorGods }
public enum AdditiveEntryKind { Original, Modified, Custom, Unresolved }
public sealed record AdditiveChange(Guid Handle, string Path, string Mode, XElement? Before, XElement Patch, XElement? Result, string? ActionName = null);
public sealed record AdditiveReadResult(XElement Effective, IReadOnlyList<AdditiveChange> Changes, IReadOnlyList<string> Diagnostics);

/// <summary>Versioned identity rules, independent of gameplay validation and tactics overlays.</summary>
public static class AdditiveIdentityRules
{
    private static readonly JsonDocument Catalog = Load();
    public static string Version => Catalog.RootElement.GetProperty("version").GetString()!;
    private static JsonDocument Load()
    {
        using var stream = typeof(AdditiveIdentityRules).Assembly.GetManifestResourceStream("AoMDivineDataEditor.AdditiveRules")!;
        return JsonDocument.Parse(stream);
    }

    internal static JsonElement? Find(AdditiveFamily family, XElement patch)
    {
        var group = family switch { AdditiveFamily.Proto => "ProtoUnit / ProtoAction data", AdditiveFamily.TechTree => "TechTree",
            AdditiveFamily.MajorGods => "Major gods / civilization", _ => "God powers" };
        // The recovered family is not a complete ancestry map. Limit lookup to
        // the actual entity and its known action/effects context, not arbitrary descendants.
        var ancestors = patch.Ancestors().Select(x => x.Name.LocalName.ToLowerInvariant()).ToArray();
        var allowed = family switch
        {
            AdditiveFamily.Proto => ancestors.All(x => x is "protomods" or "proto" or "unit" or "protoaction"),
            AdditiveFamily.TechTree => ancestors.All(x => x is "techtreemods" or "techtree" or "tech" or "effects"),
            AdditiveFamily.MajorGods => ancestors.All(x => x is "civsmods" or "civs" or "civ" or
                "startingunits" or "startingqueue" or "startingresources" or "shades" or "terraincreeps" or "terraincreep" or
                "bonusunitspawning" or "damagegoal" or "deathcountgoal" or "resourcegoal" or "bountyresourceearning" or "buildingchain" or "anchor" or
                "resourcegeneration" or "timeshifting" or "combatxp" or "oncastpowercostfactor"),
            _ => ancestors.All(x => x is "powersmod" or "powersmods" or "powers" or "power")
        };
        if (!allowed) return null;
        return Catalog.RootElement.GetProperty("rules").EnumerateArray()
            .Where(r => r.GetProperty("group").GetString() == group &&
                r.GetProperty("element").GetString()!.Equals(patch.Name.LocalName, StringComparison.OrdinalIgnoreCase))
            .Select(r => (JsonElement?)r).FirstOrDefault();
    }

    internal static string? Attribute(XElement node, string name) => node.Attributes()
        .FirstOrDefault(a => a.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value;
    private static string Text(XElement node) => string.Concat(node.Nodes().OfType<XText>().Select(t => t.Value)).Trim();
    internal static bool Matches(XElement candidate, XElement patch, JsonElement? rule)
    {
        if (candidate.Name.Namespace != patch.Name.Namespace || !candidate.Name.LocalName.Equals(patch.Name.LocalName, StringComparison.OrdinalIgnoreCase)) return false;
        if (rule is not { } r) return true;
        bool Equal(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        if (r.GetProperty("match_element_text").GetBoolean() && !Equal(Text(candidate), Text(patch))) return false;
        var keys = r.GetProperty("keys").EnumerateArray().Select(k => k.GetString()!).ToArray();
        if (r.GetProperty("match_named_children").GetBoolean())
        {
            foreach (var key in keys)
                if (!Equal(candidate.Elements().FirstOrDefault(c => c.Name.LocalName.Equals(key, StringComparison.OrdinalIgnoreCase))?.Value,
                    patch.Elements().FirstOrDefault(c => c.Name.LocalName.Equals(key, StringComparison.OrdinalIgnoreCase))?.Value)) return false;
        }
        else foreach (var key in keys) if (!Equal(Attribute(candidate, key), Attribute(patch, key))) return false;
        if (r.GetProperty("match_all_attributes").GetBoolean())
        {
            var a = candidate.Attributes().Where(x => !x.Name.LocalName.Equals("mergeMode", StringComparison.OrdinalIgnoreCase)).ToArray();
            var b = patch.Attributes().Where(x => !x.Name.LocalName.Equals("mergeMode", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (a.Length != b.Length || a.Any(x => !Equal(x.Value, Attribute(patch, x.Name.LocalName)))) return false;
        }
        return true;
    }
}

/// <summary>Detached, ordered read projection. Never writes into either input or plans edits.</summary>
public static class AdditiveXmlReader
{
    public static AdditiveEntryKind Classify(XElement? originals, string name, bool inMod) =>
        Classify(originals == null ? null :
        originals.Name.LocalName is "proto" or "protos" or "protomods"
            ? ProtoXmlHandler.GetUnitElement(originals, name) != null
            : originals.Elements().Any(x => string.Equals(AdditiveIdentityRules.Attribute(x, "name"), name, StringComparison.OrdinalIgnoreCase)), inMod);

    public static AdditiveEntryKind Classify(bool? originalIdentityExists, bool inMod)
        => !inMod ? AdditiveEntryKind.Original : originalIdentityExists switch
        { true => AdditiveEntryKind.Modified, false => AdditiveEntryKind.Custom, null => AdditiveEntryKind.Unresolved };

    public static AdditiveReadResult Evaluate(XElement original, XElement patchRoot, AdditiveFamily family)
    {
        var effective = new XElement(original);
        var changes = new List<AdditiveChange>();
        var diagnostics = new List<string>();
        void Apply(XElement parent, XElement patch, string path)
        {
            var mode = (AdditiveIdentityRules.Attribute(patch, "mergeMode") ?? "modify").ToLowerInvariant();
            var rule = AdditiveIdentityRules.Find(family, patch);
            var matches = parent.Elements().Where(c => AdditiveIdentityRules.Matches(c, patch, rule)).ToArray();
            if (patch.Name.Namespace != XNamespace.None)
            { diagnostics.Add($"{path}: namespaced operation is unsupported; source preserved."); return; }
            if (rule == null && parent.Elements(patch.Name).Skip(1).Any()) diagnostics.Add($"{path}: no resolved identity rule for repeated nodes; tag-only fallback is a prediction.");
            if (matches.Length > 1 && mode != "add") diagnostics.Add($"{path}: {matches.Length} identity matches; first sibling selected.");
            if (mode is not ("add" or "modify" or "default" or "replace" or "remove"))
            { diagnostics.Add($"{path}: unsupported mergeMode '{mode}'; source preserved."); return; }
            var found = matches.FirstOrDefault();
            var before = found == null ? null : new XElement(found);
            XElement Clean(XElement node)
            {
                var copy = new XElement(node);
                foreach (var a in copy.DescendantsAndSelf().Attributes().Where(a => a.Name.LocalName.Equals("mergeMode", StringComparison.OrdinalIgnoreCase)).ToArray()) a.Remove();
                return copy;
            }
            XElement? result = found;
            if (mode == "add" || (found == null && mode is "modify" or "default"))
            { result = Clean(patch); parent.Add(result); }
            else if (found == null) diagnostics.Add($"{path}: {mode} found no match.");
            else if (mode == "remove") { found.Remove(); result = null; }
            else if (mode == "replace") { result = Clean(patch); found.ReplaceWith(result); }
            else
            {
                foreach (var a in patch.Attributes().Where(a => !a.Name.LocalName.Equals("mergeMode", StringComparison.OrdinalIgnoreCase))) found.SetAttributeValue(a.Name, a.Value);
                if (!patch.HasElements)
                {
                    // An empty container fragment does not remove its existing
                    // children. Omission is not a delete operation.
                    if (!found.HasElements || !string.IsNullOrWhiteSpace(patch.Value)) found.Value = patch.Value;
                }
                else foreach (var child in patch.Elements()) Apply(found, child, path + "/" + child.Name.LocalName);
            }
            changes.Add(new(Guid.NewGuid(), path, mode, before, new XElement(patch), result,
                patch.Ancestors("protoaction").FirstOrDefault()?.Element("name")?.Value));
        }
        foreach (var patch in patchRoot.Elements()) Apply(effective, patch, "/" + patch.Name.LocalName);
        return new(effective, changes, diagnostics.Distinct().ToArray());
    }
}
