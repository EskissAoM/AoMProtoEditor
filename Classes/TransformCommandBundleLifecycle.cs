using System.Xml.Linq;

namespace AoMDivineDataEditor.Classes;

public sealed record OwnedTransformCommandBundle(
    string CommandName,
    string TechnologyName,
    string FromUnit,
    string ToUnit);

/// <summary>
/// Pure XML lifecycle operations for transform bundles created by the inline editor.
/// A bundle is owned when its command and associated technology share the deterministic
/// FromToTarget identity. Target renames also accept a stale transform target so a save
/// can repair all three files after an interrupted or previously incomplete rename.
/// </summary>
public static class TransformCommandBundleLifecycle
{
    public static IReadOnlyList<OwnedTransformCommandBundle> FindOwnedBundles(
        XElement unit,
        XDocument commandDocument,
        XDocument transformDocument)
    {
        var unitName = unit.Attribute("name")?.Value.Trim() ?? "";
        var assigned = unit.Elements()
            .Where(element => element.Name.LocalName.Equals("command", StringComparison.OrdinalIgnoreCase) ||
                              element.Name.LocalName.Equals("transformcommand", StringComparison.OrdinalIgnoreCase))
            .Select(element => element.Value.Trim())
            .Where(value => value.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var result = new List<OwnedTransformCommandBundle>();
        foreach (var transformElement in transformDocument.Root?.Elements().Where(element =>
                     element.Name.LocalName.Equals("transform", StringComparison.OrdinalIgnoreCase)) ?? [])
        {
            var transform = ProtoUnitTransformDefinition.FromElement(transformElement);
            if (!assigned.Contains(transform.Command) ||
                !transform.From.Equals(unitName, StringComparison.OrdinalIgnoreCase))
                continue;
            var commandElement = FindCommand(commandDocument, transform.Command);
            if (commandElement == null)
                continue;
            var command = ProtoUnitCommandDefinition.FromElement(commandElement);
            var associated = command.Values.GetValueOrDefault("associatedtech", "").Trim();
            if (!TransformAssociatedTechnologyDefinition.IsOwnedIdentity(
                    transform.Command, transform.From, associated))
                continue;
            result.Add(new OwnedTransformCommandBundle(transform.Command, associated, transform.From, transform.To));
        }
        return result;
    }

    public static IReadOnlyList<OwnedTransformCommandBundle> Duplicate(
        XElement sourceUnit,
        XElement duplicatedUnit,
        string duplicatedUnitName,
        XDocument commandDocument,
        XDocument transformDocument,
        XDocument technologyDocument)
        => Duplicate(
            sourceUnit,
            duplicatedUnit,
            duplicatedUnitName,
            commandDocument,
            transformDocument,
            technologyDocument,
            commandDocument,
            transformDocument,
            technologyDocument);

    public static IReadOnlyList<OwnedTransformCommandBundle> Duplicate(
        XElement sourceUnit,
        XElement duplicatedUnit,
        string duplicatedUnitName,
        XDocument sourceCommandDocument,
        XDocument sourceTransformDocument,
        XDocument sourceTechnologyDocument,
        XDocument destinationCommandDocument,
        XDocument destinationTransformDocument,
        XDocument destinationTechnologyDocument)
    {
        var created = new List<OwnedTransformCommandBundle>();
        foreach (var source in FindOwnedBundles(sourceUnit, sourceCommandDocument, sourceTransformDocument))
        {
            var newName = TransformAssociatedTechnologyDefinition.BuildOwnedName(duplicatedUnitName, source.ToUnit);
            EnsureNamesAvailable(newName, destinationCommandDocument, destinationTechnologyDocument);

            var command = ProtoUnitCommandDefinition.FromElement(FindCommand(sourceCommandDocument, source.CommandName)!);
            command.Name = newName;
            command.Values["associatedtech"] = newName;
            destinationCommandDocument.Root!.Add(command.ToElement());

            var sourceTransform = FindTransform(sourceTransformDocument, source.CommandName)!;
            var transform = ProtoUnitTransformDefinition.FromElement(sourceTransform);
            transform.From = duplicatedUnitName;
            transform.Command = newName;
            destinationTransformDocument.Root!.Add(transform.ToElement());

            var technology = FindTechnology(sourceTechnologyDocument, source.TechnologyName)
                ?? throw new InvalidOperationException(
                    $"Owned Transform technology '{source.TechnologyName}' could not be duplicated because it could not be found.");
            var technologyCopy = TransformAssociatedTechnologyDefinition.FromElement(technology);
            technologyCopy.Name = newName;
            destinationTechnologyDocument.Root!.Add(technologyCopy.ToElement());

            ReplaceAssignment(duplicatedUnit, source.CommandName, newName);
            created.Add(new OwnedTransformCommandBundle(newName, newName, duplicatedUnitName, source.ToUnit));
        }
        return created;
    }

    public static IReadOnlyList<(OwnedTransformCommandBundle Old, OwnedTransformCommandBundle New)> RenameUnit(
        XElement renamedUnit,
        string oldUnitName,
        string newUnitName,
        XDocument commandDocument,
        XDocument transformDocument,
        XDocument technologyDocument)
    {
        // The unit element already carries the new name, while transforms still carry the old one.
        var lookupUnit = new XElement(renamedUnit);
        lookupUnit.SetAttributeValue("name", oldUnitName);
        var renamed = new List<(OwnedTransformCommandBundle, OwnedTransformCommandBundle)>();
        foreach (var oldBundle in FindOwnedBundles(lookupUnit, commandDocument, transformDocument))
        {
            var newName = TransformAssociatedTechnologyDefinition.BuildOwnedName(newUnitName, oldBundle.ToUnit);
            EnsureNamesAvailable(newName, commandDocument, technologyDocument, oldBundle.CommandName, oldBundle.TechnologyName);

            var commandElement = FindCommand(commandDocument, oldBundle.CommandName)!;
            var command = ProtoUnitCommandDefinition.FromElement(commandElement);
            command.Name = newName;
            command.Values["associatedtech"] = newName;
            commandElement.ReplaceWith(command.ToElement());

            var transformElement = FindTransform(transformDocument, oldBundle.CommandName)!;
            var transform = ProtoUnitTransformDefinition.FromElement(transformElement);
            transform.From = newUnitName;
            transform.Command = newName;
            transformElement.ReplaceWith(transform.ToElement());

            var technologyElement = FindTechnology(technologyDocument, oldBundle.TechnologyName)
                ?? throw new InvalidOperationException(
                    $"Owned Transform technology '{oldBundle.TechnologyName}' could not be renamed because it is not present in techtree_mods.xml.");
            var technology = TransformAssociatedTechnologyDefinition.FromElement(technologyElement);
            technology.Name = newName;
            technologyElement.ReplaceWith(technology.ToElement());

            ReplaceAssignment(renamedUnit, oldBundle.CommandName, newName);
            renamed.Add((oldBundle, new OwnedTransformCommandBundle(newName, newName, newUnitName, oldBundle.ToUnit)));
        }
        return renamed;
    }

    public static (OwnedTransformCommandBundle Old, OwnedTransformCommandBundle New) RenameTarget(
        string commandName,
        string newToUnit,
        XDocument commandDocument,
        XDocument transformDocument,
        XDocument technologyDocument)
    {
        var commandElement = FindCommand(commandDocument, commandName)
            ?? throw new InvalidOperationException($"Owned Transform command '{commandName}' could not be found.");
        var transformElement = FindTransform(transformDocument, commandName)
            ?? throw new InvalidOperationException($"Owned Transform definition '{commandName}' could not be found.");
        var command = ProtoUnitCommandDefinition.FromElement(commandElement);
        var transform = ProtoUnitTransformDefinition.FromElement(transformElement);
        var technologyName = command.Values.GetValueOrDefault("associatedtech", "").Trim();
        if (!TransformAssociatedTechnologyDefinition.IsOwnedIdentity(
                commandName, transform.From, technologyName))
        {
            throw new InvalidOperationException(
                $"'{commandName}' does not own a command/transform/technology bundle and cannot be renamed automatically.");
        }

        var newName = TransformAssociatedTechnologyDefinition.BuildOwnedName(transform.From, newToUnit);
        EnsureNamesAvailable(newName, commandDocument, technologyDocument, commandName, technologyName);
        var technologyElement = FindTechnology(technologyDocument, technologyName)
            ?? throw new InvalidOperationException(
                $"Owned Transform technology '{technologyName}' could not be renamed because it is not present in techtree_mods.xml.");

        var oldBundle = new OwnedTransformCommandBundle(commandName, technologyName, transform.From, transform.To);
        command.Name = newName;
        command.Values["associatedtech"] = newName;
        commandElement.ReplaceWith(command.ToElement());

        transform.To = newToUnit.Trim();
        transform.Command = newName;
        transformElement.ReplaceWith(transform.ToElement());

        var technology = TransformAssociatedTechnologyDefinition.FromElement(technologyElement);
        technology.Name = newName;
        technologyElement.ReplaceWith(technology.ToElement());

        return (oldBundle, new OwnedTransformCommandBundle(newName, newName, transform.From, transform.To));
    }

    public static void Delete(
        IEnumerable<OwnedTransformCommandBundle> bundles,
        XDocument commandDocument,
        XDocument transformDocument,
        XDocument technologyDocument)
    {
        foreach (var bundle in bundles)
        {
            FindCommand(commandDocument, bundle.CommandName)?.Remove();
            FindTransform(transformDocument, bundle.CommandName)?.Remove();
            if (!string.IsNullOrWhiteSpace(bundle.TechnologyName))
                FindTechnology(technologyDocument, bundle.TechnologyName)?.Remove();
        }
    }

    private static void ReplaceAssignment(XElement unit, string oldName, string newName)
    {
        foreach (var element in unit.Elements().Where(element =>
                     (element.Name.LocalName.Equals("command", StringComparison.OrdinalIgnoreCase) ||
                      element.Name.LocalName.Equals("transformcommand", StringComparison.OrdinalIgnoreCase)) &&
                     element.Value.Trim().Equals(oldName, StringComparison.OrdinalIgnoreCase)))
            element.Value = newName;
    }

    private static void EnsureNamesAvailable(
        string name,
        XDocument commandDocument,
        XDocument technologyDocument,
        string? allowedCommand = null,
        string? allowedTechnology = null)
    {
        var command = FindCommand(commandDocument, name);
        if (command != null && !name.Equals(allowedCommand, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Command '{name}' already exists.");
        var technology = FindTechnology(technologyDocument, name);
        if (technology != null && !name.Equals(allowedTechnology, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Technology '{name}' already exists.");
    }

    private static XElement? FindCommand(XDocument document, string name)
        => document.Root?.Elements().FirstOrDefault(element =>
            element.Name.LocalName.Equals("protounitcommand", StringComparison.OrdinalIgnoreCase) &&
            element.Elements().Any(child =>
                child.Name.LocalName.Equals("name", StringComparison.OrdinalIgnoreCase) &&
                child.Value.Trim().Equals(name, StringComparison.OrdinalIgnoreCase)));

    private static XElement? FindTransform(XDocument document, string commandName)
        => document.Root?.Elements().FirstOrDefault(element =>
            element.Name.LocalName.Equals("transform", StringComparison.OrdinalIgnoreCase) &&
            element.Elements().Any(child =>
                child.Name.LocalName.Equals("command", StringComparison.OrdinalIgnoreCase) &&
                child.Value.Trim().Equals(commandName, StringComparison.OrdinalIgnoreCase)));

    private static XElement? FindTechnology(XDocument document, string name)
        => document.Root?.Elements().FirstOrDefault(element =>
            element.Name.LocalName.Equals("tech", StringComparison.OrdinalIgnoreCase) &&
            element.Attributes().Any(attribute =>
                attribute.Name.LocalName.Equals("name", StringComparison.OrdinalIgnoreCase) &&
                attribute.Value.Trim().Equals(name, StringComparison.OrdinalIgnoreCase)));
}
