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

    public XElement ToElement()
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
}
