using System.Globalization;
using System.Xml.Linq;

namespace AoMDivineDataEditor.Classes;

/// <summary>
/// The small, string-free technology payload owned by an inline Transform command.
/// Unknown XML is preserved when an existing custom technology is edited.
/// </summary>
public sealed class TransformAssociatedTechnologyDefinition
{
    private readonly XElement _source;
    private readonly bool _isNew;

    public string Name { get; set; } = "";
    public string ResearchPoints { get; set; } = "0";
    public Dictionary<string, string> Costs { get; } = new(StringComparer.OrdinalIgnoreCase);

    public TransformAssociatedTechnologyDefinition(XElement? source = null)
    {
        _isNew = source == null;
        _source = source == null ? new XElement("tech") : new XElement(source);
        foreach (var resource in ProtoConstants.KnownResourceTypes)
            Costs[resource] = "0";
    }

    public static TransformAssociatedTechnologyDefinition Create(string name)
        => new() { Name = name };

    public static TransformAssociatedTechnologyDefinition FromElement(XElement element)
    {
        var result = new TransformAssociatedTechnologyDefinition(element)
        {
            Name = element.Attributes().FirstOrDefault(attribute =>
                attribute.Name.LocalName.Equals("name", StringComparison.OrdinalIgnoreCase))?.Value.Trim() ?? "",
            ResearchPoints = element.Elements().FirstOrDefault(child =>
                child.Name.LocalName.Equals("researchpoints", StringComparison.OrdinalIgnoreCase))?.Value.Trim() ?? "0"
        };
        foreach (var cost in element.Elements().Where(child =>
                     child.Name.LocalName.Equals("cost", StringComparison.OrdinalIgnoreCase)))
        {
            var resource = cost.Attributes().FirstOrDefault(attribute =>
                attribute.Name.LocalName.Equals("resourcetype", StringComparison.OrdinalIgnoreCase))?.Value.Trim() ?? "";
            if (ProtoConstants.KnownResourceTypes.Contains(resource, StringComparer.OrdinalIgnoreCase))
                result.Costs[resource] = cost.Value.Trim();
        }
        return result;
    }

    public TransformAssociatedTechnologyDefinition Clone() => FromElement(ToElement());

    public XElement ToElement()
    {
        var element = new XElement(_source);
        element.Name = "tech";
        element.SetAttributeValue("name", Name.Trim());

        SetSingleton(element, "researchpoints", NormalizeUnsignedFloat(ResearchPoints));
        if (_isNew)
        {
            SetSingleton(element, "status", "UNOBTAINABLE");
            if (!element.Elements().Any(child =>
                    child.Name.LocalName.Equals("flag", StringComparison.OrdinalIgnoreCase) &&
                    child.Value.Trim().Equals("HideAllNotifications", StringComparison.OrdinalIgnoreCase)))
                element.Add(new XElement("flag", "HideAllNotifications"));
        }
        foreach (var resource in ProtoConstants.KnownResourceTypes)
        {
            var matches = element.Elements().Where(child =>
                    child.Name.LocalName.Equals("cost", StringComparison.OrdinalIgnoreCase) &&
                    child.Attributes().Any(attribute =>
                        attribute.Name.LocalName.Equals("resourcetype", StringComparison.OrdinalIgnoreCase) &&
                        attribute.Value.Equals(resource, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            var first = matches.FirstOrDefault();
            foreach (var duplicate in matches.Skip(1))
                duplicate.Remove();

            var normalizedCost = NormalizeUnsignedFloat(Costs.GetValueOrDefault(resource, "0"));
            if (IsZero(normalizedCost))
            {
                first?.Remove();
                continue;
            }

            if (first == null)
            {
                first = new XElement("cost", new XAttribute("resourcetype", resource));
                var research = element.Elements().FirstOrDefault(child =>
                    child.Name.LocalName.Equals("researchpoints", StringComparison.OrdinalIgnoreCase));
                if (research != null)
                    research.AddBeforeSelf(first);
                else
                    element.Add(first);
            }
            first.Value = normalizedCost;
        }
        return element;
    }

    public static string BuildOwnedName(string fromUnit, string toUnit)
        => $"{fromUnit.Trim()}To{toUnit.Trim()}";

    public static bool IsOwnedConvention(string commandName, string fromUnit, string toUnit, string associatedTech)
    {
        var expected = BuildOwnedName(fromUnit, toUnit);
        return commandName.Equals(expected, StringComparison.OrdinalIgnoreCase) &&
               associatedTech.Equals(commandName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Recognizes an editor-owned Transform bundle from the parts whose identity does not
    /// depend on the current target. This deliberately tolerates a stale transform &lt;to&gt;
    /// value so a later save can repair an interrupted or previously incomplete rename.
    /// </summary>
    public static bool IsOwnedIdentity(string commandName, string fromUnit, string associatedTech)
    {
        var prefix = $"{fromUnit.Trim()}To";
        return !string.IsNullOrWhiteSpace(fromUnit) &&
               commandName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
               commandName.Length > prefix.Length &&
               associatedTech.Equals(commandName, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsValidUnsignedFloat(string? value)
        => decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0;

    public static bool IsValidUnsignedInteger(string? value)
        => decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) &&
           parsed >= 0 && parsed == decimal.Truncate(parsed);

    private static string NormalizeUnsignedFloat(string? value)
        => IsValidUnsignedFloat(value) ? value!.Trim() : "0";

    private static bool IsZero(string value)
        => decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && parsed == 0;

    private static void SetSingleton(XElement parent, string name, string value)
    {
        var matches = parent.Elements().Where(child =>
            child.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
        var first = matches.FirstOrDefault();
        foreach (var duplicate in matches.Skip(1))
            duplicate.Remove();
        if (first == null)
            parent.Add(new XElement(name, value));
        else
            first.Value = value;
    }
}
