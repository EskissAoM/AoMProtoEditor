using System;
using System.Linq;
using System.Xml.Linq;

namespace CryBarEditor.Classes;

public sealed class ProtoUnitTransformDefinition
{
    private readonly XElement _source;

    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public string Tech { get; set; } = "";
    public string Command { get; set; } = "";
    public string RevertOthersTo { get; set; } = "";
    public bool FullHeal { get; set; }

    public ProtoUnitTransformDefinition(XElement? source = null)
    {
        _source = source != null ? new XElement(source) : new XElement("transform");
    }

    public static ProtoUnitTransformDefinition FromElement(XElement element)
    {
        string Child(string name) => element.Elements()
            .FirstOrDefault(child => child.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?.Value?.Trim() ?? "";

        return new ProtoUnitTransformDefinition(element)
        {
            From = Child("from"),
            To = Child("to"),
            Tech = Child("tech"),
            Command = Child("command"),
            RevertOthersTo = Child("revertothersto"),
            FullHeal = string.Equals(element.Attributes()
                .FirstOrDefault(attribute => attribute.Name.LocalName.Equals("fullheal", StringComparison.OrdinalIgnoreCase))
                ?.Value, "true", StringComparison.OrdinalIgnoreCase)
        };
    }

    public ProtoUnitTransformDefinition Clone()
        => FromElement(ToElement());

    public XElement ToElement()
    {
        var element = new XElement(_source);
        element.Name = "transform";

        var fullHealAttribute = element.Attributes()
            .FirstOrDefault(attribute => attribute.Name.LocalName.Equals("fullheal", StringComparison.OrdinalIgnoreCase));
        if (FullHeal)
        {
            if (fullHealAttribute == null)
                element.Add(new XAttribute("fullheal", "true"));
            else
                fullHealAttribute.Value = "true";
        }
        else
        {
            fullHealAttribute?.Remove();
        }

        SetChild(element, "from", From, required: true);
        SetChild(element, "to", To, required: true);
        SetChild(element, "tech", Tech, required: false);
        SetChild(element, "command", Command, required: true);
        SetChild(element, "revertothersto", RevertOthersTo, required: false);
        return element;
    }

    private static void SetChild(XElement parent, string name, string value, bool required)
    {
        var matches = parent.Elements()
            .Where(child => child.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var first = matches.FirstOrDefault();
        foreach (var duplicate in matches.Skip(1))
            duplicate.Remove();

        value = value?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(value) && !required)
        {
            first?.Remove();
            return;
        }

        if (first == null)
        {
            var knownOrder = new[] { "from", "to", "tech", "command", "revertothersto" };
            var targetIndex = Array.IndexOf(knownOrder, name);
            var insertBefore = parent.Elements().FirstOrDefault(child =>
            {
                var childIndex = Array.FindIndex(knownOrder, item => item.Equals(child.Name.LocalName, StringComparison.OrdinalIgnoreCase));
                return childIndex >= 0 && childIndex > targetIndex;
            });
            first = new XElement(name, value);
            if (insertBefore != null)
                insertBefore.AddBeforeSelf(first);
            else
                parent.Add(first);
        }
        else
        {
            first.Value = value;
        }
    }
}
