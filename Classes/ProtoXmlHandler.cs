using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using AoMDivineDataEditor.GameData;

namespace AoMDivineDataEditor.Classes;

/// <summary>
/// Parsed representation of a ProtoAction element within a unit.
/// </summary>
public sealed class ProtoAction
{
    public string Name    { get; set; } = "";
    public string Type    { get; set; } = "";
    public string Rof     { get; set; } = "";
    public string MaxRange{ get; set; } = "";
    public List<(string DamageType, string Amount)> Damages       { get; } = [];
    public List<(string UnitType, string Multiplier)> DamageBonuses { get; } = [];
    public XElement? SourceElement { get; set; }
    public List<XElement> AdditionalElements { get; } = [];

    public ProtoAction Clone()
    {
        var clone = new ProtoAction
        {
            Name = Name,
            Type = Type,
            Rof = Rof,
            MaxRange = MaxRange,
            SourceElement = SourceElement != null ? new XElement(SourceElement) : null,
        };

        foreach (var damage in Damages)
            clone.Damages.Add(damage);

        foreach (var bonus in DamageBonuses)
            clone.DamageBonuses.Add(bonus);

        foreach (var element in AdditionalElements)
            clone.AdditionalElements.Add(new XElement(element));

        return clone;
    }
}

public sealed class ProtoActionStructuredFieldEntry
{
    public string Value { get; set; } = "";
    public Dictionary<string, string> Attributes { get; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ProtoCommandEntry
{
    public string Value { get; set; } = "";
    public string Row { get; set; } = "";
    public string Column { get; set; } = "";
    public string MergeMode { get; set; } = "";
}

public sealed class ProtoBuildLimitEntry
{
    public string Value { get; set; } = "";
    public string Weight { get; set; } = "";
}

public sealed class ProtoCultureFieldEntry
{
    public string Culture { get; set; } = "";
    public string Value { get; set; } = "";
}

/// <summary>
/// Static XML CRUD helpers – port of xml_handler.py using LINQ to XML.
/// </summary>
public static class ProtoXmlHandler
{
    private static readonly HashSet<string> KnownProtoActionChildTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "name",
        "type",
        "rof",
        "maxrange",
        "damage",
        "damagebonus",
    };

    private sealed class EncodingStringWriter(Encoding encoding) : StringWriter
    {
        public override Encoding Encoding { get; } = encoding;
    }

    // ─── Load / Save ──────────────────────────────────────────────────────────

    /// <summary>Parse a proto XML file from disk.</summary>
    public static (XDocument Doc, XElement Root) ParseProtoXml(string path)
    {
        var result = ModXmlDocumentSafety.LoadExisting(path, ModXmlDocumentKind.ProtoUnits);
        return (result.Document, result.Document.Root!);
    }

    /// <summary>Parse a proto XML string (from BAR / cache).</summary>
    public static (XDocument Doc, XElement Root) ParseProtoXmlString(string xml)
    {
        var doc  = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        var root = doc.Root ?? throw new InvalidOperationException("Empty XML document.");
        return (doc, root);
    }

    /// <summary>
    /// Save a proto XML document without rebuilding its node tree. Existing comments,
    /// processing instructions, unknown elements, whitespace, encoding, and newline
    /// convention are retained. A separate formatting source can be supplied when
    /// the destination is an atomic temporary file.
    /// </summary>
    public static void SaveProtoXml(XDocument doc, string path, string? formattingSourcePath = null)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var sourcePath = string.IsNullOrWhiteSpace(formattingSourcePath)
            ? path
            : formattingSourcePath;
        var format = ReadDocumentFormat(sourcePath, doc);
        var serializable = new XDocument(doc);
        EnsureExplicitArmorClosingTags(serializable);

        var settings = new XmlWriterSettings
        {
            Indent      = true,
            IndentChars = "  ",
            NewLineChars = format.Newline,
            // LINQ to XML normalizes parsed CRLF characters to LF in text nodes.
            // Replace them with the source document convention while serializing.
            NewLineHandling = NewLineHandling.Replace,
            OmitXmlDeclaration = serializable.Declaration == null,
            Encoding = format.TextEncoding.Encoding,
        };
        using var stringWriter = new EncodingStringWriter(format.TextEncoding.Encoding);
        using (var writer = XmlWriter.Create(stringWriter, settings))
            serializable.Save(writer);

        var xml = stringWriter.ToString();
        File.WriteAllBytes(path, format.TextEncoding.Encode(xml));
    }

    private static ProtoXmlDocumentFormat ReadDocumentFormat(string path, XDocument document)
    {
        if (File.Exists(path))
        {
            var encoded = PreservedTextEncoding.Decode(File.ReadAllBytes(path));
            return new ProtoXmlDocumentFormat(encoded, DetectNewline(encoded.Text));
        }

        var encoding = CreateEncodingForDeclaration(document.Declaration?.Encoding);
        return new ProtoXmlDocumentFormat(
            new PreservedTextEncoding("", encoding, encoding.GetPreamble()),
            DetectNewline(document.ToString(SaveOptions.DisableFormatting)));
    }

    private static Encoding CreateEncodingForDeclaration(string? declarationEncoding)
    {
        if (declarationEncoding?.Equals("utf-16", StringComparison.OrdinalIgnoreCase) == true ||
            declarationEncoding?.Equals("utf-16le", StringComparison.OrdinalIgnoreCase) == true)
        {
            return new UnicodeEncoding(bigEndian: false, byteOrderMark: true, throwOnInvalidBytes: true);
        }
        if (declarationEncoding?.Equals("utf-16be", StringComparison.OrdinalIgnoreCase) == true)
            return new UnicodeEncoding(bigEndian: true, byteOrderMark: true, throwOnInvalidBytes: true);
        if (declarationEncoding?.Equals("utf-32", StringComparison.OrdinalIgnoreCase) == true ||
            declarationEncoding?.Equals("utf-32le", StringComparison.OrdinalIgnoreCase) == true)
        {
            return new UTF32Encoding(bigEndian: false, byteOrderMark: true, throwOnInvalidCharacters: true);
        }
        if (declarationEncoding?.Equals("utf-32be", StringComparison.OrdinalIgnoreCase) == true)
            return new UTF32Encoding(bigEndian: true, byteOrderMark: true, throwOnInvalidCharacters: true);

        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    }

    private static string DetectNewline(string content)
    {
        var newline = content.IndexOf('\n');
        if (newline < 0)
            return Environment.NewLine;
        return newline > 0 && content[newline - 1] == '\r' ? "\r\n" : "\n";
    }

    private static void EnsureExplicitArmorClosingTags(XDocument document)
    {
        foreach (var element in document.Descendants().Where(element =>
                     element.Name.LocalName is "armor" or "directionalarmor" or "armoroverride" &&
                     !element.Nodes().Any()))
        {
            element.Add(new XText(""));
        }
    }

    private sealed record ProtoXmlDocumentFormat(PreservedTextEncoding TextEncoding, string Newline);

    /// <summary>Create a new proto_mods XML file with a minimal skeleton.</summary>
    public static (XDocument Doc, XElement Root) CreateNewProtoFile(string path)
    {
        var root = new XElement("protomods");
        var doc  = new XDocument(root);
        SaveProtoXml(doc, path);
        return (doc, root);
    }

    // ─── Unit Enumeration ─────────────────────────────────────────────────────

    /// <summary>Return sorted list of unit names from the root element.</summary>
    public static List<string> GetUnitNames(XElement root)
        => root.Descendants("unit")
               .Select(u => (string?)u.Attribute("name") ?? "")
               .Where(n => n.Length > 0)
               .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
               .ToList();

    /// <summary>Find a unit element by name (case-insensitive).</summary>
    public static XElement? GetUnitElement(XElement root, string name)
        => root.Descendants("unit")
               .FirstOrDefault(u =>
                   string.Equals((string?)u.Attribute("name"), name, StringComparison.OrdinalIgnoreCase));

    // ─── Simple Fields ────────────────────────────────────────────────────────

    /// <summary>Get text content of a direct child element, or null if absent.</summary>
    public static string? GetSimpleField(XElement unit, string tag)
        => (string?)unit.Element(tag);

    /// <summary>Set (create/update) a direct child element's text content.</summary>
    public static void SetSimpleField(XElement unit, string tag, string value)
    {
        var el = unit.Element(tag);
        if (el == null)
            unit.Add(new XElement(tag, value));
        else
            XmlTriviaPreserver.SetText(el, value);
    }

    /// <summary>Remove a direct child element entirely.</summary>
    public static void RemoveSimpleField(XElement unit, string tag)
        => unit.Elements(tag).Remove();

    public static void SetResourceMapEntries(
        XElement unit,
        string elementName,
        IEnumerable<(string ResourceType, string Value, string DropOffMultiplier)> entries,
        bool omitZeroValues = false)
    {
        unit.Elements(elementName).Remove();
        foreach (var entry in entries)
        {
            var resourceType = entry.ResourceType?.Trim() ?? "";
            var value = entry.Value?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(resourceType) || string.IsNullOrWhiteSpace(value))
                continue;
            if (omitZeroValues &&
                double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed) &&
                parsed == 0d)
            {
                continue;
            }

            var element = new XElement(elementName, value);
            element.SetAttributeValue("resourcetype", resourceType);
            var multiplier = entry.DropOffMultiplier?.Trim() ?? "";
            if (!string.IsNullOrWhiteSpace(multiplier))
                element.SetAttributeValue("dropoffmultiplier", multiplier);
            unit.Add(element);
        }
    }

    public static List<ProtoCultureFieldEntry> GetCultureAwareSimpleFields(XElement unit, string tag)
        => unit.Elements(tag)
               .Select(e => new ProtoCultureFieldEntry
               {
                   Culture = (string?)e.Attribute("culture") ?? "",
                   Value = e.Value?.Trim() ?? "",
               })
               .Where(x => !string.IsNullOrWhiteSpace(x.Value))
               .ToList();

    public static void SetCultureAwareSimpleFields(XElement unit, string tag, IEnumerable<ProtoCultureFieldEntry> entries)
    {
        unit.Elements(tag).Remove();

        foreach (var entry in entries.Where(x => !string.IsNullOrWhiteSpace(x.Value)))
        {
            var element = new XElement(tag, entry.Value.Trim());
            if (!string.IsNullOrWhiteSpace(entry.Culture))
                element.SetAttributeValue("culture", entry.Culture.Trim());
            unit.Add(element);
        }
    }

    // ─── Costs ───────────────────────────────────────────────────────────────

    /// <summary>Return cost entries as (resourceType, amount) pairs.</summary>
    public static List<(string ResourceType, string Amount)> GetCostEntries(XElement unit)
        => unit.Elements("cost")
               .Select(c => ((string?)c.Attribute("resourcetype") ?? "", c.Value))
               .Where(t => t.Item1.Length > 0)
               .ToList();

    /// <summary>Replace all cost entries with non-zero supplied pairs.</summary>
    public static void SetCostEntries(XElement unit, IEnumerable<(string ResourceType, string Amount)> entries)
    {
        unit.Elements("cost").Remove();
        foreach (var (rt, amount) in entries)
        {
            var resourceType = rt?.Trim() ?? "";
            var value = amount?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(resourceType) || string.IsNullOrWhiteSpace(value))
                continue;
            if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed) &&
                parsed == 0d)
            {
                continue;
            }
            unit.Add(new XElement("cost", new XAttribute("resourcetype", resourceType), value));
        }
    }

    // ─── Armor ───────────────────────────────────────────────────────────────

    /// <summary>Return armor entries as (armorType, value) pairs.</summary>
    public static List<(string ArmorType, string Value)> GetArmorEntries(XElement unit)
        => unit.Elements("armor")
               .Select(a => ((string?)a.Attribute("type") ?? "", (string?)a.Attribute("value") ?? a.Value))
               .Where(t => t.Item1.Length > 0)
               .ToList();

    /// <summary>Replace all armor entries with the supplied non-zero pairs.</summary>
    public static void SetArmorEntries(XElement unit, IEnumerable<(string ArmorType, string Value)> entries)
    {
        unit.Elements("armor").Remove();
        foreach (var (at, val) in entries)
        {
            var armorType = at?.Trim() ?? "";
            var value = val?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(armorType) || string.IsNullOrWhiteSpace(value))
                continue;
            if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed) &&
                parsed == 0d)
            {
                continue;
            }
            unit.Add(new XElement("armor", new XAttribute("type", armorType), new XAttribute("value", value)));
        }
    }

    // ─── Unit Types ───────────────────────────────────────────────────────────

    /// <summary>Return all <unittype> values for a unit.</summary>
    public static List<string> GetUnitTypeList(XElement unit)
        => unit.Elements("unittype").Select(e => e.Value).Where(v => v.Length > 0).ToList();

    /// <summary>Replace all <unittype> elements.</summary>
    public static void SetUnitTypeList(XElement unit, IEnumerable<string> types)
    {
        unit.Elements("unittype").Remove();
        foreach (var t in types)
            unit.Add(new XElement("unittype", t));
    }

    // ─── Flags ────────────────────────────────────────────────────────────────

    /// <summary>Return all <flag> values for a unit.</summary>
    public static List<string> GetFlagList(XElement unit)
        => unit.Elements("flag").Select(e => e.Value).Where(v => v.Length > 0).ToList();

    /// <summary>Replace all <flag> elements.</summary>
    public static void SetFlagList(XElement unit, IEnumerable<string> flags)
    {
        unit.Elements("flag").Remove();
        foreach (var f in flags)
            unit.Add(new XElement("flag", f));
    }

    public static List<ProtoCommandEntry> GetTrainEntries(XElement unit)
        => GetCommandEntries(unit, "train");

    public static void SetTrainEntries(XElement unit, IEnumerable<ProtoCommandEntry> entries)
        => SetCommandEntries(unit, "train", entries);

    public static List<ProtoCommandEntry> GetTechEntries(XElement unit)
        => GetCommandEntries(unit, "tech");

    public static void SetTechEntries(XElement unit, IEnumerable<ProtoCommandEntry> entries)
        => SetCommandEntries(unit, "tech", entries);

    public static List<ProtoCommandEntry> GetCommandEntries(XElement unit)
        => GetCommandEntries(unit, "command");

    public static void SetCommandEntries(XElement unit, IEnumerable<ProtoCommandEntry> entries)
        => SetCommandEntries(unit, "command", entries);

    public static List<ProtoCommandEntry> GetOptionalCommandEntries(XElement unit)
        => GetCommandEntries(unit, "optionalcommand");

    public static void SetOptionalCommandEntries(XElement unit, IEnumerable<ProtoCommandEntry> entries)
        => SetCommandEntries(unit, "optionalcommand", entries);

    public static ProtoCommandEntry? GetTransformCommandEntry(XElement unit)
        => GetCommandEntries(unit, "transformcommand").FirstOrDefault();

    public static void SetTransformCommandEntry(XElement unit, ProtoCommandEntry? entry)
        => SetCommandEntries(unit, "transformcommand", entry == null ? [] : [entry]);

    public static List<string> GetContainList(XElement unit)
        => unit.Elements("contain").Select(e => e.Value?.Trim() ?? "").Where(v => v.Length > 0).ToList();

    public static void SetContainList(XElement unit, IEnumerable<string> values, bool external = false)
    {
        unit.Elements("contain").Remove();
        foreach (var value in values.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var element = new XElement("contain", value.Trim());
            if (external)
                element.SetAttributeValue("external", "1");
            unit.Add(element);
        }
    }

    public static List<string> GetNotContainList(XElement unit)
        => unit.Elements("notcontain").Select(e => e.Value?.Trim() ?? "").Where(v => v.Length > 0).ToList();

    public static void SetNotContainList(XElement unit, IEnumerable<string> values)
    {
        unit.Elements("notcontain").Remove();
        foreach (var value in values.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
            unit.Add(new XElement("notcontain", value.Trim()));
    }

    public static List<string> GetDynamicBuildLimitUnitTypes(XElement unit)
        => unit.Element("dynamicbuildlimitunittypes")?
               .Elements("unittype")
               .Select(e => e.Value?.Trim() ?? "")
               .Where(v => v.Length > 0)
               .ToList()
           ?? [];

    public static void SetDynamicBuildLimitUnitTypes(XElement unit, IEnumerable<string> unitTypes)
    {
        unit.Element("dynamicbuildlimitunittypes")?.Remove();

        var values = unitTypes
            .Select(x => x?.Trim() ?? "")
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (values.Count == 0)
            return;

        unit.Add(new XElement("dynamicbuildlimitunittypes",
            values.Select(x => new XElement("unittype", x))));
    }

    public static List<ProtoBuildLimitEntry> GetSharedBuildLimitEntries(XElement unit)
        => unit.Element("sharedbuildlimitunittypes")?
               .Elements("unittype")
               .Select(e => new ProtoBuildLimitEntry
               {
                   Value = e.Value?.Trim() ?? "",
                   Weight = (string?)e.Attribute("weight") ?? "",
               })
               .Where(x => x.Value.Length > 0)
               .ToList()
           ?? [];

    public static void SetSharedBuildLimitEntries(XElement unit, IEnumerable<ProtoBuildLimitEntry> entries)
    {
        unit.Element("sharedbuildlimitunit")?.Remove();
        unit.Element("sharedbuildlimitunittypes")?.Remove();

        var values = entries
            .Where(x => !string.IsNullOrWhiteSpace(x.Value))
            .GroupBy(x => x.Value.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var first = g.First();
                return new ProtoBuildLimitEntry
                {
                    Value = first.Value.Trim(),
                    Weight = first.Weight?.Trim() ?? "",
                };
            })
            .ToList();

        if (values.Count == 0)
            return;

        unit.Add(new XElement("sharedbuildlimitunittypes",
            values.Select(x =>
            {
                var el = new XElement("unittype", x.Value);
                if (!string.IsNullOrWhiteSpace(x.Weight))
                    el.SetAttributeValue("weight", x.Weight.Trim());
                return el;
            })));
    }

    public static void RemoveBuildLimitModeElements(XElement unit)
    {
        unit.Element("dynamicbuildlimitunittypes")?.Remove();
        unit.Element("sharedbuildlimitunit")?.Remove();
        unit.Element("sharedbuildlimitunittypes")?.Remove();
    }

    // ─── ProtoActions ─────────────────────────────────────────────────────────

    /// <summary>Parse all protoaction elements for the given unit.</summary>
    public static List<ProtoAction> GetProtoActions(XElement unit)
    {
        var result = new List<ProtoAction>();
        foreach (var pa in unit.Elements("protoaction"))
            result.Add(ParseProtoActionLikeElement(pa));
        return result;
    }

    /// <summary>Replace all protoaction elements for the given unit.</summary>
    public static void SetProtoActions(XElement unit, IEnumerable<ProtoAction> actions)
    {
        unit.Elements("protoaction").Remove();
        foreach (var a in actions)
        {
            var pa = a.SourceElement != null
                ? new XElement(a.SourceElement)
                : new XElement("protoaction");

            pa.RemoveNodes();
            if (a.Name.Length     > 0) pa.Add(new XElement("name",     a.Name));
            if (a.Type.Length     > 0) pa.Add(new XElement("type",     a.Type));
            if (a.Rof.Length      > 0) pa.Add(new XElement("rof",      a.Rof));
            var minRange = a.AdditionalElements.FirstOrDefault(element =>
                element.Name.LocalName.Equals("minrange", StringComparison.OrdinalIgnoreCase));
            if (minRange != null) pa.Add(new XElement(minRange));
            if (a.MaxRange.Length > 0) pa.Add(new XElement("maxrange", a.MaxRange));
            foreach (var (dt, amt) in a.Damages)
                pa.Add(new XElement("damage", new XAttribute("type", dt), amt));
            foreach (var (ut, mult) in a.DamageBonuses)
                pa.Add(new XElement("damagebonus", new XAttribute("type", ut), mult));
            foreach (var extra in a.AdditionalElements.Where(element => !ReferenceEquals(element, minRange)))
                pa.Add(new XElement(extra));
            if (a.SourceElement != null)
                XmlTriviaPreserver.Preserve(a.SourceElement, pa);
            unit.Add(pa);
        }
    }

    public static ProtoAction ParseProtoActionLikeElement(XElement actionElement)
    {
        if (actionElement == null)
            throw new ArgumentNullException(nameof(actionElement));

        var action = new ProtoAction
        {
            Name = (string?)actionElement.Element("name") ?? "",
            Type = (string?)actionElement.Element("type") ?? "",
            Rof = (string?)actionElement.Element("rof") ?? "",
            MaxRange = (string?)actionElement.Element("maxrange") ?? "",
            SourceElement = new XElement(actionElement),
        };

        foreach (var d in actionElement.Elements("damage"))
            action.Damages.Add(((string?)d.Attribute("type") ?? "", d.Value));
        foreach (var db in actionElement.Elements("damagebonus"))
            action.DamageBonuses.Add(((string?)db.Attribute("type") ?? (string?)db.Attribute("unittype") ?? "", db.Value));
        foreach (var child in actionElement.Elements())
        {
            if (!KnownProtoActionChildTags.Contains(child.Name.LocalName))
                action.AdditionalElements.Add(new XElement(child));
        }

        return action;
    }

    public static string GetProtoActionSimpleFieldValue(ProtoAction action, string tag)
    {
        if (action == null)
            throw new ArgumentNullException(nameof(action));

        if (string.IsNullOrWhiteSpace(tag))
            return "";

        return action.AdditionalElements
            .FirstOrDefault(x =>
                x.Name.LocalName.Equals(tag.Trim(), StringComparison.OrdinalIgnoreCase) &&
                !x.HasElements)
            ?.Value
            ?.Trim()
            ?? "";
    }

    public static void SetProtoActionSimpleFieldValue(ProtoAction action, string tag, string value)
    {
        if (action == null)
            throw new ArgumentNullException(nameof(action));

        if (string.IsNullOrWhiteSpace(tag))
            return;

        var normalizedTag = tag.Trim();
        action.AdditionalElements.RemoveAll(x => x.Name.LocalName.Equals(normalizedTag, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(value))
            action.AdditionalElements.Add(new XElement(normalizedTag, value.Trim()));
    }

    public static List<ProtoActionStructuredFieldEntry> GetProtoActionStructuredFieldEntries(ProtoAction action, string tag)
    {
        if (action == null)
            throw new ArgumentNullException(nameof(action));

        if (string.IsNullOrWhiteSpace(tag))
            return [];

        return action.AdditionalElements
            .Where(x =>
                x.Name.LocalName.Equals(tag.Trim(), StringComparison.OrdinalIgnoreCase) &&
                !x.HasElements)
            .Select(element =>
            {
                var entry = new ProtoActionStructuredFieldEntry
                {
                    Value = element.Value?.Trim() ?? "",
                };

                foreach (var attribute in element.Attributes())
                {
                    if (!string.IsNullOrWhiteSpace(attribute.Name.LocalName))
                        entry.Attributes[attribute.Name.LocalName] = attribute.Value?.Trim() ?? "";
                }

                return entry;
            })
            .ToList();
    }

    public static void SetProtoActionStructuredFieldEntries(ProtoAction action, string tag, IEnumerable<ProtoActionStructuredFieldEntry> entries)
    {
        if (action == null)
            throw new ArgumentNullException(nameof(action));

        if (string.IsNullOrWhiteSpace(tag))
            return;

        var normalizedTag = tag.Trim();
        action.AdditionalElements.RemoveAll(x => x.Name.LocalName.Equals(normalizedTag, StringComparison.OrdinalIgnoreCase));

        foreach (var entry in entries ?? [])
        {
            var value = entry?.Value?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(value) &&
                (entry == null || entry.Attributes.Values.All(string.IsNullOrWhiteSpace)))
            {
                continue;
            }

            var element = new XElement(normalizedTag, value);
            if (entry != null)
            {
                foreach (var attribute in entry.Attributes)
                {
                    if (!string.IsNullOrWhiteSpace(attribute.Key) && !string.IsNullOrWhiteSpace(attribute.Value))
                        element.SetAttributeValue(attribute.Key.Trim(), attribute.Value.Trim());
                }
            }

            action.AdditionalElements.Add(element);
        }
    }

    // ─── Unit CRUD ────────────────────────────────────────────────────────────

    /// <summary>
    /// Add a new unit with default cost and armor entries.
    /// Returns the new XElement.
    /// </summary>
    public static XElement AddNewUnit(XElement root, string name)
    {
        var unit = new XElement("unit", new XAttribute("name", name));
        // Default costs
        foreach (var rt in ProtoConstants.KnownResourceTypes)
            unit.Add(new XElement("cost", new XAttribute("resourcetype", rt), "0"));
        // Default armors
        foreach (var at in ProtoConstants.KnownArmorTypes)
            unit.Add(new XElement("armor", new XAttribute("type", at), "0"));
        root.Add(unit);
        return unit;
    }

    /// <summary>Delete a unit by name. Returns true if found and removed.</summary>
    public static bool DeleteUnit(XElement root, string name)
    {
        var unit = GetUnitElement(root, name);
        if (unit == null) return false;
        unit.Remove();
        return true;
    }

    /// <summary>
    /// Clone an existing unit under a new name. Returns the new element,
    /// or null if the source unit was not found.
    /// </summary>
    public static XElement? CloneUnit(XElement root, string sourceName, string newName)
    {
        var source = GetUnitElement(root, sourceName);
        if (source == null) return null;
        return CloneUnit(root, source, newName);
    }

    /// <summary>Clone an existing unit element into the supplied root under a new name.</summary>
    public static XElement CloneUnit(XElement root, XElement source, string newName)
    {
        var clone = new XElement(source);
        clone.SetAttributeValue("name", newName);
        root.Add(clone);
        return clone;
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Test whether a unit element exists in the root.</summary>
    public static bool UnitExists(XElement root, string name)
        => GetUnitElement(root, name) != null;

    static List<ProtoCommandEntry> GetCommandEntries(XElement unit, string elementName)
        => unit.Elements(elementName)
               .Select(e => new ProtoCommandEntry
               {
                   Value = e.Value?.Trim() ?? "",
                   Row = (string?)e.Attribute("row") ?? "",
                   Column = (string?)e.Attribute("column") ?? "",
                   MergeMode = (string?)e.Attribute("mergeMode") ?? "",
               })
               .Where(e => e.Value.Length > 0)
               .ToList();

    static void SetCommandEntries(XElement unit, string elementName, IEnumerable<ProtoCommandEntry> entries)
    {
        unit.Elements(elementName).Remove();
        foreach (var entry in entries.Where(x => !string.IsNullOrWhiteSpace(x.Value)))
        {
            var element = new XElement(elementName, entry.Value);
            if (!string.IsNullOrWhiteSpace(entry.Row))
                element.SetAttributeValue("row", entry.Row);
            if (!string.IsNullOrWhiteSpace(entry.Column))
                element.SetAttributeValue("column", entry.Column);
            if (!string.IsNullOrWhiteSpace(entry.MergeMode))
                element.SetAttributeValue("mergeMode", entry.MergeMode);
            unit.Add(element);
        }
    }

}
