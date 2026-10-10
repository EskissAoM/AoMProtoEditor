using System.Xml;
using System.Xml.Linq;

namespace AoMDivineDataEditor.Classes;

public enum ModXmlDocumentKind
{
    ProtoUnits,
    Abilities,
    Powers,
    ProtoUnitCommands,
    UnitTransforms,
    Tactics,
    UnitTypes,
    TechTypes,
    Cultures,
    MajorGods,
    MinorGods,
}

public enum ModXmlValidationSeverity
{
    Warning,
    Error,
}

public sealed record ModXmlValidationIssue(
    ModXmlValidationSeverity Severity,
    string ObjectName,
    string Reason);

public sealed record ModXmlDocumentValidation(IReadOnlyList<ModXmlValidationIssue> Issues)
{
    public IReadOnlyList<ModXmlValidationIssue> Errors => Issues
        .Where(issue => issue.Severity == ModXmlValidationSeverity.Error)
        .ToList();

    public IReadOnlyList<ModXmlValidationIssue> Warnings => Issues
        .Where(issue => issue.Severity == ModXmlValidationSeverity.Warning)
        .ToList();

    public bool IsSafeToEdit => Errors.Count == 0;

    public string ToDisplayMessage(string path)
    {
        var details = Issues.Select(issue =>
            $"{issue.Severity}: {issue.ObjectName}: {issue.Reason}");
        return $"'{Path.GetFullPath(path)}' cannot be edited safely and was not changed.{Environment.NewLine}" +
               string.Join(Environment.NewLine, details);
    }
}

public sealed record SafeModXmlLoadResult(
    XDocument Document,
    ModXmlDocumentValidation Validation,
    FileIntegrityTransaction.FileState FileState);

/// <summary>
/// Loads user-owned XML without repairing or normalizing malformed input, then
/// rejects only structures whose ownership would be ambiguous to an editor.
/// Unknown elements are preserved and reported as non-blocking warnings.
/// </summary>
public static class ModXmlDocumentSafety
{
    private const long MaximumEditableXmlBytes = 256L * 1024 * 1024;
    private static readonly StringComparer NameComparer = StringComparer.OrdinalIgnoreCase;

    private static readonly IReadOnlyDictionary<ModXmlDocumentKind, string> ExpectedRoots =
        new Dictionary<ModXmlDocumentKind, string>
        {
            [ModXmlDocumentKind.ProtoUnits] = "protomods",
            [ModXmlDocumentKind.Abilities] = "abilitiesmods",
            [ModXmlDocumentKind.Powers] = "powersmod",
            [ModXmlDocumentKind.ProtoUnitCommands] = "protounitcommandmods",
            [ModXmlDocumentKind.UnitTransforms] = "unittransformmods",
            [ModXmlDocumentKind.Tactics] = "tactics",
            [ModXmlDocumentKind.UnitTypes] = "abstractunittypesmods",
            [ModXmlDocumentKind.TechTypes] = "techtypesmods",
            [ModXmlDocumentKind.Cultures] = "culturesmods",
            [ModXmlDocumentKind.MajorGods] = "civsmods",
            [ModXmlDocumentKind.MinorGods] = "minorgodsmods",
        };

    private static readonly HashSet<string> ProtoSingletonElements = new(NameComparer)
    {
        "displaynameid", "editornameid", "rollovertextid", "shortrollovertextid",
        "tactics", "movementtype", "maxvelocity", "maxrunvelocity", "initialhitpoints",
        "maxhitpoints", "populationcount", "buildpoints", "trainpoints", "los",
        "maxrange", "obstructionradiusx", "obstructionradiusz", "unitregenrate",
        "deathmatchbuildlimit", "placementfile", "pathabilityflags", "minimapicon",
        "resourcesubtype", "transformcommand"
    };

    private static readonly HashSet<string> ProtoActionSingletonElements = new(NameComparer)
    {
        "name", "type", "rof", "maxrange", "minrange", "accuracy", "trackrating",
        "numberbounces", "recharge", "rechargetime"
    };

    private static readonly HashSet<string> CommandSingletonElements = new(NameComparer)
    {
        "name", "type", "displaynameid", "rollovertextid", "shortrollovertextid",
        "activerollovertextid", "disabledrollovertextid", "buildlimittextid", "icon",
        "cursor", "associatedtech", "prereqcommand", "transformtype"
    };

    private static readonly HashSet<string> AbilityAssociationSingletonElements = new(NameComparer)
    {
        "tech", "rof", "columnoverride", "alwaysdisabledingrid", "chargetimeasrof",
        "noautocast", "castonself", "actioncommand", "auxchargetimeasrof", "notcastable",
        "donotforcestop", "bindtochargeaction", "bindtoauxchargeaction", "enabledduringaction",
        "inactivewhenfullhp"
    };

    private static readonly HashSet<string> PowerSingletonElements = new(NameComparer)
    {
        "icon", "displaynameid", "rolloverid", "placement",
        "unitaction", "rangeindicator", "cursortype", "activetime", "radius",
        "replacerolloverid", "replaceicon",
        "allowduringnorush", "onlyfromstealth", "notblockable"
    };

    private static readonly HashSet<string> CultureSingletonElements = new(NameComparer)
    {
        "name", "displaynameid", "compendiumimage", "compendiumtopic", "replaypanelbackground"
    };

    private static readonly HashSet<string> MajorGodSingletonElements = new(NameComparer)
    {
        "name", "statsid", "key", "culture", "displaynameid", "rollovernameid", "titleid",
        "agetech", "deathmatchtech", "teamtech", "empirewarstech", "icon", "portrait",
        "breakoutportrait", "minimapborderimage", "backgroundscenario", "buildingefficiency",
        "extrabuildingefficiency", "buildingrepairfactor", "secondaryprayerefficiency",
        "partisans", "devotioncommands", "shades", "terraincreeps", "bonusunitspawning",
        "bountyresourceearning", "timeshifting", "buildingchain", "combatxp",
        "oncastpowercostfactor"
    };

    private static readonly HashSet<string> AgeTechSingletonElements = new(NameComparer)
    {
        "tech"
    };

    private static readonly HashSet<string> ShadesSingletonElements = new(NameComparer)
    {
        "protounit", "chance", "delay", "maxqueued"
    };

    private static readonly HashSet<string> BuildingChainSingletonElements = new(NameComparer)
    {
        "anchor", "abundancevfx", "autobuildvfx", "resourcegeneration"
    };

    private static readonly HashSet<string> BountySingletonElements = new(NameComparer)
    {
        "active", "bountydamagegoal", "bountyreductionstart", "bountyreductionend",
        "bountyreductionstrength", "vfx"
    };

    private static readonly HashSet<string> BonusGoalSingletonElements = new(NameComparer)
    {
        "active", "goalamount", "spawnlocation", "displayonhud", "displayinfo",
        "forcespawn", "agerestricted", "culturerestricted", "archaicspawn",
        "currentageonly", "nextageonly", "ignoreageroll"
    };

    private static readonly HashSet<string> BonusDisplayInfoSingletonElements = new(NameComparer)
    {
        "displaynameid", "rolloverid", "progresstype", "icon", "progresstooltipid",
        "nextunittooltipid", "notificationsound", "notificationmessageid"
    };

    public static SafeModXmlLoadResult LoadExisting(
        string path,
        ModXmlDocumentKind kind,
        bool allowUnsafeStructureForReadOnly = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("The XML file does not exist.", fullPath);

        try
        {
            var before = FileIntegrityTransaction.CaptureState(fullPath);
            if (before.Length > MaximumEditableXmlBytes)
            {
                throw new IOException(
                    $"The XML file is larger than {MaximumEditableXmlBytes / (1024 * 1024):N0} MiB.");
            }
            XDocument document;
            using (var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var reader = XmlReader.Create(stream, new XmlReaderSettings
                   {
                       DtdProcessing = DtdProcessing.Prohibit,
                       XmlResolver = null,
                       MaxCharactersInDocument = MaximumEditableXmlBytes * 2,
                       IgnoreComments = false,
                       IgnoreWhitespace = false
                   }))
                document = XDocument.Load(reader, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
            var after = FileIntegrityTransaction.CaptureState(fullPath);
            if (before != after)
                throw new IOException("The file changed while it was being read. Open it again to load one consistent version.");

            var validation = Validate(document, kind);
            if (!validation.IsSafeToEdit && !allowUnsafeStructureForReadOnly)
                throw new InvalidDataException(validation.ToDisplayMessage(fullPath));
            return new SafeModXmlLoadResult(document, validation, after);
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is XmlException or IOException or UnauthorizedAccessException)
        {
            throw new InvalidDataException(
                $"'{fullPath}' cannot be edited safely and was not changed. " +
                $"The XML could not be read exactly as declared: {exception.Message}", exception);
        }
    }

    public static SafeModXmlLoadResult LoadOrCreate(string path, ModXmlDocumentKind kind)
    {
        if (File.Exists(path))
            return LoadExisting(path, kind);

        var document = new XDocument(new XElement(ExpectedRoots[kind]));
        return new SafeModXmlLoadResult(
            document,
            Validate(document, kind),
            FileIntegrityTransaction.CaptureState(path));
    }

    public static ModXmlDocumentValidation Validate(XDocument document, ModXmlDocumentKind kind)
    {
        ArgumentNullException.ThrowIfNull(document);
        var issues = new List<ModXmlValidationIssue>();
        var root = document.Root;
        var expectedRoot = ExpectedRoots[kind];
        if (root == null || !IsNamed(root, expectedRoot))
        {
            AddError(issues, "Document", $"Expected a <{expectedRoot}> root element.");
            return new ModXmlDocumentValidation(issues);
        }

        switch (kind)
        {
            case ModXmlDocumentKind.ProtoUnits:
                ValidateNamedAttributeEntries(root, "unit", "unit", ProtoSingletonElements, issues, ValidateProtoUnit);
                WarnUnknownRootChildren(root, ["unit"], issues);
                break;
            case ModXmlDocumentKind.Abilities:
                ValidateAbilityAssociations(root, issues);
                break;
            case ModXmlDocumentKind.Powers:
                ValidateNamedAttributeEntries(root, "power", "power", PowerSingletonElements, issues);
                WarnUnknownRootChildren(root, ["power"], issues);
                break;
            case ModXmlDocumentKind.ProtoUnitCommands:
                ValidateNamedChildEntries(root, "protounitcommand", "command", "name", CommandSingletonElements, issues);
                WarnUnknownRootChildren(root, ["protounitcommand"], issues);
                break;
            case ModXmlDocumentKind.UnitTransforms:
                ValidateNamedChildEntries(root, "transform", "transform", "command", new HashSet<string>(NameComparer) { "command" }, issues);
                WarnUnknownRootChildren(root, ["transform"], issues);
                break;
            case ModXmlDocumentKind.Tactics:
                ValidateTactics(root, issues);
                break;
            case ModXmlDocumentKind.UnitTypes:
                ValidateTextEntries(root, "abstractunittype", "unit type", issues);
                WarnUnknownRootChildren(root, ["abstractunittype"], issues);
                break;
            case ModXmlDocumentKind.TechTypes:
                ValidateTextEntries(root, "techtype", "technology type", issues);
                WarnUnknownRootChildren(root, ["techtype"], issues);
                break;
            case ModXmlDocumentKind.Cultures:
                ValidateNamedChildEntries(root, "culture", "culture", "name", CultureSingletonElements, issues);
                WarnUnknownRootChildren(root, ["culture"], issues);
                break;
            case ModXmlDocumentKind.MajorGods:
                ValidateNamedChildEntries(root, "civ", "major god", "name", MajorGodSingletonElements, issues, ValidateMajorGod);
                WarnUnknownRootChildren(root, ["civ"], issues);
                break;
            case ModXmlDocumentKind.MinorGods:
                AddDuplicateNames(issues, "minor god", root.Elements().Select(element => element.Name.LocalName));
                AddDuplicateNames(
                    issues,
                    "minor god display name",
                    root.Elements().Select(element => MinorGodCatalog.GetDisplayName(element.Name.LocalName)));
                foreach (var minorGod in root.Elements())
                {
                    ValidateDuplicateAttributes(
                        minorGod,
                        $"Minor god '{MinorGodCatalog.GetDisplayName(minorGod.Name.LocalName)}'",
                        [MinorGodCatalog.PortraitAttribute, MinorGodCatalog.IconAttribute],
                        issues);
                }
                break;
        }

        return new ModXmlDocumentValidation(issues.Distinct().ToList());
    }

    private static void ValidateProtoUnit(XElement unit, string name, List<ModXmlValidationIssue> issues)
    {
        ValidateDuplicateSingletons(unit, name, ProtoSingletonElements, issues);
        ValidateDuplicateKeyedChildren(unit, name, "cost", "resourcetype", issues);

        var directActions = unit.Elements().Where(element => IsNamed(element, "protoaction")).ToList();
        var nestedActions = unit.Descendants().Where(element => IsNamed(element, "protoaction")).Except(directActions).ToList();
        if (nestedActions.Count > 0)
            AddError(issues, $"Unit '{name}'", $"contains {nestedActions.Count} nested <protoaction> definition(s).");

        var namedActions = new List<string>();
        foreach (var action in directActions)
        {
            var names = action.Elements().Where(element => IsNamed(element, "name")).ToList();
            if (names.Count != 1 || string.IsNullOrWhiteSpace(names[0].Value))
                AddError(issues, $"Unit '{name}'", "contains a <protoaction> without exactly one non-empty <name>.");
            else
                namedActions.Add(names[0].Value.Trim());
            ValidateDuplicateSingletons(action, $"Unit '{name}' action '{names.FirstOrDefault()?.Value.Trim() ?? "(unnamed)"}'", ProtoActionSingletonElements, issues);
        }

        AddDuplicateNames(issues, $"Unit '{name}' action", namedActions);
    }

    private static void ValidateMajorGod(XElement entry, string name, List<ModXmlValidationIssue> issues)
    {
        var owner = $"Major god '{name}'";
        if (FindDirect(entry, "agetech") is { } ageTech)
        {
            ValidateDuplicateSingletons(ageTech, $"{owner} Age Tech", AgeTechSingletonElements, issues);
            ValidateDuplicateAttributes(ageTech, $"{owner} Age Tech", ["age"], issues);
        }
        if (FindDirect(entry, "secondaryprayerefficiency") is { } prayer)
            ValidateDuplicateAttributes(prayer, $"{owner} Secondary Prayer Efficiency", ["g", "v", "z"], issues);
        if (FindDirect(entry, "shades") is { } shades)
            ValidateDuplicateSingletons(shades, $"{owner} Shades", ShadesSingletonElements, issues);
        if (FindDirect(entry, "buildingchain") is { } buildingChain)
            ValidateDuplicateSingletons(buildingChain, $"{owner} BuildingChain", BuildingChainSingletonElements, issues);
        if (FindDirect(entry, "bountyresourceearning") is { } bounty)
            ValidateDuplicateSingletons(bounty, $"{owner} BountyResourceEarning", BountySingletonElements, issues);

        ValidateModeGroups(entry, owner, "startingunits", issues);
        ValidateModeGroups(entry, owner, "startingqueue", issues);
        ValidateModeGroups(entry, owner, "startingresources", issues);
        foreach (var group in Direct(entry, "startingunits").Concat(Direct(entry, "startingqueue")))
        {
            ValidateDuplicateAttributes(group, $"{owner} <{group.Name.LocalName}>", ["mode"], issues);
            foreach (var unit in Direct(group, "unit"))
            {
                ValidateDuplicateAttributes(
                    unit,
                    $"{owner} <{group.Name.LocalName}> unit '{GetDirectText(unit)}'",
                    ["count", "delay", "x", "y", "z", "autotrain", "prequeue"],
                    issues);
            }
        }
        foreach (var resources in Direct(entry, "startingresources"))
        {
            ValidateDuplicateAttributes(resources, $"{owner} <startingresources>", ["mode"], issues);
            foreach (var duplicate in resources.Elements()
                         .GroupBy(element => element.Name.LocalName, NameComparer)
                         .Where(group => group.Count() > 1))
            {
                AddError(
                    issues,
                    $"{owner} Starting Resources '{GetMode(resources)}'",
                    $"contains {duplicate.Count()} <{duplicate.Key}> entries; a resource can occur only once per mode.");
            }
        }

        if (FindDirect(entry, "bonusunitspawning") is { } bonus)
        {
            var goals = bonus.Elements().Where(IsBonusGoal).ToList();
            AddDuplicateNames(
                issues,
                $"{owner} BonusUnitSpawning goal",
                goals.Select(goal => GetAttribute(goal, "name")).Where(value => value.Length > 0));
            foreach (var goal in goals)
            {
                var goalName = GetAttribute(goal, "name");
                var goalOwner = $"{owner} BonusUnitSpawning goal '{(goalName.Length == 0 ? "(unnamed)" : goalName)}'";
                ValidateDuplicateSingletons(goal, goalOwner, BonusGoalSingletonElements, issues);
                ValidateDuplicateAttributes(goal, goalOwner, ["name", "rewardtracking"], issues);
                foreach (var spawn in Direct(goal, "spawnlocation"))
                    ValidateDuplicateAttributes(spawn, $"{goalOwner} Spawn Location", ["type", "landbuilding", "waterbuilding"], issues);
                foreach (var contributor in Direct(goal, "contributor"))
                    ValidateDuplicateAttributes(contributor, $"{goalOwner} Contributor", ["unit", "type", "resource"], issues);
                foreach (var cost in Direct(goal, "rewardpointcost"))
                    ValidateDuplicateAttributes(cost, $"{goalOwner} Reward Point Cost", ["resourcetype"], issues);
                ValidateDuplicateKeyedChildren(goal, goalOwner, "rewardpointcost", "resourcetype", issues);
                if (FindDirect(goal, "displayinfo") is { } displayInfo)
                    ValidateDuplicateSingletons(displayInfo, $"{goalOwner} Display info", BonusDisplayInfoSingletonElements, issues);
            }
        }

        if (FindDirect(entry, "terraincreeps") is { } terrainCreeps)
        {
            foreach (var creep in Direct(terrainCreeps, "terraincreep"))
            {
                ValidateDuplicateAttributes(
                    creep,
                    $"{owner} Terrain Creep",
                    ["creep", "maxradius", "growthrate", "decayrate", "strength", "minupdateinterval",
                     "maxupdateinterval", "avoidunbuildable", "avoidimpassable", "avoidshallowwater",
                     "allowdisconnectedgrowth", "buildability", "preventplacement"],
                    issues);
            }
        }

        if (FindDirect(entry, "timeshifting") is { } timeShifting)
        {
            ValidateDuplicateAttributes(timeShifting, $"{owner} TimeShifting", ["maxconcurrenttimeshifts"], issues);
            foreach (var unit in Direct(timeShifting, "protounit"))
                ValidateDuplicateAttributes(unit, $"{owner} TimeShifting unit '{GetDirectText(unit)}'", ["costratio", "timeratio"], issues);
        }

        if (FindDirect(entry, "buildingchain") is { } chain)
        {
            if (FindDirect(chain, "anchor") is { } anchor)
                ValidateDuplicateAttributes(anchor, $"{owner} BuildingChain Anchor", ["vfx"], issues);
            foreach (var vfx in Direct(chain, "abundancevfx").Concat(Direct(chain, "autobuildvfx")))
                ValidateDuplicateAttributes(vfx, $"{owner} BuildingChain <{vfx.Name.LocalName}>", ["small", "medium", "large"], issues);
            foreach (var generation in Direct(chain, "resourcegeneration"))
            foreach (var tier in Direct(generation, "tilerateperminutetier"))
                ValidateDuplicateAttributes(tier, $"{owner} BuildingChain resource tier", ["resource", "mintiles"], issues);
            foreach (var building in Direct(chain, "chainablebuilding"))
                ValidateDuplicateAttributes(building, $"{owner} Chainable Building '{GetDirectText(building)}'", ["chainradius", "generationresource"], issues);
        }

        if (FindDirect(entry, "bountyresourceearning") is { } bountySystem)
        {
            foreach (var reward in Direct(bountySystem, "bountyreward"))
                ValidateDuplicateAttributes(reward, $"{owner} Bounty Reward", ["condition", "protounit", "unittype", "resourcetype", "combatxp", "multiplybyunitcost", "asspawnedunit"], issues);
            foreach (var multiplier in Direct(bountySystem, "bountytargetmultiplier"))
                ValidateDuplicateAttributes(multiplier, $"{owner} Bounty Target Multiplier", ["condition", "protounit", "unittype", "resourcetype", "combatxp", "attackertype", "relativity"], issues);
            foreach (var research in Direct(bountySystem, "researchreward").Concat(Direct(bountySystem, "researchcostmultiplier")))
                ValidateDuplicateAttributes(research, $"{owner} Bounty <{research.Name.LocalName}>", ["techtype", "resourcetype", "combatxp"], issues);
        }
    }

    private static void ValidateModeGroups(
        XElement entry,
        string owner,
        string groupName,
        List<ModXmlValidationIssue> issues)
    {
        foreach (var duplicate in Direct(entry, groupName)
                     .GroupBy(GetMode, NameComparer)
                     .Where(group => group.Count() > 1))
        {
            AddError(
                issues,
                owner,
                $"contains {duplicate.Count()} <{groupName}> groups for mode '{duplicate.Key}'.");
        }
    }

    private static string GetMode(XElement group)
    {
        var mode = GetAttribute(group, "mode");
        return mode.Length == 0 ? "supremacy" : mode;
    }

    private static IEnumerable<XElement> Direct(XElement owner, string name)
        => owner.Elements().Where(element => IsNamed(element, name));

    private static XElement? FindDirect(XElement owner, string name)
        => Direct(owner, name).FirstOrDefault();

    private static string GetAttribute(XElement element, string name)
        => element.Attributes().FirstOrDefault(attribute =>
            attribute.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value.Trim() ?? "";

    private static bool IsBonusGoal(XElement element)
        => element.Name.LocalName.Equals("damagegoal", StringComparison.OrdinalIgnoreCase) ||
           element.Name.LocalName.Equals("deathgoal", StringComparison.OrdinalIgnoreCase) ||
           element.Name.LocalName.Equals("resourcegoal", StringComparison.OrdinalIgnoreCase);

    private static void ValidateAbilityAssociations(XElement root, List<ModXmlValidationIssue> issues)
    {
        var wrappers = root.Elements().ToList();
        AddDuplicateNames(issues, "Ability unit", wrappers.Select(wrapper => wrapper.Name.LocalName));
        foreach (var wrapper in wrappers)
        {
            var directAbilities = wrapper.Elements().Where(element => IsNamed(element, "ability")).ToList();
            var nestedAbilities = wrapper.Descendants().Where(element => IsNamed(element, "ability")).Except(directAbilities).ToList();
            if (nestedAbilities.Count > 0)
                AddError(issues, $"Ability unit '{wrapper.Name.LocalName}'", $"contains {nestedAbilities.Count} nested <ability> entry or entries.");

            var names = directAbilities.Select(GetElementIdentifier).Where(name => name.Length > 0).ToList();
            if (names.Count != directAbilities.Count)
                AddError(issues, $"Ability unit '{wrapper.Name.LocalName}'", "contains an <ability> without a non-empty name or value.");
            foreach (var ability in directAbilities)
            {
                ValidateDuplicateSingletons(
                    ability,
                    $"Ability unit '{wrapper.Name.LocalName}' ability '{GetElementIdentifier(ability)}'",
                    AbilityAssociationSingletonElements,
                    issues);
            }
            AddDuplicateNames(issues, $"Ability unit '{wrapper.Name.LocalName}' ability", names);
        }
    }

    private static void ValidateTactics(XElement root, List<ModXmlValidationIssue> issues)
    {
        var directActions = root.Elements().Where(element => IsNamed(element, "action")).ToList();
        var namedNestedActions = root.Descendants()
            .Where(element => IsNamed(element, "action") && !directActions.Contains(element) &&
                              element.Elements().Any(child => IsNamed(child, "name") && !string.IsNullOrWhiteSpace(child.Value)))
            .ToList();
        if (namedNestedActions.Count > 0)
            AddError(issues, "Tactics", $"contains {namedNestedActions.Count} named <action> definition(s) nested outside the root action list.");

        var actionNames = new List<string>();
        foreach (var action in directActions)
        {
            var names = action.Elements().Where(element => IsNamed(element, "name")).ToList();
            if (names.Count != 1 || string.IsNullOrWhiteSpace(names[0].Value))
                AddError(issues, "Tactics action", "contains a root <action> without exactly one non-empty <name>.");
            else
                actionNames.Add(names[0].Value.Trim());
            ValidateDuplicateSingletons(action, $"Tactics action '{names.FirstOrDefault()?.Value.Trim() ?? "(unnamed)"}'", ProtoActionSingletonElements, issues);
        }
        AddDuplicateNames(issues, "Tactics action", actionNames);

        var tactics = root.Elements().Where(element => IsNamed(element, "tactic")).ToList();
        var tacticNames = tactics.Select(GetDirectText).Where(value => value.Length > 0).ToList();
        if (tacticNames.Count != tactics.Count)
            AddError(issues, "Tactics", "contains a root <tactic> without a non-empty direct text name.");
        AddDuplicateNames(issues, "Tactic", tacticNames);
        WarnUnknownRootChildren(root, ["action", "tactic"], issues);
    }

    private static void ValidateNamedAttributeEntries(
        XElement root,
        string entryName,
        string objectKind,
        HashSet<string>? singletonElements,
        List<ModXmlValidationIssue> issues,
        Action<XElement, string, List<ModXmlValidationIssue>>? additionalValidation = null)
    {
        var direct = root.Elements().Where(element => IsNamed(element, entryName)).ToList();
        var nested = root.Descendants()
            .Where(element => IsNamed(element, entryName) &&
                              element.Attributes().Any(attribute =>
                                  attribute.Name.LocalName.Equals("name", StringComparison.OrdinalIgnoreCase) &&
                                  !string.IsNullOrWhiteSpace(attribute.Value)))
            .Except(direct)
            .ToList();
        if (nested.Count > 0)
            AddError(issues, "Document", $"Found {nested.Count} <{entryName}> definition(s) outside the root list.");

        var names = new List<string>();
        foreach (var entry in direct)
        {
            var matchingAttributes = entry.Attributes().Where(attribute =>
                attribute.Name.LocalName.Equals("name", StringComparison.OrdinalIgnoreCase)).ToList();
            var name = matchingAttributes.FirstOrDefault()?.Value.Trim() ?? "";
            if (matchingAttributes.Count != 1 || name.Length == 0)
                AddError(issues, $"{objectKind} entry", "does not have exactly one non-empty name attribute.");
            else
                names.Add(name);
            if (singletonElements != null)
                ValidateDuplicateSingletons(entry, $"{objectKind} '{name}'", singletonElements, issues);
            additionalValidation?.Invoke(entry, name.Length == 0 ? "(unnamed)" : name, issues);
        }
        AddDuplicateNames(issues, objectKind, names);
    }

    private static void ValidateNamedChildEntries(
        XElement root,
        string entryName,
        string objectKind,
        string identifierElement,
        HashSet<string> singletonElements,
        List<ModXmlValidationIssue> issues,
        Action<XElement, string, List<ModXmlValidationIssue>>? additionalValidation = null)
    {
        var direct = root.Elements().Where(element => IsNamed(element, entryName)).ToList();
        var nested = root.Descendants().Where(element => IsNamed(element, entryName)).Except(direct).ToList();
        if (nested.Count > 0)
            AddError(issues, "Document", $"Found {nested.Count} <{entryName}> definition(s) outside the root list.");
        var names = new List<string>();
        foreach (var entry in direct)
        {
            var identifiers = entry.Elements().Where(element => IsNamed(element, identifierElement)).ToList();
            var name = identifiers.FirstOrDefault()?.Value.Trim() ?? "";
            if (identifiers.Count != 1 || name.Length == 0)
                AddError(issues, $"{objectKind} entry", $"does not have exactly one non-empty <{identifierElement}>.");
            else
                names.Add(name);
            ValidateDuplicateSingletons(entry, $"{objectKind} '{name}'", singletonElements, issues);
            additionalValidation?.Invoke(entry, name.Length == 0 ? "(unnamed)" : name, issues);
        }
        AddDuplicateNames(issues, objectKind, names);
    }

    private static void ValidateTextEntries(XElement root, string entryName, string objectKind, List<ModXmlValidationIssue> issues)
    {
        var direct = root.Elements().Where(element => IsNamed(element, entryName)).ToList();
        var nested = root.Descendants().Where(element => IsNamed(element, entryName)).Except(direct).ToList();
        if (nested.Count > 0)
            AddError(issues, "Document", $"Found {nested.Count} nested <{entryName}> definition(s).");
        var names = direct.Select(element => element.Value.Trim()).ToList();
        if (names.Any(name => name.Length == 0))
            AddError(issues, objectKind, $"contains an empty <{entryName}> definition.");
        AddDuplicateNames(issues, objectKind, names.Where(name => name.Length > 0));
    }

    private static void ValidateDuplicateSingletons(XElement owner, string objectName, HashSet<string> singletonNames, List<ModXmlValidationIssue> issues)
    {
        foreach (var duplicate in owner.Elements()
                     .Where(element => singletonNames.Contains(element.Name.LocalName))
                     .GroupBy(element => element.Name.LocalName, NameComparer)
                     .Where(group => group.Count() > 1))
        {
            AddError(issues, objectName, $"contains {duplicate.Count()} <{duplicate.Key}> elements, but the editor manages that element as a single value.");
        }
    }

    private static void ValidateDuplicateKeyedChildren(XElement owner, string objectName, string elementName, string keyAttribute, List<ModXmlValidationIssue> issues)
    {
        foreach (var duplicate in owner.Elements().Where(element => IsNamed(element, elementName))
                     .Select(element => element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName.Equals(keyAttribute, StringComparison.OrdinalIgnoreCase))?.Value.Trim() ?? "")
                     .Where(value => value.Length > 0)
                     .GroupBy(value => value, NameComparer)
                     .Where(group => group.Count() > 1))
        {
            AddError(issues, objectName, $"contains {duplicate.Count()} <{elementName}> entries for '{duplicate.Key}'.");
        }
    }

    private static void ValidateDuplicateAttributes(
        XElement owner,
        string objectName,
        IReadOnlyCollection<string> managedNames,
        List<ModXmlValidationIssue> issues)
    {
        foreach (var duplicate in owner.Attributes()
                     .Where(attribute => managedNames.Contains(attribute.Name.LocalName, NameComparer))
                     .GroupBy(attribute => attribute.Name.LocalName, NameComparer)
                     .Where(group => group.Count() > 1))
        {
            AddError(
                issues,
                objectName,
                $"contains {duplicate.Count()} '{duplicate.Key}' attributes, but the editor manages that attribute as a single value.");
        }
    }

    private static void AddDuplicateNames(List<ModXmlValidationIssue> issues, string objectKind, IEnumerable<string> names)
    {
        foreach (var duplicate in names.GroupBy(name => name.Trim(), NameComparer).Where(group => group.Count() > 1))
            AddError(issues, $"{objectKind} '{duplicate.Key}'", $"is defined {duplicate.Count()} times.");
    }

    private static void WarnUnknownRootChildren(XElement root, IReadOnlyCollection<string> knownNames, List<ModXmlValidationIssue> issues)
    {
        foreach (var unknown in root.Elements().Where(element => !knownNames.Contains(element.Name.LocalName, NameComparer)))
        {
            issues.Add(new ModXmlValidationIssue(
                ModXmlValidationSeverity.Warning,
                $"Root element <{unknown.Name.LocalName}>",
                "is not managed by this editor; it will be preserved unchanged."));
        }
    }

    private static string GetElementIdentifier(XElement element)
        => element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName.Equals("name", StringComparison.OrdinalIgnoreCase))?.Value.Trim()
           ?? GetDirectText(element);

    private static string GetDirectText(XElement element)
        => string.Concat(element.Nodes().OfType<XText>().Select(text => text.Value)).Trim();

    private static bool IsNamed(XElement element, string localName)
        => element.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase);

    private static void AddError(List<ModXmlValidationIssue> issues, string objectName, string reason)
        => issues.Add(new ModXmlValidationIssue(ModXmlValidationSeverity.Error, objectName, reason));
}
