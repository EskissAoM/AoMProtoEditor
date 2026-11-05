using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace AoMDivineDataEditor.Classes;

public sealed class ProtoUnitCommandDefinition
{
    public static readonly string[] ValueFieldTags =
    [
        "command", "secondarycommand", "icon", "controllericon", "associatedtech", "trainableunitreq",
        "prereqcommand", "displaynameid", "rollovertextid", "shortrollovertextid", "activerollovertextid",
        "disabledrollovertextid", "buildlimittextid", "valuetext", "associatedpower", "forbidtech", "protounit",
        "amount", "costprotounit", "age", "actionforactiveicon", "activeicon", "disabledicon"
    ];

    public static readonly string[] RepeatableFieldTags = ["sharedcommand", "removecommandprequeueonprequeue"];
    public static readonly string[] DeprecatedValueFieldTags = ["activeicon", "disabledicon"];
    public static readonly string[] DeprecatedFlagTags = ["bindtochargeaction", "castpower", "displayaspassive"];

    public static readonly string[] FlagTags =
    [
        "commandpassesunitid", "usemultiple", "donotallowoverpoplimit", "donotallowifunitdamaged", "requireunitdamaged",
        "spawncommand", "socketbuild", "notcancellable", "deploy", "sitecommand", "allowedonotherplayers", "transform",
        "transformselected", "transformvillager", "unitcommand", "displayontarget", "researchonselected", "reusable",
        "allowprequeue", "canprequeuewhilefoundation", "displaywhileresearching", "researchcancelsothers", "singleuseglobal",
        "displayafteruse", "displaywithabilities", "sharedcommandhotkey", "quickactioncheckforavailable", "requiresfoundation",
        "bindtochargeaction", "castpower", "displayaspassive"
    ];

    public static string ExpandEmptyFlagElements(string xml)
    {
        if (string.IsNullOrEmpty(xml))
            return xml;

        var pattern = $@"<(?<name>{string.Join("|", FlagTags.Select(Regex.Escape))})\s*/>";
        return Regex.Replace(
            xml,
            pattern,
            match => $"<{match.Groups["name"].Value}></{match.Groups["name"].Value}>",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static readonly HashSet<string> KnownChildTags = new(
        ValueFieldTags.Concat(RepeatableFieldTags).Concat(FlagTags).Append("name"),
        StringComparer.OrdinalIgnoreCase);

    public string Name { get; set; } = "";
    public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, List<string>> RepeatableValues { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Flags { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> ExistingFlagText { get; } = new(StringComparer.OrdinalIgnoreCase);
    public XElement SourceElement { get; private set; } = new("protounitcommand");
    internal bool PreserveSourceStructure { get; set; }

    public static ProtoUnitCommandDefinition FromElement(XElement element)
    {
        var result = new ProtoUnitCommandDefinition { SourceElement = new XElement(element) };
        foreach (var child in element.Elements())
        {
            var tag = child.Name.LocalName;
            if (tag.Equals("name", StringComparison.OrdinalIgnoreCase))
            {
                result.Name = child.Value.Trim();
                continue;
            }

            if (RepeatableFieldTags.Contains(tag, StringComparer.OrdinalIgnoreCase))
            {
                if (!result.RepeatableValues.TryGetValue(tag, out var values))
                    result.RepeatableValues[tag] = values = [];
                values.Add(child.Value);
                continue;
            }

            if (FlagTags.Contains(tag, StringComparer.OrdinalIgnoreCase))
            {
                result.Flags.Add(tag);
                result.ExistingFlagText[tag] = child.Value;
                continue;
            }

            if (ValueFieldTags.Contains(tag, StringComparer.OrdinalIgnoreCase))
                result.Values[tag] = child.Value;
        }
        return result;
    }

    internal void AdoptSavedElement(XElement element)
    {
        var saved = FromElement(element);
        SourceElement = saved.SourceElement;
        Name = saved.Name;
        Values.Clear();
        foreach (var pair in saved.Values) Values[pair.Key] = pair.Value;
        RepeatableValues.Clear();
        foreach (var pair in saved.RepeatableValues) RepeatableValues[pair.Key] = pair.Value;
        Flags.Clear(); Flags.UnionWith(saved.Flags);
        ExistingFlagText.Clear();
        foreach (var pair in saved.ExistingFlagText) ExistingFlagText[pair.Key] = pair.Value;
    }

    public XElement ToElement()
        => PreserveSourceStructure ? ToPreservedElement() : ToProjectedElement();

    private XElement ToProjectedElement()
    {
        var result = new XElement(SourceElement);
        foreach (var child in result.Elements().Where(e => KnownChildTags.Contains(e.Name.LocalName)).ToList())
            child.Remove();

        result.AddFirst(new XElement("name", Name));

        foreach (var tag in ValueFieldTags)
        {
            if (Values.TryGetValue(tag, out var value) && !string.IsNullOrWhiteSpace(value))
                result.Add(new XElement(tag, value));
        }

        foreach (var tag in RepeatableFieldTags)
        {
            if (!RepeatableValues.TryGetValue(tag, out var values)) continue;
            foreach (var value in values.Where(v => !string.IsNullOrWhiteSpace(v)))
                result.Add(new XElement(tag, value));
        }

        foreach (var tag in FlagTags)
        {
            if (!Flags.Contains(tag)) continue;
            var node = ExistingFlagText.TryGetValue(tag, out var text) && !string.IsNullOrEmpty(text)
                ? new XElement(tag, text)
                : new XElement(tag, new XText(string.Empty));
            result.Add(node);
        }
        return result;
    }

    private XElement ToPreservedElement()
    {
        // Raw XML is authoritative. Only patch fields actually changed by the
        // owner (for example reconciled string IDs), retaining their attributes,
        // comments, empty elements and positions in the submitted source.
        var projection = ToProjectedElement();
        var baseline = FromElement(SourceElement).ToElement();
        var result = new XElement(SourceElement);
        foreach (var tag in KnownChildTags)
        {
            var desired = projection.Elements().Where(child => child.Name.LocalName.Equals(tag, StringComparison.OrdinalIgnoreCase)).ToList();
            var original = baseline.Elements().Where(child => child.Name.LocalName.Equals(tag, StringComparison.OrdinalIgnoreCase)).ToList();
            if (original.Select(child => child.Value).SequenceEqual(desired.Select(child => child.Value))) continue;
            var existing = result.Elements().Where(child => child.Name.LocalName.Equals(tag, StringComparison.OrdinalIgnoreCase)).ToList();
            for (var i = 0; i < desired.Count; i++)
            {
                if (i < existing.Count) XmlTriviaPreserver.SetText(existing[i], desired[i].Value);
                else if (existing.Count > 0) { var added = new XElement(desired[i]); existing[^1].AddAfterSelf(added); existing.Add(added); }
                else { var added = new XElement(desired[i]); result.Add(added); existing.Add(added); }
            }
            foreach (var surplus in existing.Skip(desired.Count)) surplus.Remove();
        }
        return result;
    }
}
