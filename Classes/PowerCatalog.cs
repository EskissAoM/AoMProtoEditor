using System.Xml.Linq;
using AoMDivineDataEditor.GameData;

namespace AoMDivineDataEditor.Classes;

public sealed record PowerDefinition(string Name, bool IsBuiltIn, XElement SourceElement);

public enum PowerDocumentSection
{
    Abilities,
    Powers
}

/// <summary>
/// Shared catalog policy for game powers. Original .godpowers records are all
/// exposed; powers_mods.xml records use the same filtering rule as Technology
/// CreatePower selectors so ability-only definitions do not appear as powers.
/// </summary>
public static class PowerCatalog
{
    public const string ModFileName = "powers_mods.xml";
    public const string ModRootName = "powersmod";
    public const string EntryName = "power";

    internal static readonly string[] OwnedStringTags = ["displaynameid", "rolloverid", "activepowerrolloverid",
        "exclusiveerrormsgid", "notificationformatid", "selfnotificationformatid"];

    public static IReadOnlySet<string> CollectOwnedStringIds(XElement entry)
    {
        var name = GetName(entry);
        var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (entry.Name.Namespace != XNamespace.None || name.Length == 0) return owned;
        foreach (var field in OwnedStringFields(entry))
        {
            var id = field.Value.Trim();
            if (ReferenceEquals(field.Parent, entry) && OwnedStringTags.Contains(field.Name.LocalName, StringComparer.OrdinalIgnoreCase))
            {
                if (id.Equals(BuildStringId(name, field.Name.LocalName), StringComparison.OrdinalIgnoreCase)) owned.Add(id);
            }
            else
            {
                var kind = field.Name.LocalName.ToLowerInvariant() switch
                { "timerrolloverid" => "PROG", "displaynameid" => "NAME", "rolloverid" => "LR", "notificationmessageid" => "NOTIF", _ => "TOOLTIP" };
                var basis = SwitchingEffectsFamilyController.CreateStringId(name, kind);
                if (id.Equals(basis, StringComparison.OrdinalIgnoreCase) ||
                    (kind != "PROG" && id.StartsWith(basis + "_", StringComparison.OrdinalIgnoreCase) &&
                     int.TryParse(id[(basis.Length + 1)..], out var index) && index > 0 &&
                     index.ToString(System.Globalization.CultureInfo.InvariantCulture) == id[(basis.Length + 1)..])) owned.Add(id);
            }
        }
        return owned;
    }

    public static IReadOnlyList<OwnedStringRename> RenameOwnedStringIds(XElement entry, string oldName, string newName)
    {
        var original = new XElement(entry); SetName(original, oldName);
        var owned = CollectOwnedStringIds(original);
        var mappings = OwnedStringTags.ToDictionary(tag => BuildStringId(oldName, tag), tag => BuildStringId(newName, tag), StringComparer.OrdinalIgnoreCase);
        var oldSwitchingPrefix = SwitchingEffectsFamilyController.CreateStringId(oldName, "");
        var newSwitchingPrefix = SwitchingEffectsFamilyController.CreateStringId(newName, "");
        var changes = new List<OwnedStringRename>();
        foreach (var field in OwnedStringFields(entry))
        {
            var id = field.Value.Trim();
            if (!owned.Contains(id)) continue;
            var next = mappings.GetValueOrDefault(id) ?? (id.StartsWith(oldSwitchingPrefix, StringComparison.OrdinalIgnoreCase)
                ? newSwitchingPrefix + id[oldSwitchingPrefix.Length..] : id);
            if (id.Equals(next, StringComparison.OrdinalIgnoreCase)) continue;
            XmlTriviaPreserver.SetText(field, next);
            changes.Add(new OwnedStringRename(field.Name.LocalName, id, next));
        }
        return changes;
    }

    internal static void ApplyOwnedStringRenames(XElement entry, IEnumerable<OwnedStringRename> renames)
    {
        foreach (var rename in renames)
            foreach (var field in OwnedStringFields(entry).Where(field =>
                         field.Name.LocalName.Equals(rename.Field, StringComparison.OrdinalIgnoreCase) &&
                         field.Value.Trim().Equals(rename.OldId, StringComparison.OrdinalIgnoreCase)))
                XmlTriviaPreserver.SetText(field, rename.NewId);
    }

    private static IEnumerable<XElement> OwnedStringFields(XElement entry)
    {
        foreach (var field in entry.Elements().Where(field => field.Name.Namespace == XNamespace.None &&
                     OwnedStringTags.Contains(field.Name.LocalName, StringComparer.OrdinalIgnoreCase) && !field.HasElements)) yield return field;
        if (!SwitchingEffectsFamilyController.IsPower(entry)) yield break;
        foreach (var timer in entry.Elements().Where(field => field.Name == "timerrolloverid" && !field.HasElements)) yield return timer;
        foreach (var tier in entry.Elements().Where(field => field.Name == "effects"))
        {
            foreach (var field in tier.Elements().Where(field => field.Name.Namespace == XNamespace.None && !field.HasElements &&
                         new[] { "displaynameid", "rolloverid", "notificationmessageid" }.Contains(field.Name.LocalName, StringComparer.OrdinalIgnoreCase))) yield return field;
            foreach (var field in tier.Elements("extratooltips").Elements("string").Where(field => !field.HasElements)) yield return field;
        }
    }

    public static string BuildStringId(string powerName, string tag)
    {
        var suffix = tag.ToLowerInvariant() switch
        {
            "displaynameid" => "",
            "rolloverid" => "_LR",
            "activepowerrolloverid" => "_ACTIVE_LR",
            "notificationformatid" => "",
            "selfnotificationformatid" => "",
            "exclusiveerrormsgid" => "",
            _ => throw new ArgumentOutOfRangeException(nameof(tag), tag, "Not a Power string field.")
        };
        var prefix = tag.ToLowerInvariant() switch
        {
            "exclusiveerrormsgid" => "STR_ERROR_POWER_",
            "notificationformatid" => "STR_MSG_INVOKE_POWER_",
            "selfnotificationformatid" => "STR_MSG_SELF_INVOKE_POWER_",
            _ => "STR_POWER_"
        };
        return $"{prefix}{TechnologyStringIdPolicy.NormalizeToken(powerName)}{suffix}";
    }

    public static XElement? FindField(XElement entry, string tag)
        => entry.Elements().FirstOrDefault(element =>
            element.Name.LocalName.Equals(tag, StringComparison.OrdinalIgnoreCase));

    public static XElement SetField(XElement entry, string tag, string value)
    {
        var existing = FindField(entry, tag);
        if (existing != null)
        {
            XmlTriviaPreserver.SetText(existing, value);
            return existing;
        }

        var created = new XElement(tag, value);
        var parentIndentation = GetNodeIndent(entry) ?? "  ";
        var indentation = entry.Elements()
            .Select(GetNodeIndent)
            .FirstOrDefault(value => value != null)
            ?? parentIndentation + (parentIndentation.Contains('\t') ? "\t" : "  ");
        var trailingWhitespace = entry.Nodes().LastOrDefault() as XText;
        var nodes = entry.Nodes().ToList();
        var insertionIndex = PowerXmlFieldOrder.FindInsertionIndex(nodes, entry, created);
        if (insertionIndex >= 0 && insertionIndex < nodes.Count)
            nodes[insertionIndex].AddBeforeSelf(new XText("\n" + indentation), created);
        else if (trailingWhitespace != null && string.IsNullOrWhiteSpace(trailingWhitespace.Value))
            trailingWhitespace.AddBeforeSelf(new XText("\n" + indentation), created);
        else
            entry.Add(new XText("\n" + indentation), created, new XText("\n" + parentIndentation));
        return created;
    }

    public static XElement AddRepeatedField(XElement entry, string tag, string value)
    {
        var last = entry.Elements().LastOrDefault(element =>
            element.Name.LocalName.Equals(tag, StringComparison.OrdinalIgnoreCase));
        if (last == null)
        {
            var created = SetField(entry, tag, value);
            return created;
        }

        var createdAfterLast = new XElement(tag, value);
        var indentation = GetNodeIndent(last) ?? GetNodeIndent(entry) + "  ";
        last.AddAfterSelf(new XText("\n" + indentation), createdAfterLast);
        return createdAfterLast;
    }

    /// <summary>
    /// Collapses excess whitespace left by earlier optional-field edits inside
    /// a Power being saved. Other entries and non-whitespace nodes are untouched.
    /// </summary>
    public static void CollapseExcessiveBlankLines(XElement entry)
    {
        if (EditorXmlOrderPolicy.HasProtectedContent(entry)) return;
        foreach (var whitespace in entry.Nodes().OfType<XText>()
                     .Where(node => string.IsNullOrWhiteSpace(node.Value)).ToList())
        {
            while (whitespace.PreviousNode is XText previous &&
                   string.IsNullOrWhiteSpace(previous.Value))
                previous.Remove();
            if (whitespace.Value.Count(character => character == '\n') < 3)
                continue;

            var lastNewline = whitespace.Value.LastIndexOf('\n');
            var indentation = whitespace.Value[(lastNewline + 1)..];
            var newline = whitespace.Value.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            whitespace.Value = newline + indentation;
        }

        // The first TempUnit editor could append repeated children without their
        // indentation text node. Repair that narrowly scoped historical output.
        if (!string.Equals(entry.Attribute("type")?.Value, "TempUnit",
                StringComparison.OrdinalIgnoreCase))
            return;
        var parentIndentation = GetNodeIndent(entry) ?? "  ";
        var childIndentation = entry.Elements().Select(GetNodeIndent)
            .FirstOrDefault(value => value != null) ??
            parentIndentation + (parentIndentation.Contains('\t') ? "\t" : "  ");
        foreach (var child in entry.Elements().ToList())
        {
            if (child.PreviousNode is not XText previous || !string.IsNullOrWhiteSpace(previous.Value))
                child.AddBeforeSelf(new XText("\n" + childIndentation));
            else
            {
                var lastNewline = previous.Value.LastIndexOf('\n');
                var currentIndentation = lastNewline >= 0 ? previous.Value[(lastNewline + 1)..] : "";
                if (!currentIndentation.Equals(childIndentation, StringComparison.Ordinal))
                    previous.Value = (lastNewline >= 0 ? previous.Value[..(lastNewline + 1)] : "\n") +
                                     childIndentation;
            }
        }
        if (entry.LastNode is not XText trailing || !string.IsNullOrWhiteSpace(trailing.Value))
            entry.Add(new XText("\n" + parentIndentation));
        else
        {
            var lastNewline = trailing.Value.LastIndexOf('\n');
            var currentIndentation = lastNewline >= 0 ? trailing.Value[(lastNewline + 1)..] : "";
            if (!currentIndentation.Equals(parentIndentation, StringComparison.Ordinal))
                trailing.Value = (lastNewline >= 0 ? trailing.Value[..(lastNewline + 1)] : "\n") +
                                 parentIndentation;
        }
    }

    public static void NormalizeNestedElementIndentation(XElement container)
    {
        ArgumentNullException.ThrowIfNull(container);
        if (EditorXmlOrderPolicy.HasProtectedContent(container)) return;
        var parentIndentation = GetNodeIndent(container) ?? "    ";
        var indentUnit = parentIndentation.Contains('\t') ? "\t" : "  ";
        var childIndentation = parentIndentation + indentUnit;
        foreach (var child in container.Elements().ToList())
        {
            if (child.PreviousNode is XText previous && string.IsNullOrWhiteSpace(previous.Value))
                previous.Value = "\n" + childIndentation;
            else
                child.AddBeforeSelf(new XText("\n" + childIndentation));
        }
        if (container.LastNode is XText trailing && string.IsNullOrWhiteSpace(trailing.Value))
            trailing.Value = "\n" + parentIndentation;
        else
            container.Add(new XText("\n" + parentIndentation));
    }

    public static string GetSectionMarker(PowerDocumentSection section)
        => $"=== AoM: DDE ==  {(section == PowerDocumentSection.Abilities ? "Abilities" : "Powers")} ===";

    /// <summary>
    /// Adds only newly-created entries to an editor-owned section. Existing
    /// powers are never moved, regrouped, or otherwise normalized.
    /// </summary>
    public static void AddNewEntry(
        XDocument document,
        XElement entry,
        PowerDocumentSection section)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(entry);
        var root = document.Root ?? throw new InvalidDataException("The Powers document has no root element.");
        var indent = GetEntryIndent(root);
        var marker = GetSectionMarker(section);
        var matchingMarkers = root.Nodes().OfType<XComment>()
            .Where(comment => comment.Value.Trim().Equals(marker, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (matchingMarkers.Count > 1)
        {
            throw new InvalidDataException(
                $"The Powers section marker '<!-- {marker} -->' occurs more than once. " +
                "No entry was added because placement is ambiguous.");
        }

        if (matchingMarkers.Count == 0)
        {
            AppendNewSection(root, new XComment($" {marker} "), entry, indent);
            return;
        }

        var sectionMarker = matchingMarkers[0];
        AlignMarker(sectionMarker, indent);
        var nextSectionMarker = sectionMarker.NodesAfterSelf()
            .OfType<XComment>()
            .FirstOrDefault(IsEditorSectionMarker);
        if (nextSectionMarker != null)
        {
            var precedingWhitespace = nextSectionMarker.PreviousNode as XText;
            if (precedingWhitespace != null && string.IsNullOrWhiteSpace(precedingWhitespace.Value))
                precedingWhitespace.AddBeforeSelf(new XText("\n" + indent), entry);
            else
                nextSectionMarker.AddBeforeSelf(new XText("\n" + indent), entry, new XText("\n" + indent));
            return;
        }

        var trailingWhitespace = root.Nodes().LastOrDefault() as XText;
        if (trailingWhitespace != null && string.IsNullOrWhiteSpace(trailingWhitespace.Value))
            trailingWhitespace.AddBeforeSelf(new XText("\n" + indent), entry);
        else
            root.Add(new XText("\n" + indent), entry, new XText("\n"));
    }

    /// <summary>Formats a detached clone for display without changing source XML trivia.</summary>
    public static string FormatPreview(XElement entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (EditorXmlOrderPolicy.HasProtectedContent(entry)) return entry.ToString(SaveOptions.DisableFormatting);
        var preview = new XElement(entry);
        foreach (var whitespace in preview.DescendantsAndSelf()
                     .Where(element => element.HasElements && !EditorXmlOrderPolicy.HasProtectedContent(element))
                     .SelectMany(element => element.Nodes().OfType<XText>())
                     .Where(text => string.IsNullOrWhiteSpace(text.Value))
                     .ToList())
        {
            whitespace.Remove();
        }
        return preview.ToString();
    }

    public static IReadOnlyList<PowerDefinition> ExtractDefinitions(
        IEnumerable<XDocument> documents,
        bool isBuiltIn)
        => documents
            .SelectMany(GetPowerElements)
            .Where(power => isBuiltIn || IsModCatalogPower(power))
            .Select(power => CreateDefinition(power, isBuiltIn))
            .Where(definition => definition.Name.Length > 0)
            .GroupBy(definition => definition.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(definition => definition.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static PowerDefinition CreateDefinition(XElement power, bool isBuiltIn)
    {
        var clone = new XElement(power);
        var name = GetName(clone);
        if (isBuiltIn && name.Equals("ShiftingSands", StringComparison.OrdinalIgnoreCase))
        {
            var rangeIndicators = clone.Elements().Where(element =>
                element.Name.LocalName.Equals("rangeindicator", StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var duplicate in rangeIndicators.Take(Math.Max(0, rangeIndicators.Count - 1)))
                XmlTriviaPreserver.RemoveElementAndAdjacentWhitespace(duplicate);
        }
        return new PowerDefinition(name, isBuiltIn, clone);
    }

    public static IReadOnlyList<PowerDefinition> ExtractDefinitions(XContainer container, bool isBuiltIn)
    {
        var document = container switch
        {
            XDocument source => source,
            XElement source => new XDocument(new XElement(source)),
            _ => new XDocument()
        };
        return ExtractDefinitions([document], isBuiltIn);
    }

    public static IReadOnlyList<PowerDefinition> ExtractBaseDefinitionsFromBar(
        BarArchive? barFile,
        string? barPath)
        => ExtractDefinitions(ExtractBaseDocumentsFromBar(barFile, barPath), isBuiltIn: true);

    public static IReadOnlyList<XDocument> ExtractBaseDocumentsFromBar(
        BarArchive? barFile,
        string? barPath)
    {
        if (barFile?.Entries == null || string.IsNullOrWhiteSpace(barPath) || !File.Exists(barPath))
            return [];

        var documents = new List<XDocument>();
        var entries = barFile.Entries.Where(entry =>
        {
            var fileName = entry.Name.Replace('\\', '/').Split('/').LastOrDefault() ?? "";
            return fileName.EndsWith(".godpowers.xmb", StringComparison.OrdinalIgnoreCase);
        }).ToList();

        using var stream = File.OpenRead(barPath);
        foreach (var entry in entries)
        {
            try
            {
                var size = entry.IsCompressed ? entry.SizeUncompressed : entry.SizeInArchive;
                var bytes = new byte[size];
                var read = entry.ReadDataDecompressed(stream, bytes);
                if (read <= 0)
                    continue;
                var xml = XmbReader.ToFormattedXml(bytes.AsSpan(0, read));
                if (!string.IsNullOrWhiteSpace(xml))
                    documents.Add(XDocument.Parse(xml, LoadOptions.PreserveWhitespace));
            }
            catch
            {
                // Keep valid catalogs decoded from the other .godpowers entries.
            }
        }
        return documents;
    }

    public static IReadOnlyList<XDocument> LoadLooseBaseDocuments(string? gameplayDirectory)
    {
        var directory = string.IsNullOrWhiteSpace(gameplayDirectory)
            ? null
            : Path.Combine(gameplayDirectory, "god_powers");
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return [];

        var documents = new List<XDocument>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.godpowers", SearchOption.TopDirectoryOnly))
        {
            try
            {
                documents.Add(XDocument.Load(path, LoadOptions.PreserveWhitespace));
            }
            catch
            {
                // A malformed loose fallback must not hide valid catalog files.
            }
        }
        return documents;
    }

    public static IReadOnlyList<string> ExtractCatalogNames(
        IEnumerable<XDocument> originalDocuments,
        IEnumerable<XDocument> modDocuments)
        => ExtractDefinitions(originalDocuments, isBuiltIn: true)
            .Concat(ExtractDefinitions(modDocuments, isBuiltIn: false))
            .Select(definition => definition.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static bool IsModCatalogPower(XElement power)
        => !IsAbilityPower(power);

    public static bool IsAbilityPower(XElement power)
    {
        var type = FindAttribute(power, "type")?.Value.Trim() ?? "";
        if (type.Equals("UnitAction", StringComparison.OrdinalIgnoreCase))
            return true;
        if (!type.Equals("GeneralEffect", StringComparison.OrdinalIgnoreCase))
            return false;

        var godPower = FindAttribute(power, "godpower");
        return godPower == null || !string.IsNullOrWhiteSpace(godPower.Value);
    }

    public static string GetName(XElement entry)
    {
        var attributeName = FindAttribute(entry, "name")?.Value.Trim();
        if (!string.IsNullOrWhiteSpace(attributeName))
            return attributeName;
        var childName = entry.Elements().FirstOrDefault(element =>
            element.Name.LocalName.Equals("name", StringComparison.OrdinalIgnoreCase))?.Value.Trim();
        if (!string.IsNullOrWhiteSpace(childName))
            return childName;
        return entry.Nodes().OfType<XText>()
            .Select(text => text.Value.Trim())
            .FirstOrDefault(value => value.Length > 0) ?? "";
    }

    public static XElement? Find(XContainer container, string name)
    {
        var root = container switch
        {
            XDocument document => document.Root,
            XElement element => element,
            _ => null
        };
        return root?.Elements().FirstOrDefault(element =>
            element.Name.LocalName.Equals(EntryName, StringComparison.OrdinalIgnoreCase) &&
            GetName(element).Equals(name, StringComparison.OrdinalIgnoreCase) &&
            IsModCatalogPower(element));
    }

    public static void SetName(XElement entry, string name)
    {
        var attribute = FindAttribute(entry, "name");
        if (attribute != null)
            attribute.Value = name;
        else
            entry.SetAttributeValue("name", name);
    }

    public static XDocument LoadOrCreateModDocument(string path)
        => ModDocumentPersistence.Prepare(path, ModXmlDocumentKind.Powers).Document;

    private static IEnumerable<XElement> GetPowerElements(XDocument document)
        => document.Root?.DescendantsAndSelf().Where(element =>
               element.Name.LocalName.Equals(EntryName, StringComparison.OrdinalIgnoreCase)) ?? [];

    private static XAttribute? FindAttribute(XElement element, string name)
        => element.Attributes().FirstOrDefault(attribute =>
            attribute.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static bool IsEditorSectionMarker(XComment comment)
        => Enum.GetValues<PowerDocumentSection>().Any(section =>
            comment.Value.Trim().Equals(GetSectionMarker(section), StringComparison.OrdinalIgnoreCase));

    private static string GetEntryIndent(XElement root)
    {
        foreach (var element in root.Elements().Where(element =>
                     element.Name.LocalName.Equals(EntryName, StringComparison.OrdinalIgnoreCase)))
        {
            if (element.PreviousNode is not XText whitespace ||
                !string.IsNullOrWhiteSpace(whitespace.Value))
                continue;
            var lastNewline = whitespace.Value.LastIndexOf('\n');
            if (lastNewline >= 0 && lastNewline < whitespace.Value.Length - 1)
                return whitespace.Value[(lastNewline + 1)..];
        }
        return "  ";
    }

    private static string? GetNodeIndent(XNode node)
    {
        if (node.PreviousNode is not XText whitespace ||
            !string.IsNullOrWhiteSpace(whitespace.Value))
            return null;
        var newline = whitespace.Value.LastIndexOf('\n');
        return newline < 0 ? null : whitespace.Value[(newline + 1)..];
    }

    private static void AlignMarker(XComment marker, string indent)
    {
        if (marker.PreviousNode is not XText whitespace ||
            !string.IsNullOrWhiteSpace(whitespace.Value))
            return;
        var lastNewline = whitespace.Value.LastIndexOf('\n');
        if (lastNewline >= 0)
            whitespace.Value = whitespace.Value[..(lastNewline + 1)] + indent;
    }

    private static void AppendNewSection(XElement root, XComment marker, XElement entry, string indent)
    {
        var trailingWhitespace = root.Nodes().LastOrDefault() as XText;
        var hasContent = root.Nodes().Any(node =>
            node is not XText text || !string.IsNullOrWhiteSpace(text.Value));
        var prefix = new XText((hasContent ? "\n\n" : "\n") + indent);
        if (trailingWhitespace != null && string.IsNullOrWhiteSpace(trailingWhitespace.Value))
            trailingWhitespace.AddBeforeSelf(prefix, marker, new XText("\n" + indent), entry);
        else
            root.Add(prefix, marker, new XText("\n" + indent), entry, new XText("\n"));
    }
}
