using System.Xml.Linq;
using AoMDivineDataEditor.GameData;

namespace AoMDivineDataEditor.Classes;

public sealed record OwnedStringRename(string Field, string OldId, string NewId);

public sealed record MajorGodDefinition(string Name, bool IsBuiltIn, XElement SourceElement);

public static class MajorGodCatalog
{
    public const string SharedDataDisplayName = "Shared data";
    public const string BaseFileName = "major_gods.xml.xmb";
    public const string AlternateBaseFileName = "major_gods.xmb";
    public const string ModFileName = "major_gods_mods.xml";
    public const string ModRootName = "civsmods";
    public const string EntryName = "civ";
    public static readonly string[] AgeValues =
    [
        "ArchaicAge", "ClassicalAge", "HeroicAge", "MythicAge", "WonderAge"
    ];
    public static readonly string[] Pass1FieldOrder =
    [
        "name", "statsid", "key", "culture", "displaynameid", "rollovernameid", "titleid",
        "icon", "portrait", "breakoutportrait", "minimapborderimage", "backgroundscenario"
    ];
    public static readonly string[] EconomicFieldOrder =
    [
        "secondaryprayerefficiency", "buildingefficiency", "extrabuildingefficiency", "buildingrepairfactor"
    ];

    public static IReadOnlyList<MajorGodDefinition> ExtractDefinitions(XContainer container, bool isBuiltIn)
        => container.Descendants()
            .Where(element => element.Name.LocalName.Equals(EntryName, StringComparison.OrdinalIgnoreCase))
            .Select(element => new MajorGodDefinition(GetName(element), isBuiltIn, new XElement(element)))
            .Where(definition => definition.Name.Length > 0)
            .GroupBy(definition => definition.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(definition => definition.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static XElement? ExtractSharedData(XContainer container)
    {
        var root = container switch
        {
            XDocument document => document.Root,
            XElement element => element,
            _ => null
        };
        if (root == null)
            return null;

        var sharedElements = root.Elements()
            .Where(element => !element.Name.LocalName.Equals(EntryName, StringComparison.OrdinalIgnoreCase))
            .Select(element => new XElement(element))
            .ToList();
        return sharedElements.Count == 0 ? null : new XElement(root.Name, sharedElements);
    }

    public static XElement? ExtractSharedDataFromBar(BarArchive? barFile, string? barPath)
    {
        if (barFile?.Entries == null || string.IsNullOrWhiteSpace(barPath) || !File.Exists(barPath))
            return null;

        var entries = barFile.Entries.Where(entry =>
        {
            var fileName = entry.Name.Replace('\\', '/').Split('/').LastOrDefault() ?? "";
            return fileName.Equals(BaseFileName, StringComparison.OrdinalIgnoreCase) ||
                   fileName.Equals(AlternateBaseFileName, StringComparison.OrdinalIgnoreCase);
        });
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
                    return ExtractSharedData(XDocument.Parse(xml, LoadOptions.PreserveWhitespace));
            }
            catch
            {
                // Try another matching BAR member if this one is malformed.
            }
        }
        return null;
    }

    public static IReadOnlyList<MajorGodDefinition> ExtractBaseDefinitionsFromBar(BarArchive? barFile, string? barPath)
    {
        if (barFile?.Entries == null || string.IsNullOrWhiteSpace(barPath) || !File.Exists(barPath))
            return [];

        var definitions = new Dictionary<string, MajorGodDefinition>(StringComparer.OrdinalIgnoreCase);
        var entries = barFile.Entries.Where(entry =>
        {
            var fileName = entry.Name.Replace('\\', '/').Split('/').LastOrDefault() ?? "";
            return fileName.Equals(BaseFileName, StringComparison.OrdinalIgnoreCase) ||
                   fileName.Equals(AlternateBaseFileName, StringComparison.OrdinalIgnoreCase);
        });

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
                if (string.IsNullOrWhiteSpace(xml))
                    continue;

                foreach (var definition in ExtractDefinitions(XDocument.Parse(xml, LoadOptions.PreserveWhitespace), true))
                    definitions.TryAdd(definition.Name, definition);
            }
            catch
            {
                // Keep entries already decoded if a matching BAR member is malformed.
            }
        }

        return definitions.Values.OrderBy(definition => definition.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static IReadOnlyList<MajorGodDefinition> ReadModFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return [];

        var document = XDocument.Load(path, LoadOptions.PreserveWhitespace);
        RequireModRoot(document);
        return ExtractDefinitions(document, false);
    }

    public static XDocument LoadOrCreateModDocument(string path)
    {
        if (!File.Exists(path))
            return new XDocument(new XElement(ModRootName));

        var document = XDocument.Load(path, LoadOptions.PreserveWhitespace);
        RequireModRoot(document);
        return document;
    }

    public static string GetName(XElement entry)
        => entry.Elements().FirstOrDefault(element =>
               element.Name.LocalName.Equals("name", StringComparison.OrdinalIgnoreCase))?.Value.Trim() ?? "";

    public static XElement? Find(XContainer container, string name)
        => container.Descendants().FirstOrDefault(element =>
            element.Name.LocalName.Equals(EntryName, StringComparison.OrdinalIgnoreCase) &&
            GetName(element).Equals(name, StringComparison.OrdinalIgnoreCase));

    public static XElement CreatePass1Entry(string name, string culture = "Greek")
    {
        var normalizedName = name.Trim();
        return new XElement(EntryName,
            new XElement("name", normalizedName),
            new XElement("statsid", "SP"),
            new XElement("key", culture.Length == 0 ? "G" : char.ToUpperInvariant(culture[0]).ToString()),
            new XElement("culture", culture),
            new XElement("displaynameid", BuildStringId(normalizedName, "displaynameid")),
            new XElement("rollovernameid", BuildStringId(normalizedName, "rollovernameid")),
            new XElement("titleid", BuildStringId(normalizedName, "titleid")),
            new XElement("icon"),
            new XElement("portrait"),
            new XElement("breakoutportrait"),
            new XElement("minimapborderimage"),
            new XElement("backgroundscenario"));
    }

    public static void EnsureStatsId(XElement entry)
    {
        var statsId = FindDirectChild(entry, "statsid");
        if (statsId != null)
        {
            XmlTriviaPreserver.SetText(statsId, "SP");
            return;
        }

        var element = new XElement("statsid", "SP");
        var name = FindDirectChild(entry, "name");
        if (name != null)
            name.AddAfterSelf(element);
        else
            entry.AddFirst(element);
    }

    public static XElement SetPass1Value(XElement entry, string tag, string? value)
    {
        if (!Pass1FieldOrder.Contains(tag, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentOutOfRangeException(nameof(tag), tag, "Not a Pass 1 Major God field.");

        var existing = FindDirectChild(entry, tag);
        if (existing != null)
        {
            XmlTriviaPreserver.SetText(existing, value);
            return existing;
        }

        var created = new XElement(tag, value ?? "");
        var targetIndex = Array.FindIndex(Pass1FieldOrder, candidate =>
            candidate.Equals(tag, StringComparison.OrdinalIgnoreCase));
        var followingKnown = entry.Elements().FirstOrDefault(child =>
        {
            var childIndex = Array.FindIndex(Pass1FieldOrder, candidate =>
                candidate.Equals(child.Name.LocalName, StringComparison.OrdinalIgnoreCase));
            return childIndex > targetIndex;
        });
        if (followingKnown != null)
            followingKnown.AddBeforeSelf(created);
        else
            entry.Add(created);
        return created;
    }

    public static XElement? FindDirectChild(XElement entry, string tag)
        => entry.Elements().FirstOrDefault(element =>
            element.Name.LocalName.Equals(tag, StringComparison.OrdinalIgnoreCase));

    public static XElement SetAgeTech(XElement entry, string age, string technology)
    {
        var ageTech = FindDirectChild(entry, "agetech");
        if (ageTech == null)
        {
            ageTech = new XElement("agetech", new XAttribute("age", age), new XElement("tech", technology));
            InsertAssociatedTechElement(entry, ageTech, "deathmatchtech", "teamtech", "icon");
            return ageTech;
        }

        ageTech.SetAttributeValue("age", age);
        var tech = FindDirectChild(ageTech, "tech");
        if (tech == null)
            ageTech.AddFirst(new XElement("tech", technology));
        else
            XmlTriviaPreserver.SetText(tech, technology);
        return ageTech;
    }

    public static XElement SetAssociatedTech(XElement entry, string tag, string technology)
    {
        if (!tag.Equals("deathmatchtech", StringComparison.OrdinalIgnoreCase) &&
            !tag.Equals("teamtech", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentOutOfRangeException(nameof(tag), tag, "Not an associated Major God technology field.");
        }

        var existing = FindDirectChild(entry, tag);
        if (existing != null)
        {
            XmlTriviaPreserver.SetText(existing, technology);
            return existing;
        }

        var created = new XElement(tag, technology);
        if (tag.Equals("deathmatchtech", StringComparison.OrdinalIgnoreCase))
        {
            var following = entry.Elements().FirstOrDefault(element =>
                element.Name.LocalName.Equals("teamtech", StringComparison.OrdinalIgnoreCase) ||
                element.Name.LocalName.Equals("icon", StringComparison.OrdinalIgnoreCase));
            if (following != null)
                following.AddBeforeSelf(created);
            else if (FindDirectChild(entry, "agetech") is { } ageTech)
                ageTech.AddAfterSelf(created);
            else
                InsertAssociatedTechElement(entry, created);
        }
        else
        {
            var icon = FindDirectChild(entry, "icon");
            if (icon != null)
                icon.AddBeforeSelf(created);
            else if (FindDirectChild(entry, "deathmatchtech") is { } deathmatchTech)
                deathmatchTech.AddAfterSelf(created);
            else if (FindDirectChild(entry, "agetech") is { } ageTech)
                ageTech.AddAfterSelf(created);
            else
                InsertAssociatedTechElement(entry, created);
        }
        return created;
    }

    public static XElement SetEconomicValue(XElement entry, string tag, string value)
    {
        var targetIndex = Array.FindIndex(EconomicFieldOrder, candidate =>
            candidate.Equals(tag, StringComparison.OrdinalIgnoreCase));
        if (targetIndex < 0 || tag.Equals("secondaryprayerefficiency", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentOutOfRangeException(nameof(tag), tag, "Not a simple Major God economic field.");

        var existing = FindDirectChild(entry, tag);
        if (existing != null)
        {
            XmlTriviaPreserver.SetText(existing, value);
            return existing;
        }

        var created = new XElement(tag.ToLowerInvariant(), value);
        InsertEconomicElement(entry, created, targetIndex);
        return created;
    }

    public static XElement AddSecondaryPrayerEfficiency(
        XElement entry,
        string laterGrowth = "0",
        string earlyIncomeReduction = "0",
        string modifier = "0")
    {
        var existing = FindDirectChild(entry, "secondaryprayerefficiency");
        if (existing != null)
            return existing;

        var created = new XElement("secondaryprayerefficiency",
            new XAttribute("g", laterGrowth),
            new XAttribute("v", earlyIncomeReduction),
            new XAttribute("z", modifier));
        InsertEconomicElement(entry, created, 0);
        return created;
    }

    public static XElement AddSpecialMechanic(XElement entry, string tag)
    {
        var existing = FindDirectChild(entry, tag);
        if (existing != null)
            return existing;

        var created = new XElement(tag.ToLowerInvariant());
        entry.Add(created);
        NormalizeDirectChildIndentation(entry);
        return created;
    }

    public static XElement AddSpecialMechanicChild(XElement mechanic, string tag, string value = "")
    {
        var child = new XElement(tag.ToLowerInvariant(), value);
        mechanic.Add(child);
        NormalizeDirectChildIndentation(mechanic);
        return child;
    }

    public static XElement AddShadesChild(XElement shades, string tag, string value = "")
    {
        var order = new[] { "protounit", "targetunit", "chance", "delay", "maxqueued" };
        var normalizedTag = tag.ToLowerInvariant();
        var child = new XElement(normalizedTag, value);
        var index = Array.FindIndex(order, candidate => candidate.Equals(normalizedTag, StringComparison.OrdinalIgnoreCase));
        var following = shades.Elements().FirstOrDefault(element =>
        {
            var followingIndex = Array.FindIndex(order, candidate =>
                candidate.Equals(element.Name.LocalName, StringComparison.OrdinalIgnoreCase));
            return followingIndex > index;
        });
        if (following != null) following.AddBeforeSelf(child);
        else shades.Add(child);
        NormalizeDirectChildIndentation(shades);
        return child;
    }

    public static XElement AddTimeShiftingUnit(XElement timeShifting, string protoUnitName = "")
    {
        var unit = new XElement("protounit",
            new XAttribute("costratio", "0"),
            new XAttribute("timeratio", "0"),
            protoUnitName);
        timeShifting.Add(unit);
        NormalizeDirectChildIndentation(timeShifting);
        return unit;
    }

    public static XElement AddTerrainCreep(XElement terrainCreeps)
    {
        var creep = new XElement("terraincreep",
            new XAttribute("creep", ""),
            new XAttribute("maxradius", "0"),
            new XAttribute("growthrate", "0"),
            new XAttribute("decayrate", "0"),
            new XAttribute("minupdateinterval", "0"),
            new XAttribute("maxupdateinterval", "0"));
        terrainCreeps.Add(creep);
        NormalizeDirectChildIndentation(terrainCreeps);
        return creep;
    }

    public static XElement AddTerrainCreepGenerator(XElement terrainCreep, string protoUnitName)
    {
        var generator = new XElement("protounit", protoUnitName.Trim());
        terrainCreep.Add(generator);
        NormalizeDirectChildIndentation(terrainCreep);
        return generator;
    }

    public static XElement AddBuildingChainAnchor(XElement buildingChain)
    {
        var existing = FindDirectChild(buildingChain, "anchor");
        if (existing != null)
            return existing;
        var anchor = new XElement("anchor", new XAttribute("vfx", ""));
        buildingChain.AddFirst(anchor);
        NormalizeDirectChildIndentation(buildingChain);
        return anchor;
    }

    public static XElement AddBuildingChainAbundanceVfx(XElement buildingChain)
        => AddBuildingChainVfxGroup(buildingChain, "abundancevfx");

    public static XElement AddBuildingChainAutoBuildVfx(XElement buildingChain)
        => AddBuildingChainVfxGroup(buildingChain, "autobuildvfx");

    private static XElement AddBuildingChainVfxGroup(XElement buildingChain, string tag)
    {
        var existing = FindDirectChild(buildingChain, tag);
        if (existing != null)
            return existing;
        var vfxGroup = new XElement(tag,
            new XAttribute("small", ""),
            new XAttribute("medium", ""),
            new XAttribute("large", ""));
        var preceding = tag.Equals("autobuildvfx", StringComparison.OrdinalIgnoreCase)
            ? FindDirectChild(buildingChain, "abundancevfx") ?? FindDirectChild(buildingChain, "anchor")
            : FindDirectChild(buildingChain, "anchor");
        if (preceding != null) preceding.AddAfterSelf(vfxGroup);
        else buildingChain.AddFirst(vfxGroup);
        NormalizeDirectChildIndentation(buildingChain);
        return vfxGroup;
    }

    public static XElement AddCombatXpTier(XElement combatXp, string value = "0")
    {
        var tier = new XElement("tier", value);
        combatXp.Add(tier);
        NormalizeDirectChildIndentation(combatXp);
        return tier;
    }

    public static XElement AddBountyElement(XElement bounty, string tag, string value = "")
    {
        var normalizedTag = tag.ToLowerInvariant();
        var element = normalizedTag switch
        {
            "bountyreward" or "bountytargetmultiplier" => new XElement(normalizedTag,
                new XAttribute("unittype", ""),
                new XAttribute("condition", "Damage"),
                new XAttribute("resourcetype", "Favor"),
                value.Length == 0 ? "0" : value),
            "researchreward" or "researchcostmultiplier" => new XElement(normalizedTag,
                new XAttribute("techtype", "all"),
                new XAttribute("resourcetype", "Favor"),
                value.Length == 0 ? "0" : value),
            _ => new XElement(normalizedTag, value)
        };

        string[] order =
        [
            "active", "vfx", "excludedtarget", "bountydamagegoal", "bountyreductionstart",
            "bountyreductionend", "bountyreductionstrength", "bountyreward",
            "bountytargetmultiplier", "researchreward", "researchcostmultiplier", "excludedtechflag"
        ];
        var index = Array.FindIndex(order, candidate => candidate.Equals(normalizedTag, StringComparison.OrdinalIgnoreCase));
        var following = bounty.Elements().FirstOrDefault(child =>
        {
            var followingIndex = Array.FindIndex(order, candidate =>
                candidate.Equals(child.Name.LocalName, StringComparison.OrdinalIgnoreCase));
            return followingIndex > index;
        });
        if (following != null) following.AddBeforeSelf(element);
        else bounty.Add(element);
        NormalizeDirectChildIndentation(bounty);
        return element;
    }

    public static XElement AddBonusUnitGoal(XElement bonusUnitSpawning, string goalTag = "damagegoal")
    {
        var normalizedTag = goalTag.ToLowerInvariant();
        var goal = new XElement(normalizedTag,
            new XAttribute("name", ""),
            new XAttribute("rewardtracking", "Single"),
            new XElement("goalamount", "1"),
            new XElement("spawnlocation", new XAttribute("type", DefaultSpawnLocationForGoal(normalizedTag))));
        bonusUnitSpawning.Add(goal);
        NormalizeDirectChildIndentation(bonusUnitSpawning);
        NormalizeDirectChildIndentation(goal);
        return goal;
    }

    public static XElement AddBonusGoalChild(XElement goal, string tag, string value = "")
    {
        var normalizedTag = tag.ToLowerInvariant();
        var child = normalizedTag switch
        {
            "contributor" => new XElement(normalizedTag, new XAttribute("unit", ""), value.Length == 0 ? "0" : value),
            "spawnlocation" => new XElement(normalizedTag,
                new XAttribute("type", DefaultSpawnLocationForGoal(goal.Name.LocalName))),
            _ => new XElement(normalizedTag, value)
        };
        goal.Add(child);
        NormalizeDirectChildIndentation(goal);
        return child;
    }

    public static IReadOnlyList<XElement> AddBonusRewardPointCosts(XElement goal)
    {
        var created = new List<XElement>();
        foreach (var resource in ProtoConstants.KnownResourceTypes)
        {
            var cost = new XElement("rewardpointcost", new XAttribute("resourcetype", resource), "0");
            goal.Add(cost);
            created.Add(cost);
        }
        NormalizeDirectChildIndentation(goal);
        return created;
    }

    public static XElement AddBonusDisplayInfo(XElement goal)
    {
        var existing = FindDirectChild(goal, "displayinfo");
        if (existing != null)
            return existing;
        var goalName = goal.Attributes().FirstOrDefault(attribute =>
            attribute.Name.LocalName.Equals("name", StringComparison.OrdinalIgnoreCase))?.Value.Trim().ToUpperInvariant() ?? "";
        var displayInfo = new XElement("displayinfo",
            new XElement("displaynameid", $"STR_{goalName}_SPAWN_REWARD"),
            new XElement("rolloverid", $"STR_{goalName}_SPAWN_REWARD_LR"),
            new XElement("progresstype", "Default"),
            new XElement("icon", ""),
            new XElement("progresstooltipid", $"STR_{goalName}_SPAWN_REWARD_PROGRESS"),
            new XElement("nextunittooltipid", $"STR_{goalName}_SPAWN_NEXT_UNIT"),
            new XElement("notificationsound", ""),
            new XElement("notificationmessageid", $"STR_{goalName}_SPAWN_REWARD_NOTIFICATION"));
        goal.Add(new XElement("displayonhud", "true"), displayInfo);
        NormalizeDirectChildIndentation(goal);
        NormalizeDirectChildIndentation(displayInfo);
        return displayInfo;
    }

    private static string DefaultSpawnLocationForGoal(string goalTag)
        => goalTag.Equals("deathcountgoal", StringComparison.OrdinalIgnoreCase)
            ? "DeathLocation"
            : goalTag.Equals("resourcegoal", StringComparison.OrdinalIgnoreCase)
                ? "FromBuilding"
                : "DamageTarget";

    public static XElement AddBuildingChainRateTier(XElement buildingChain)
    {
        var resourceGeneration = FindDirectChild(buildingChain, "resourcegeneration");
        if (resourceGeneration == null)
        {
            resourceGeneration = new XElement("resourcegeneration");
            buildingChain.Add(resourceGeneration);
            NormalizeDirectChildIndentation(buildingChain);
        }
        var tier = new XElement("tilerateperminutetier",
            new XAttribute("resource", "Favor"),
            new XAttribute("mintiles", "0"),
            "0");
        resourceGeneration.Add(tier);
        NormalizeDirectChildIndentation(resourceGeneration);
        return tier;
    }

    public static XElement AddChainableBuilding(XElement buildingChain)
    {
        var building = new XElement("chainablebuilding",
            new XAttribute("generationresource", "Favor"),
            new XAttribute("chainradius", "0"));
        var resourceGeneration = FindDirectChild(buildingChain, "resourcegeneration");
        if (resourceGeneration != null) resourceGeneration.AddBeforeSelf(building);
        else buildingChain.Add(building);
        NormalizeDirectChildIndentation(buildingChain);
        return building;
    }

    public static XElement? SetSpecialMechanicToggle(XElement entry, string tag, bool enabled, string value = "")
    {
        var existing = FindDirectChild(entry, tag);
        if (!enabled)
        {
            if (existing != null)
                RemoveElementAndAdjacentWhitespace(existing);
            return null;
        }

        var element = existing ?? AddSpecialMechanic(entry, tag);
        XmlTriviaPreserver.SetText(element, value);
        return element;
    }

    private static void InsertEconomicElement(XElement entry, XElement created, int targetIndex)
    {
        var following = entry.Elements().FirstOrDefault(element =>
        {
            var economicIndex = Array.FindIndex(EconomicFieldOrder, candidate =>
                candidate.Equals(element.Name.LocalName, StringComparison.OrdinalIgnoreCase));
            return economicIndex > targetIndex ||
                   element.Name.LocalName.Equals("startingunits", StringComparison.OrdinalIgnoreCase) ||
                   element.Name.LocalName.Equals("startingqueue", StringComparison.OrdinalIgnoreCase) ||
                   element.Name.LocalName.Equals("startingresources", StringComparison.OrdinalIgnoreCase) ||
                   element.Name.LocalName.Equals("icon", StringComparison.OrdinalIgnoreCase);
        });
        if (following != null)
            following.AddBeforeSelf(created);
        else
            entry.Add(created);
        NormalizeDirectChildIndentation(entry);
    }

    public static XElement AddStartingUnitsGroup(XElement entry, string mode = "supremacy")
    {
        var group = new XElement("startingunits");
        if (!mode.Equals("supremacy", StringComparison.OrdinalIgnoreCase))
            group.SetAttributeValue("mode", mode.ToLowerInvariant());

        var following = entry.Elements().FirstOrDefault(element =>
            element.Name.LocalName.Equals("startingresources", StringComparison.OrdinalIgnoreCase) ||
            element.Name.LocalName.Equals("icon", StringComparison.OrdinalIgnoreCase));
        if (following != null)
            following.AddBeforeSelf(group);
        else
            entry.Add(group);
        NormalizeDirectChildIndentation(entry);
        return group;
    }

    public static XElement AddStartingUnit(XElement group, string protoUnitName = "")
    {
        var unit = new XElement("unit",
            new XAttribute("count", "1"),
            new XAttribute("x", "0"),
            new XAttribute("y", "0"),
            new XAttribute("z", "0"),
            protoUnitName);
        group.Add(unit);
        NormalizeDirectChildIndentation(group);
        return unit;
    }

    public static XElement AddStartingResourcesGroup(XElement entry, string mode = "supremacy")
    {
        var group = new XElement("startingresources");
        if (!mode.Equals("supremacy", StringComparison.OrdinalIgnoreCase))
            group.SetAttributeValue("mode", mode.ToLowerInvariant());

        var icon = FindDirectChild(entry, "icon");
        if (icon != null)
            icon.AddBeforeSelf(group);
        else
            entry.Add(group);
        NormalizeDirectChildIndentation(entry);
        return group;
    }

    public static XElement AddStartingQueueGroup(XElement entry, string mode = "supremacy")
    {
        var group = new XElement("startingqueue");
        if (!mode.Equals("supremacy", StringComparison.OrdinalIgnoreCase))
            group.SetAttributeValue("mode", mode.ToLowerInvariant());

        var following = entry.Elements().FirstOrDefault(element =>
            element.Name.LocalName.Equals("startingresources", StringComparison.OrdinalIgnoreCase) ||
            element.Name.LocalName.Equals("icon", StringComparison.OrdinalIgnoreCase));
        if (following != null)
            following.AddBeforeSelf(group);
        else
            entry.Add(group);
        NormalizeDirectChildIndentation(entry);
        return group;
    }

    public static XElement AddStartingQueueUnit(XElement group, string protoUnitName = "")
    {
        var unit = new XElement("unit", protoUnitName);
        group.Add(unit);
        NormalizeDirectChildIndentation(group);
        return unit;
    }

    public static void SetStartingQueueCount(XElement unit, string value)
    {
        foreach (var attributeName in new[] { "autotrain", "prequeue" })
            unit.Attributes().FirstOrDefault(attribute =>
                attribute.Name.LocalName.Equals(attributeName, StringComparison.OrdinalIgnoreCase))?.Remove();

        var count = unit.Attributes().FirstOrDefault(attribute =>
            attribute.Name.LocalName.Equals("count", StringComparison.OrdinalIgnoreCase));
        if (count == null)
            unit.SetAttributeValue("count", value);
        else
            count.Value = value;
    }

    public static XElement AddStartingResource(XElement group, string resourceType, string amount = "0")
    {
        var resource = new XElement(resourceType.Trim().ToLowerInvariant(), amount);
        group.Add(resource);
        NormalizeDirectChildIndentation(group);
        return resource;
    }

    public static void RemoveElementAndAdjacentWhitespace(XElement element)
    {
        var parent = element.Parent;
        element.Remove();
        if (parent != null)
            NormalizeDirectChildIndentation(parent);
    }

    public static void NormalizeDirectChildIndentation(XElement parent)
    {
        var contentNodes = parent.Nodes()
            .Where(node => node is not XText text || !string.IsNullOrWhiteSpace(text.Value))
            .ToList();
        if (contentNodes.Count == 0)
        {
            parent.RemoveNodes();
            return;
        }

        var ownIndent = FindElementIndentation(parent);
        var childIndent = ownIndent + "  ";
        parent.RemoveNodes();
        foreach (var node in contentNodes)
        {
            parent.Add(new XText(Environment.NewLine + childIndent));
            parent.Add(node);
        }
        parent.Add(new XText(Environment.NewLine + ownIndent));
    }

    private static string FindElementIndentation(XElement element)
    {
        if (element.PreviousNode is XText precedingWhitespace && string.IsNullOrWhiteSpace(precedingWhitespace.Value))
        {
            var lastNewLine = Math.Max(precedingWhitespace.Value.LastIndexOf('\n'), precedingWhitespace.Value.LastIndexOf('\r'));
            return precedingWhitespace.Value[(lastNewLine + 1)..];
        }

        return element.Parent == null ? "" : FindElementIndentation(element.Parent) + "  ";
    }

    private static void InsertAssociatedTechElement(XElement entry, XElement created, params string[] followingTags)
    {
        var following = entry.Elements().FirstOrDefault(element => followingTags.Any(tag =>
            element.Name.LocalName.Equals(tag, StringComparison.OrdinalIgnoreCase)));
        if (following != null)
        {
            following.AddBeforeSelf(created);
            return;
        }

        var title = FindDirectChild(entry, "titleid");
        if (title != null)
            title.AddAfterSelf(created);
        else
            entry.Add(created);
    }

    public static string BuildStringId(string majorGodName, string tag)
    {
        var token = new string(majorGodName.Trim()
            .Select(character => char.IsLetterOrDigit(character) ? char.ToUpperInvariant(character) : '_')
            .ToArray());
        while (token.Contains("__", StringComparison.Ordinal))
            token = token.Replace("__", "_", StringComparison.Ordinal);
        token = token.Trim('_');
        var suffix = tag.ToLowerInvariant() switch
        {
            "rollovernameid" => "_LR",
            "titleid" => "_T",
            _ => ""
        };
        return $"STR_CIV_{token}{suffix}";
    }

    public static bool OwnsResolvedStringId(string majorGodName, string tag, string? stringId)
        => !string.IsNullOrWhiteSpace(stringId) &&
           BuildStringId(majorGodName, tag).Equals(stringId.Trim(), StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<OwnedStringRename> RenameOwnedResolvedStringIds(
        XElement entry,
        string oldMajorGodName,
        string newMajorGodName)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var changes = new List<OwnedStringRename>();
        foreach (var tag in new[] { "displaynameid", "rollovernameid", "titleid" })
        {
            var element = FindDirectChild(entry, tag);
            var oldId = BuildStringId(oldMajorGodName, tag);
            if (element == null || !element.Value.Trim().Equals(oldId, StringComparison.OrdinalIgnoreCase))
                continue;
            var newId = BuildStringId(newMajorGodName, tag);
            if (oldId.Equals(newId, StringComparison.OrdinalIgnoreCase))
                continue;
            XmlTriviaPreserver.SetText(element, newId);
            changes.Add(new OwnedStringRename(tag, oldId, newId));
        }
        return changes;
    }

    public static string BuildBonusGoalStringId(string goalName, string suffix)
        => $"STR_{goalName.Trim().ToUpperInvariant()}_{suffix}";

    public static IReadOnlyList<OwnedStringRename> RenameOwnedBonusGoalStringIds(
        XElement goal,
        string oldGoalName,
        string newGoalName)
    {
        ArgumentNullException.ThrowIfNull(goal);
        var displayInfo = FindDirectChild(goal, "displayinfo");
        if (displayInfo == null)
            return [];
        var changes = new List<OwnedStringRename>();
        foreach (var (tag, suffix) in new[]
                 {
                     ("displaynameid", "SPAWN_REWARD"),
                     ("rolloverid", "SPAWN_REWARD_LR"),
                     ("progresstooltipid", "SPAWN_REWARD_PROGRESS"),
                     ("nextunittooltipid", "SPAWN_NEXT_UNIT"),
                     ("notificationmessageid", "SPAWN_REWARD_NOTIFICATION")
                 })
        {
            var element = FindDirectChild(displayInfo, tag);
            var oldId = BuildBonusGoalStringId(oldGoalName, suffix);
            if (element == null || !element.Value.Trim().Equals(oldId, StringComparison.OrdinalIgnoreCase))
                continue;
            var newId = BuildBonusGoalStringId(newGoalName, suffix);
            if (oldId.Equals(newId, StringComparison.OrdinalIgnoreCase))
                continue;
            XmlTriviaPreserver.SetText(element, newId);
            changes.Add(new OwnedStringRename(tag, oldId, newId));
        }
        return changes;
    }

    public static IReadOnlySet<string> CollectOwnedStringIds(XElement entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var majorGodName = GetName(entry);
        foreach (var tag in new[] { "displaynameid", "rollovernameid", "titleid" })
        {
            var id = FindDirectChild(entry, tag)?.Value.Trim() ?? "";
            if (majorGodName.Length > 0 && OwnsResolvedStringId(majorGodName, tag, id))
                owned.Add(id);
        }

        foreach (var goal in entry.Descendants().Where(element =>
                     element.Name.LocalName.Equals("damagegoal", StringComparison.OrdinalIgnoreCase) ||
                     element.Name.LocalName.Equals("deathcountgoal", StringComparison.OrdinalIgnoreCase) ||
                     element.Name.LocalName.Equals("resourcegoal", StringComparison.OrdinalIgnoreCase)))
        {
            var goalName = goal.Attributes().FirstOrDefault(attribute =>
                attribute.Name.LocalName.Equals("name", StringComparison.OrdinalIgnoreCase))?.Value.Trim() ?? "";
            var displayInfo = FindDirectChild(goal, "displayinfo");
            if (goalName.Length == 0 || displayInfo == null)
                continue;
            foreach (var (tag, suffix) in new[]
                     {
                         ("displaynameid", "SPAWN_REWARD"),
                         ("rolloverid", "SPAWN_REWARD_LR"),
                         ("progresstooltipid", "SPAWN_REWARD_PROGRESS"),
                         ("nextunittooltipid", "SPAWN_NEXT_UNIT"),
                         ("notificationmessageid", "SPAWN_REWARD_NOTIFICATION")
                     })
            {
                var id = FindDirectChild(displayInfo, tag)?.Value.Trim() ?? "";
                if (id.Equals(BuildBonusGoalStringId(goalName, suffix), StringComparison.OrdinalIgnoreCase))
                    owned.Add(id);
            }
        }
        return owned;
    }

    public static MajorGodDefinition? FindGeneratedStringNamespaceCollision(
        string majorGodName,
        IEnumerable<MajorGodDefinition> definitions,
        string? excludedMajorGodName = null)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        var generatedIds = new[] { "displaynameid", "rollovernameid", "titleid" }
            .Select(tag => BuildStringId(majorGodName, tag))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return definitions.FirstOrDefault(definition =>
        {
            if (definition.Name.Equals(majorGodName, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(excludedMajorGodName) &&
                 definition.Name.Equals(excludedMajorGodName, StringComparison.OrdinalIgnoreCase)))
                return false;
            if (new[] { "displaynameid", "rollovernameid", "titleid" }
                .Any(tag => generatedIds.Contains(BuildStringId(definition.Name, tag))))
                return true;
            return definition.SourceElement.DescendantsAndSelf().Any(element =>
                !element.HasElements && generatedIds.Contains(element.Value.Trim())) ||
                   definition.SourceElement.DescendantsAndSelf().Any(element =>
                       element.Attributes().Any(attribute => generatedIds.Contains(attribute.Value.Trim())));
        });
    }

    public static bool IsValidKey(string? value)
        => value is { Length: 1 } && value[0] is >= 'A' and <= 'Z';

    public static XElement RequireModRoot(XDocument document)
    {
        if (document.Root == null || !document.Root.Name.LocalName.Equals(ModRootName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Expected <{ModRootName}> as the major-god mod root.");
        return document.Root;
    }
}
