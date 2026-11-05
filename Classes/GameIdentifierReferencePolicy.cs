using System.Xml.Linq;

namespace AoMDivineDataEditor.Classes;

public enum GameIdentifierKind
{
    ProtoUnit,
    UnitType,
    TechType,
    Culture,
    MajorGod,
    Ability,
    ProtoUnitCommand,
    Tactics
}

public sealed record ProtoActionRenameOperation(
    string UnitName,
    string OldName,
    string NewName);

public sealed record GameIdentifierReferenceUsage(
    string Identifier,
    string SourceLabel,
    string Path,
    string Field)
{
    public string DisplayLocation => $"{SourceLabel}: {Field}";
}

public sealed record GameIdentifierReferenceLocation(
    string Owner,
    string Field,
    bool IsRecognized);

/// <summary>
/// Central, conservative rename policy for identifiers shared by the ProtoUnit
/// and Technology editors. Definition names are deliberately excluded: callers
/// rename the definition themselves and this class migrates only references.
/// </summary>
public static class GameIdentifierReferencePolicy
{
    // Exact fields traced to the structured Power producers (coverage inventory D).
    // These are Unit identities even where the XML spelling is only "sfx" or "portal".
    private static readonly HashSet<string> PowerUnitFields = new(
        ("areavfx attackproto burstvfx chargingvfx cloudvfxproto damagereceiverproto damagingproto decalproto " +
         "dragonprotounit dustlargevfxproto dustmediumvfxproto dustsmallvfxproto earthmonsterprotounit effectproto " +
         "embersproto farmproto gatebirthproto gateflameproto groundimpactvfxproto groundsparksvfxproto groundvfxproto " +
         "gustproto healvfx icesheetvfxproto inportal inportalprotounit landimpactprotounit landprotounit largesfxprotoid " +
         "largestrikeproto largevfxproto lureproto meteorprotounit outportal outportalprotounit placementvfx " +
         "pseedsfarmtargetvfx pseedsfarmvfx rainprotounit rainvfxproto reappearunitvfx revealerproto rootedenemyvfx " +
         "scorchvfxproto sfx shockwaveproto smallsfx smallsfxprotoid smallvfxproto snowdriftprotounit sparksvfxproto " +
         "spawnproto spawnvfx sphereproto spiderdoorproto spidereggproto spiderproto spiderwarnproto spyprotounit " +
         "startingvfx stormprotounit strikeproto swarmprotounit targetunitvfx tartariangateproto tornadoprotounit " +
         "treeareavfx treevfx typhoonprotounit unitvfx vanishunitvfx vanishvfx vfxadditionalimpact vfxarea vfxcurseproto " +
         "vfxeidolonspawnproto vfxforunitswithoutbirthanims vfxmaintarget vfxmaintargetimpact vfxmarkanim vfxproto " +
         "vfxpulleffectproto vortexfinishlinkedproto vortexlandingproto vortexstartlinkedproto vortexstartproto wallproto " +
         "watersplashprotounit waveprotounit wolfvfx birthvfx modelattachment wolfproto vfxgrass snowdrift").Split(' '),
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> PowerMixedFields = new(
        ("mainpacktargettype minipacktargettype attachvfxto eidolontype attacktarget abstracttype rooteddamage " +
         "initialdamage excludefrompullingintofunnel excludefromthrow abstractplacementtargettype " +
         "explicitlyrestrictedplacementtargettype abstractattacktargettype explicitlyrestrictedattacktargettype " +
         "placementrestriction abstractpowerresetsabilitychargetype").Split(' '), StringComparer.OrdinalIgnoreCase);

    private enum ReferenceDomain { Unknown, Unit, UnitType, Mixed, MajorGod, Power }

    private static bool MatchesDomain(ReferenceDomain domain, GameIdentifierKind kind) => domain switch
    {
        ReferenceDomain.Unit => kind == GameIdentifierKind.ProtoUnit,
        ReferenceDomain.UnitType => kind == GameIdentifierKind.UnitType,
        ReferenceDomain.Mixed => kind is GameIdentifierKind.ProtoUnit or GameIdentifierKind.UnitType,
        ReferenceDomain.MajorGod => kind == GameIdentifierKind.MajorGod,
        ReferenceDomain.Power => kind == GameIdentifierKind.Ability,
        _ => false
    };

    private static bool IsKnownPowerField(XElement field)
        => IsDirectPowerField(field) && PowerSchemaRegistry.Schemas.ContainsKey(GetAttribute(field.Parent!, "type")?.Value.Trim() ?? "");

    private static bool IsActionDefinition(XElement action)
        => IsPlainXml(action) &&
           (action.Name == "protoaction" && action.Parent is { } unit && unit.Name == "unit" &&
            unit.Parent is { } root && root.Parent == null && root.Name.LocalName is "proto" or "protomods" ||
            action.Name == "action" && action.Parent is { } tactics && tactics.Name == "tactics" && tactics.Parent == null);

    private static bool IsManagedOnHit(XElement element)
        => element.Name == "onhiteffect" &&
           (IsKnownPowerField(element) || element.Parent is { } action && IsActionDefinition(action));

    private static bool IsTransformUnitFieldName(string name)
        => name.ToLowerInvariant() is "from" or "to" or "revertothersto";

    private static bool IsTransformUnitReference(XElement element)
        => IsPlainXml(element) && !element.HasElements && IsTransformUnitFieldName(element.Name.LocalName) &&
           element.Parent is { } transform && transform.Name == "transform" &&
           transform.Parent is { } root && root.Parent == null &&
           root.Name.LocalName is "unittransforms" or "unittransformmods";

    private static ReferenceDomain Group5ElementDomain(XElement element)
    {
        if (!IsPlainXml(element) || element.HasElements) return ReferenceDomain.Unknown;
        var name = element.Name.LocalName.ToLowerInvariant();
        if (element.Parent is { } command && command.Name == "protounitcommand" &&
            command.Parent is { } root && root.Name == "protounitcommands" && root.Parent == null)
        {
            if (name is "trainableunitreq" or "protounit" or "costprotounit") return ReferenceDomain.Unit;
            if (name == "associatedpower") return ReferenceDomain.Power;
        }
        if (name == "devotionpower" && element.Parent is { } action && IsActionDefinition(action))
            return ReferenceDomain.Power;
        if (name == "target" && element.Parent is { } replacements && replacements.Name == "duplicatetargetreplacement" &&
            IsDirectPowerField(replacements) &&
            GetAttribute(replacements.Parent!, "type")?.Value.Equals("PowerClone", StringComparison.OrdinalIgnoreCase) == true)
            return ReferenceDomain.Power;
        return ReferenceDomain.Unknown;
    }

    private static ReferenceDomain Group3ElementDomain(XElement element)
    {
        if (!IsPlainXml(element) || element.HasElements) return ReferenceDomain.Unknown;
        var name = element.Name.LocalName.ToLowerInvariant();
        if (element.Parent is { } tactic && tactic.Name == "tactic" &&
            tactic.Parent is { } tacticsRoot && tacticsRoot.Name == "tactics" && tacticsRoot.Parent == null &&
            name is "attackprioritytype" or "attacktype" or "excludeattacktype" or
                "autoattacktype" or "attackresponsetype" or "attackmovetype")
            return ReferenceDomain.Mixed;
        if (IsKnownPowerField(element))
        {
            if (PowerUnitFields.Contains(name)) return ReferenceDomain.Unit;
            if (PowerMixedFields.Contains(name)) return ReferenceDomain.Mixed;
            if (name is "rainspotproto" or "smallrainspotproto") return ReferenceDomain.UnitType;
        }
        if (element.Parent is { } action && IsActionDefinition(action))
        {
            if (name is "splashvfxproto" or "trailprotounit") return ReferenceDomain.Unit;
            if (name is "restrictempowertype" or "modifyabstracttype" or "modifyunittype" or "modifydamagetargettype") return ReferenceDomain.Mixed;
            if (name == "modifyciv") return ReferenceDomain.MajorGod;
        }
        return ReferenceDomain.Unknown;
    }

    private static ReferenceDomain Group3AttributeDomain(XElement owner, XAttribute attribute)
    {
        if (!IsPlainXml(owner) || attribute.Name.Namespace != XNamespace.None) return ReferenceDomain.Unknown;
        var name = attribute.Name.LocalName.ToLowerInvariant();
        // These action selectors accept concrete Units as well as UnitTypes.
        // Classify the exact context so rename/deletion and ambiguity checks
        // agree, without treating arbitrary @type values as Unit identities.
        if (name == "type" && owner.Name.LocalName.ToLowerInvariant() is "rate" or "damagebonus" &&
            owner.Parent is { } ownerAction && IsActionDefinition(ownerAction)) return ReferenceDomain.Mixed;
        if (IsKnownPowerField(owner))
        {
            if (owner.Name == "abstracttype" && name == "swapto" &&
                GetAttribute(owner.Parent!, "type")?.Value.Equals("SwapUnit", StringComparison.OrdinalIgnoreCase) == true) return ReferenceDomain.Unit;
            if (owner.Name == "placement" && name == "losprotounit") return ReferenceDomain.Unit;
            if (owner.Name == "snowdrift" && name == "target" ||
                owner.Name == "damagecap" && name == "unittype" ||
                owner.Name == "placement" && name is "restrictplacementnear" or "overlaptype" ||
                owner.Name == "terraincreep" && name == "preventplacement") return ReferenceDomain.Mixed;
        }
        if (owner.Name == "conversionprotoid" && name == "srctype" &&
            owner.Parent is { } action && IsActionDefinition(action)) return ReferenceDomain.Unit;
        if (IsManagedOnHit(owner) && name == "byunittype" &&
            GetAttribute(owner, "type")?.Value.Equals("InstantKillablePercentChance", StringComparison.OrdinalIgnoreCase) == true) return ReferenceDomain.Mixed;
        if (owner.Name == "target" && owner.Parent is { } hit && IsManagedOnHit(hit) &&
            name is "attacktype" or "ignoretype") return ReferenceDomain.Mixed;
        if (IsPowerEffect(owner))
        {
            var effect = EffectKind(owner);
            if (name == "attachprotounit" && effect == "enemyattachprotounit" ||
                name == "unittype" && effect == "protoactionadd") return ReferenceDomain.Unit;
            if (name == "unittype" && effect is "updatevisuals" or "enemyattachprotounit" or "workratespecific" or "damagebonus" ||
                name == "targettype" && effect is "onhiteffectprobability" or "onhiteffectrate") return ReferenceDomain.Mixed;
            if (IsWorkRateResourceField(owner, attribute) &&
                !ProtoConstants.KnownResourceTypes.Contains(attribute.Value.Trim(), StringComparer.OrdinalIgnoreCase)) return ReferenceDomain.Mixed;
        }
        if (IsTechnologyEffect(owner) && EffectKind(owner) == "createunit" && name == "generator") return ReferenceDomain.Mixed;
        return ReferenceDomain.Unknown;
    }

    private static readonly HashSet<string> ProtoUnitElementNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "train", "projectile", "conversionprotoid", "attachprotounit", "activevfxproto",
        "modifyprotoid", "partisantype", "disguiseprotoid", "deadreplacement", "deadtransform",
        "eidolonprotoid", "nonsocketplaceprotoid", "locationprotoid", "fromprotoid", "toprotoid",
        "dependentunit", "generator", "spawn", "unit"
    };

    private static readonly HashSet<string> UnitTypeElementNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "unittype", "modifyabstracttype", "forbidabstracttype", "forbidunittype", "targettype",
        "targetunittype", "queryunittype", "attackertype", "contributorid", "rewardtype",
        "socketunittype", "modifyunittype", "castpowertargettype", "modifydamagetargettype",
        "abstractplacementtargettype", "explicitlyrestrictedplacementtargettype"
    };

    private static readonly HashSet<string> ProtoUnitAttributeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "proto", "unit", "generator", "fromprotoid", "toprotoid", "locationprotoid",
        "waterprotounit", "modifyprotoid"
    };

    private static readonly HashSet<string> UnitTypeAttributeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "unittype", "targettype", "targetunittype", "queryunittype", "attackertype",
        "contributorid", "rewardtype", "forbidunittype", "modifyabstracttype", "forbidabstracttype",
        "attacktype", "ignoretype"
    };

    private static readonly HashSet<string> TypeAttributeReferenceOwners = new(StringComparer.OrdinalIgnoreCase)
    {
        "damagebonus", "typedanim", "modifyabstracttype", "forbidabstracttype", "forbidunittype",
        "unittype", "rate"
    };

    private static readonly HashSet<string> ProtoActionReferenceElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "selfdestructprotoaction", "birthprotoaction", "stackprotoaction", "areaprotoaction",
        "stackaddaction", "stacksubaction"
    };

    private static readonly HashSet<string> AbilityReferenceElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "ability", "castpower", "power", "protopower"
    };

    private static readonly HashSet<string> CommandReferenceElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "command", "optionalcommand", "transformcommand", "prereqcommand", "sharedcommand",
        "removecommandprequeueonprequeue"
    };

    private static readonly HashSet<string> MajorGodMixedUnitReferenceElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "targetunit", "anchor", "chainablebuilding", "excludedtarget", "reward", "excludefromreward"
    };

    private static readonly HashSet<string> MajorGodMixedUnitReferenceAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "preventplacement", "landbuilding", "waterbuilding", "attackertype"
    };

    public static int RenameReferences(
        XContainer document,
        GameIdentifierKind kind,
        string oldName,
        string newName)
    {
        ArgumentNullException.ThrowIfNull(document);
        var oldValue = oldName.Trim();
        var newValue = newName.Trim();
        if (oldValue.Length == 0 || newValue.Length == 0 ||
            oldValue.Equals(newValue, StringComparison.OrdinalIgnoreCase))
            return 0;

        var changed = 0;
        if (kind == GameIdentifierKind.UnitType)
            changed += RenameProtoActionDynamicUnitTypeElements(document, oldValue, newValue);
        foreach (var element in document.Descendants())
        {
            if (IsGeneralAbilityAssociation(element, kind) && ElementContainsIdentifier(element, oldValue, kind))
            {
                var text = element.Nodes().OfType<XText>().Single(node => !string.IsNullOrWhiteSpace(node.Value));
                var leading = text.Value.Length - text.Value.TrimStart().Length;
                var trailing = text.Value.Length - text.Value.TrimEnd().Length;
                text.Value = text.Value[..leading] + newValue + (trailing == 0 ? "" : text.Value[^trailing..]);
                changed++;
            }
            else if (IsPowerCreateUnitList(element, kind))
            {
                var tokens = element.Value.Split('|');
                var replaced = false;
                for (var i = 0; i < tokens.Length; i++)
                {
                    var token = tokens[i];
                    if (!EqualsIdentifier(token, oldValue)) continue;
                    var start = token.Length - token.TrimStart().Length;
                    tokens[i] = token[..start] + newValue + token[(start + token.Trim().Length)..];
                    replaced = true;
                }
                if (replaced) { element.Value = string.Join('|', tokens); changed++; }
            }
            else if (IsReferenceElement(element, kind) && EqualsIdentifier(element.Value, oldValue, kind))
            {
                element.Value = NormalizeReplacement(newValue, kind);
                changed++;
            }

            foreach (var attribute in element.Attributes())
            {
                if (!IsReferenceAttribute(element, attribute, kind) ||
                    !AttributeContainsIdentifier(attribute, oldValue, kind))
                    continue;
                if (IsExclusionAttribute(attribute))
                {
                    changed += RenamePipeSeparatedIdentifier(attribute, oldValue, newValue);
                    continue;
                }
                attribute.Value = NormalizeReplacement(newValue, kind);
                changed++;
            }
        }

        return changed;
    }

    public static IReadOnlyList<string> FindRecognizedReferences(
        XContainer document,
        GameIdentifierKind kind,
        string identifier)
    {
        ArgumentNullException.ThrowIfNull(document);
        var value = identifier.Trim();
        if (value.Length == 0)
            return [];

        var result = new List<string>();
        if (kind == GameIdentifierKind.UnitType)
        {
            result.AddRange(document.Descendants().Where(element =>
                    IsDynamicUnitTypeReference(element) &&
                    element.Name.LocalName.Equals(value, StringComparison.OrdinalIgnoreCase))
                .Select(element => $"<{element.Parent!.Name.LocalName}>/<{element.Name.LocalName}>"));
        }

        foreach (var element in document.Descendants())
        {
            if (IsReferenceElement(element, kind) && ElementContainsIdentifier(element, value, kind))
                result.Add($"<{element.Name.LocalName}>");
            result.AddRange(element.Attributes().Where(attribute =>
                    IsReferenceAttribute(element, attribute, kind) && AttributeContainsIdentifier(attribute, value, kind))
                .Select(attribute => $"<{element.Name.LocalName}>/@{attribute.Name.LocalName}"));
        }
        return result;
    }

    private static bool IsDynamicUnitTypeReference(XElement element)
        => element.Parent is { } parent && IsPlainXml(element) &&
           (parent.Name.LocalName.Equals("empowerdata", StringComparison.OrdinalIgnoreCase) ||
            parent.Name.LocalName.Equals("enemyempowerdata", StringComparison.OrdinalIgnoreCase) ||
            parent.Name.LocalName.Equals("natureempowerdata", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Display-only bulk equivalent of recognized Unit Type lookup. The caller
    /// supplies its case-insensitive catalog set; classification stays here.
    /// Visits each field once rather than rescanning the owner for every type.
    /// </summary>
    internal static IReadOnlySet<string> FindRecognizedUnitTypeIdentifiers(
        XContainer document, IReadOnlySet<string> knownTypes)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var element in document.Descendants())
        {
            if (knownTypes.Contains(element.Name.LocalName) && IsDynamicUnitTypeReference(element))
                result.Add(element.Name.LocalName);
            if (!element.HasElements)
            {
                var value = element.Value.Trim();
                if (knownTypes.Contains(value) && IsReferenceElement(element, GameIdentifierKind.UnitType))
                    result.Add(value);
            }
            foreach (var attribute in element.Attributes())
            {
                if (IsExclusionAttribute(attribute))
                {
                    var candidates = attribute.Value.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                        .Where(knownTypes.Contains).ToArray();
                    if (candidates.Length > 0 && IsReferenceAttribute(element, attribute, GameIdentifierKind.UnitType))
                        result.UnionWith(candidates);
                }
                else
                {
                    var value = attribute.Value.Trim();
                    if (knownTypes.Contains(value) && IsReferenceAttribute(element, attribute, GameIdentifierKind.UnitType))
                        result.Add(value);
                }
            }
        }
        return result;
    }

    public static IReadOnlyList<GameIdentifierReferenceLocation> FindDeletionReferenceLocations(
        XContainer document,
        GameIdentifierKind kind,
        string identifier)
    {
        ArgumentNullException.ThrowIfNull(document);
        var value = identifier.Trim();
        if (value.Length == 0)
            return [];

        var result = new List<GameIdentifierReferenceLocation>();
        if (kind == GameIdentifierKind.UnitType)
        {
            var empowerSectionNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "empowerdata", "enemyempowerdata", "natureempowerdata"
            };
            result.AddRange(document.Descendants().Where(element =>
                    element.Parent != null &&
                    IsPlainXml(element) && empowerSectionNames.Contains(element.Parent.Name.LocalName) &&
                    element.Name.LocalName.Equals(value, StringComparison.OrdinalIgnoreCase))
                .Select(element => new GameIdentifierReferenceLocation(
                    FindReferenceOwner(element),
                    $"<{element.Parent!.Name.LocalName}>/<{element.Name.LocalName}>",
                    true)));
        }

        foreach (var element in document.Descendants())
        {
            var recognizedElement = IsReferenceElement(element, kind) &&
                                    ElementContainsIdentifier(element, value, kind);
            if (recognizedElement)
            {
                result.Add(new GameIdentifierReferenceLocation(
                    FindReferenceOwner(element), $"<{element.Name.LocalName}>", true));
            }
            else if (!element.HasElements &&
                     ElementContainsIdentifier(element, value, kind) &&
                     !IsDefinitionElement(element, kind) && !IsUnrelatedElement(element, kind))
            {
                result.Add(new GameIdentifierReferenceLocation(
                    FindReferenceOwner(element),
                    $"possible <{element.Name.LocalName}> reference",
                    false));
            }

            foreach (var attribute in element.Attributes())
            {
                var recognizedAttribute = IsReferenceAttribute(element, attribute, kind) &&
                                          AttributeContainsIdentifier(attribute, value, kind);
                if (recognizedAttribute)
                {
                    result.Add(new GameIdentifierReferenceLocation(
                        FindReferenceOwner(element),
                        $"<{element.Name.LocalName}>/@{attribute.Name.LocalName}",
                        true));
                }
                else if (AttributeContainsIdentifier(attribute, value, kind) &&
                         !IsKnownExclusion(attribute) &&
                         !IsDefinitionAttribute(element, attribute, kind) && !IsUnrelatedAttribute(element, attribute, kind))
                {
                    result.Add(new GameIdentifierReferenceLocation(
                        FindReferenceOwner(element),
                        $"possible <{element.Name.LocalName}>/@{attribute.Name.LocalName} reference",
                        false));
                }

            }
        }

        return result;
    }

    private static string FindReferenceOwner(XElement element)
    {
        foreach (var candidate in element.AncestorsAndSelf())
        {
            var elementName = candidate.Name.LocalName;
            if (elementName.Equals("unit", StringComparison.OrdinalIgnoreCase) ||
                elementName.Equals("tech", StringComparison.OrdinalIgnoreCase) ||
                elementName.Equals("power", StringComparison.OrdinalIgnoreCase) ||
                elementName.Equals("majorgod", StringComparison.OrdinalIgnoreCase) ||
                elementName.Equals("civ", StringComparison.OrdinalIgnoreCase))
            {
                var name = GetAttribute(candidate, "name")?.Value.Trim();
                name ??= candidate.Elements().FirstOrDefault(child =>
                    child.Name.LocalName.Equals("name", StringComparison.OrdinalIgnoreCase))?.Value.Trim();
                if (!string.IsNullOrWhiteSpace(name))
                    return name;
            }

            if (elementName.Equals("protounitcommand", StringComparison.OrdinalIgnoreCase) ||
                elementName.Equals("transform", StringComparison.OrdinalIgnoreCase) ||
                elementName.Equals("action", StringComparison.OrdinalIgnoreCase))
            {
                var name = candidate.Elements().FirstOrDefault(child =>
                    child.Name.LocalName.Equals("name", StringComparison.OrdinalIgnoreCase) ||
                    child.Name.LocalName.Equals("command", StringComparison.OrdinalIgnoreCase))?.Value.Trim();
                if (!string.IsNullOrWhiteSpace(name))
                    return name;
            }
        }
        return "";
    }

    public static int RenameProtoActionReferences(XElement unit, string oldName, string newName)
    {
        ArgumentNullException.ThrowIfNull(unit);
        var oldValue = oldName.Trim();
        var newValue = newName.Trim();
        if (oldValue.Length == 0 || newValue.Length == 0 ||
            oldValue.Equals(newValue, StringComparison.OrdinalIgnoreCase))
            return 0;

        var changed = 0;
        foreach (var element in unit.Descendants().Where(element =>
                     ProtoActionReferenceElements.Contains(element.Name.LocalName) &&
                     EqualsIdentifier(element.Value, oldValue)))
        {
            element.Value = newValue;
            changed++;
        }

        foreach (var attribute in unit.Descendants()
                     .Where(element => element.Name.LocalName.Equals("onhiteffect", StringComparison.OrdinalIgnoreCase))
                     .Attributes()
                     .Where(attribute => attribute.Name.LocalName.Equals("protoaction", StringComparison.OrdinalIgnoreCase) &&
                                         EqualsIdentifier(attribute.Value, oldValue)))
        {
            attribute.Value = newValue;
            changed++;
        }
        return changed;
    }

    public static IReadOnlyList<string> FindUnrecognizedProtoActionReferences(
        XElement unit,
        string actionName)
    {
        ArgumentNullException.ThrowIfNull(unit);
        var recognized = new HashSet<XObject>(ReferenceEqualityComparer.Instance);
        foreach (var element in unit.Descendants().Where(element =>
                     ProtoActionReferenceElements.Contains(element.Name.LocalName)))
            recognized.Add(element);
        foreach (var attribute in unit.Descendants()
                     .Where(element => element.Name.LocalName.Equals("onhiteffect", StringComparison.OrdinalIgnoreCase))
                     .Attributes()
                     .Where(attribute => attribute.Name.LocalName.Equals("protoaction", StringComparison.OrdinalIgnoreCase)))
            recognized.Add(attribute);

        var unknown = new List<string>();
        foreach (var element in unit.Descendants().Where(element =>
                     !element.HasElements &&
                     !recognized.Contains(element) &&
                     element.Name.LocalName.Contains("action", StringComparison.OrdinalIgnoreCase) &&
                     EqualsIdentifier(element.Value, actionName)))
            unknown.Add($"<{element.Name.LocalName}>");
        foreach (var attribute in unit.Descendants().Attributes().Where(attribute =>
                     !recognized.Contains(attribute) &&
                     attribute.Name.LocalName.Contains("action", StringComparison.OrdinalIgnoreCase) &&
                     EqualsIdentifier(attribute.Value, actionName)))
            unknown.Add($"@{attribute.Name.LocalName}");
        return unknown.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static IReadOnlyList<string> FindUnrecognizedTechTypeReferences(
        XContainer techtree,
        string techTypeName)
    {
        ArgumentNullException.ThrowIfNull(techtree);
        var recognized = new HashSet<XObject>(ReferenceEqualityComparer.Instance);
        foreach (var element in techtree.Descendants().Where(element =>
                     element.Name.LocalName.Equals("techtype", StringComparison.OrdinalIgnoreCase)))
            recognized.Add(element);
        foreach (var effect in techtree.Descendants().Where(element =>
                     element.Name.LocalName.Equals("effect", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var attribute in effect.Attributes().Where(attribute =>
                         attribute.Name.LocalName.Equals("techtype", StringComparison.OrdinalIgnoreCase)))
                recognized.Add(attribute);
            foreach (var target in effect.Elements().Where(element =>
                         element.Name.LocalName.Equals("target", StringComparison.OrdinalIgnoreCase)))
            {
                if (GetAttribute(target, "type")?.Value.Equals("TechType", StringComparison.OrdinalIgnoreCase) == true)
                    recognized.Add(target);
                var exclusions = GetAttribute(target, "excludetypes");
                if (exclusions != null)
                    recognized.Add(exclusions);
            }
        }

        var unknown = new List<string>();
        foreach (var element in techtree.Descendants().Where(element =>
                     !element.HasElements &&
                     !recognized.Contains(element) &&
                     element.Name.LocalName.Contains("techtype", StringComparison.OrdinalIgnoreCase) &&
                     EqualsIdentifier(element.Value, techTypeName)))
            unknown.Add($"<{element.Name.LocalName}>");
        foreach (var attribute in techtree.Descendants().Attributes().Where(attribute =>
                     !recognized.Contains(attribute) &&
                     attribute.Name.LocalName.Contains("techtype", StringComparison.OrdinalIgnoreCase) &&
                     EqualsIdentifier(attribute.Value, techTypeName)))
            unknown.Add($"@{attribute.Name.LocalName}");
        return unknown.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static int RenameEffectProtoActionReferences(
        XContainer document,
        ProtoActionRenameOperation operation,
        IEnumerable<string>? unitTypeNames)
    {
        ArgumentNullException.ThrowIfNull(document);
        var oppositeNames = unitTypeNames?.Select(name => name.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var references = new List<XAttribute>();
        foreach (var effect in document.Descendants().Where(element =>
                     element.Name.LocalName.Equals("effect", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var attribute in effect.Attributes().Where(attribute =>
                         attribute.Name.LocalName.ToLowerInvariant() is "action" or "protoaction" or "srcaction" &&
                         EqualsIdentifier(attribute.Value, operation.OldName)))
            {
                var field = attribute.Name.LocalName.ToLowerInvariant();
                var kind = EffectKind(effect);
                // Granted/source actions belong to the source Unit, never the recipient.
                var sourceField = field == "protoaction" && kind == "protoactionadd" ? "unittype" :
                    field == "srcaction" && kind == "assignonhiteffect" ? "srcproto" : null;
                var targets = effect.Elements().Where(e => e.Name.LocalName.Equals("target", StringComparison.OrdinalIgnoreCase)).ToList();
                if (sourceField != null)
                {
                    var owner = GetAttribute(effect, sourceField)?.Value;
                    if (!string.IsNullOrWhiteSpace(owner) && !EqualsIdentifier(owner, operation.UnitName)) continue;
                    if (string.IsNullOrWhiteSpace(owner)) throw UnsafeActionOwner(operation, field);
                }
                else
                {
                    if (targets.Count > 0 && !targets.Any(t => EqualsIdentifier(t.Value, operation.UnitName))) continue;
                    if (field != "action" && !(field == "protoaction" && kind == "selfdestructprotoaction") ||
                        targets.Count == 0 || targets.Any(t => t.Name != "target" || t.HasElements ||
                            !EqualsIdentifier(t.Value, operation.UnitName) ||
                            !string.Equals(GetAttribute(t, "type")?.Value, "ProtoUnit", StringComparison.OrdinalIgnoreCase)) ||
                        oppositeNames == null || oppositeNames.Contains(operation.UnitName.Trim()))
                        throw UnsafeActionOwner(operation, field);
                }

                var tech = effect.Parent?.Name == "effects" ? effect.Parent.Parent : effect.Parent;
                var validTech = IsTechnologyEffect(effect) && tech?.Parent is { } root && root.Parent == null &&
                    (root.Name == "techtree" || root.Name == "techtreemods");
                if ((!validTech && !IsPowerEffect(effect)) || !IsPlainXml(effect) ||
                    attribute.Name.Namespace != XNamespace.None)
                    throw UnsafeActionOwner(operation, field);
                references.Add(attribute);
            }
        }
        // Validate every candidate before mutating even the in-memory document.
        foreach (var reference in references) reference.Value = operation.NewName.Trim();
        return references.Count;
    }

    private static InvalidOperationException UnsafeActionOwner(ProtoActionRenameOperation operation, string field)
        => new($"Action '{operation.OldName}' on Unit '{operation.UnitName}' was not renamed because @{field} " +
               "has an unsupported or ambiguous owner. Check the effect targets, source Unit and Unit Type catalog. No files were written.");

    public static int RenameAttributeReferences(
        XContainer document,
        string attributeName,
        string oldName,
        string newName)
    {
        ArgumentNullException.ThrowIfNull(document);
        var changed = 0;
        foreach (var attribute in document.Descendants().Attributes().Where(attribute =>
                     attribute.Name.LocalName.Equals(attributeName, StringComparison.OrdinalIgnoreCase) &&
                     EqualsIdentifier(attribute.Value, oldName)))
        {
            attribute.Value = newName.Trim();
            changed++;
        }
        return changed;
    }

    public static int RenameElementReferences(
        XContainer document,
        string elementName,
        string oldName,
        string newName)
    {
        ArgumentNullException.ThrowIfNull(document);
        var changed = 0;
        foreach (var element in document.Descendants().Where(element =>
                     !element.HasElements &&
                     element.Name.LocalName.Equals(elementName, StringComparison.OrdinalIgnoreCase) &&
                     EqualsIdentifier(element.Value, oldName)))
        {
            element.Value = newName.Trim();
            changed++;
        }
        return changed;
    }

    public static IReadOnlyList<string> FindUnrecognizedReferences(
        XContainer document,
        GameIdentifierKind kind,
        string identifier)
    {
        ArgumentNullException.ThrowIfNull(document);
        var result = new List<string>();
        foreach (var element in document.Descendants())
        {
            if (!element.HasElements && ElementContainsIdentifier(element, identifier, kind) &&
                (LooksLikeReferenceField(element.Name.LocalName, kind) || !IsPlainXml(element) ||
                 kind == GameIdentifierKind.ProtoUnit && IsTransformUnitFieldName(element.Name.LocalName) &&
                 element.Ancestors().Any(parent => parent.Name.LocalName.Equals("transform", StringComparison.OrdinalIgnoreCase)) ||
                 kind == GameIdentifierKind.Ability && element.Name.LocalName.Equals("target", StringComparison.OrdinalIgnoreCase) &&
                 element.Parent?.Name.LocalName.Equals("duplicatetargetreplacement", StringComparison.OrdinalIgnoreCase) == true) &&
                !IsReferenceElement(element, kind) &&
                !IsUnrelatedElement(element, kind) &&
                !IsDefinitionElement(element, kind))
            {
                result.Add($"<{element.Name.LocalName}>");
            }

            foreach (var attribute in element.Attributes().Where(attribute =>
                         AttributeContainsIdentifier(attribute, identifier, kind) &&
                         (LooksLikeReferenceField(attribute.Name.LocalName, kind) || IsExclusionAttribute(attribute) ||
                          attribute.Name.Namespace != XNamespace.None || !IsPlainXml(element)) &&
                         !IsKnownExclusion(attribute) &&
                         !IsUnrelatedAttribute(element, attribute, kind) &&
                         !IsReferenceAttribute(element, attribute, kind) &&
                         !IsDefinitionAttribute(element, attribute, kind)))
            {
                result.Add($"@{attribute.Name.LocalName}");
            }
        }
        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Finds exact identifier values in XML fields which are not definitions and
    /// are not part of the known schema. Deletion uses this stricter check because
    /// an unknown exact match must be treated as a possible dependency, never as
    /// evidence that an object is unused.
    /// </summary>
    public static IReadOnlyList<string> FindPotentialUnrecognizedReferences(
        XContainer document,
        GameIdentifierKind kind,
        string identifier)
    {
        ArgumentNullException.ThrowIfNull(document);
        var result = new List<string>();
        foreach (var element in document.Descendants())
        {
            if (!element.HasElements &&
                ElementContainsIdentifier(element, identifier, kind) &&
                !IsReferenceElement(element, kind) &&
                !IsUnrelatedElement(element, kind) &&
                !IsDefinitionElement(element, kind))
            {
                result.Add($"possible <{element.Name.LocalName}> reference");
            }

            foreach (var attribute in element.Attributes())
            {
                if (AttributeContainsIdentifier(attribute, identifier, kind) &&
                    !IsKnownExclusion(attribute) &&
                    !IsUnrelatedAttribute(element, attribute, kind) &&
                    !IsReferenceAttribute(element, attribute, kind) &&
                    !IsDefinitionAttribute(element, attribute, kind))
                {
                    result.Add($"possible <{element.Name.LocalName}>/@{attribute.Name.LocalName} reference");
                }
            }
        }
        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool IsDirectPowerField(XElement element)
        => element.Name.Namespace == XNamespace.None &&
           element.Parent is { } power && power.Name.Namespace == XNamespace.None &&
           power.Name.LocalName.Equals("power", StringComparison.OrdinalIgnoreCase) &&
           power.Parent is { } root && root.Parent == null && root.Name.Namespace == XNamespace.None &&
           (root.Name.LocalName.Equals("powers", StringComparison.OrdinalIgnoreCase) ||
            root.Name.LocalName.Equals("powersmod", StringComparison.OrdinalIgnoreCase));

    private static bool IsPowerCreateUnitList(XElement element, GameIdentifierKind kind)
        => kind == GameIdentifierKind.ProtoUnit && !element.HasElements &&
           element.Name.LocalName.Equals("createunit", StringComparison.OrdinalIgnoreCase) && IsDirectPowerField(element);

    internal static bool IsMixedPowerUnitReference(XElement element)
        => !element.HasElements && element.Name.Namespace == XNamespace.None &&
           ((IsDirectPowerField(element) && element.Name.LocalName.ToLowerInvariant() is
               "attracttype" or "modelattachproto" or "killunittype") ||
            (element.Name.LocalName.Equals("abstracttype", StringComparison.OrdinalIgnoreCase) &&
             element.Parent is { } armor && armor.Name == "armor" && IsDirectPowerField(armor)));

    private static bool IsPowerEffect(XElement element)
        => element.Name == "effect" &&
           (IsDirectPowerField(element) ||
            element.Parent is { } effects && effects.Name == "effects" && IsDirectPowerField(effects));

    private static bool IsExclusionAttribute(XAttribute attribute)
        => attribute.Name.LocalName.Equals("excludetypes", StringComparison.OrdinalIgnoreCase);

    private static bool IsPowerUnitExclusion(XAttribute attribute)
        => IsExclusionAttribute(attribute) && attribute.Name.Namespace == XNamespace.None &&
           attribute.Parent is { } owner && owner.Name == "unitmodify" && IsDirectPowerField(owner) &&
           UnitModifyFamilyController.IsPowerType(GetAttribute(owner.Parent!, "type")?.Value.Trim());

    private static bool IsTechnologyTypeExclusion(XAttribute attribute)
    {
        if (!IsExclusionAttribute(attribute) || attribute.Name.Namespace != XNamespace.None ||
            attribute.Parent is not { } target || target.Name != "target" ||
            target.Parent is not { } effect || effect.Name != "effect") return false;
        var tech = effect.Parent?.Name == "effects" ? effect.Parent.Parent : effect.Parent;
        if (tech?.Name != "tech" || tech.Parent is not { } root || root.Parent != null ||
            (root.Name != "techtree" && root.Name != "techtreemods")) return false;
        var targetType = GetAttribute(target, "type")?.Value.Trim();
        // Retain legacy untyped Technology exclusion targets, but not other typed domains.
        return string.IsNullOrEmpty(targetType) ||
               targetType.Equals("TechType", StringComparison.OrdinalIgnoreCase) ||
               targetType.Equals("TechAll", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsKnownExclusion(XAttribute attribute)
        => IsPowerUnitExclusion(attribute) || IsTechnologyTypeExclusion(attribute);

    private static bool AttributeContainsIdentifier(XAttribute attribute, string value, GameIdentifierKind kind)
        => IsExclusionAttribute(attribute)
            ? PipeSeparatedContains(attribute.Value, value)
            : EqualsIdentifier(attribute.Value, value, kind);

    internal static bool HasMixedUnitReference(XContainer document, string identifier)
        => document.Descendants().Any(element =>
            IsReferenceElement(element, GameIdentifierKind.ProtoUnit) &&
            IsReferenceElement(element, GameIdentifierKind.UnitType) && EqualsIdentifier(element.Value, identifier) ||
            element.Attributes().Any(attribute =>
                IsReferenceAttribute(element, attribute, GameIdentifierKind.ProtoUnit) &&
                IsReferenceAttribute(element, attribute, GameIdentifierKind.UnitType) &&
                AttributeContainsIdentifier(attribute, identifier, GameIdentifierKind.ProtoUnit)));

    private static bool IsPlainXml(XElement element)
        => element.AncestorsAndSelf().All(candidate => candidate.Name.Namespace == XNamespace.None);

    private static bool IsTechnologyEffect(XElement element)
        => element.Name == "effect" && IsPlainXml(element) &&
           (element.Parent?.Name == "tech" ||
            element.Parent?.Name == "effects" && element.Parent.Parent?.Name == "tech");

    private static string EffectKind(XElement element)
        => (GetAttribute(element, "type")?.Value.Equals("Data", StringComparison.OrdinalIgnoreCase) == true
            ? GetAttribute(element, "subtype")?.Value : GetAttribute(element, "type")?.Value)?.Trim().ToLowerInvariant() ?? "";

    private static bool IsWorkRateResourceField(XElement owner, XAttribute attribute)
        => attribute.Name.LocalName.Equals("unittype", StringComparison.OrdinalIgnoreCase) &&
           (IsTechnologyEffect(owner) || IsPowerEffect(owner)) && EffectKind(owner) is "workrate" or "minworkrate";

    internal static bool HasResourceModeTransition(XContainer document, string oldName, string newName)
        => ProtoConstants.KnownResourceTypes.Contains(newName.Trim(), StringComparer.OrdinalIgnoreCase) &&
           document.Descendants().Any(owner => IsPlainXml(owner) && owner.Attributes().Any(attribute =>
               attribute.Name.Namespace == XNamespace.None && IsWorkRateResourceField(owner, attribute) &&
               EqualsIdentifier(attribute.Value, oldName) &&
               !ProtoConstants.KnownResourceTypes.Contains(attribute.Value.Trim(), StringComparer.OrdinalIgnoreCase)));

    // Known other domains are preserved, not reported as unknown references. Unknown
    // contexts and extensions must never acquire these exemptions merely by tag name.
    private static bool IsUnrelatedElement(XElement element, GameIdentifierKind kind)
        => IsTransformUnitReference(element) && kind != GameIdentifierKind.ProtoUnit ||
           (Group5ElementDomain(element) is var powerDomain && powerDomain != ReferenceDomain.Unknown && !MatchesDomain(powerDomain, kind)) ||
           (Group3ElementDomain(element) is var domain && domain != ReferenceDomain.Unknown && !MatchesDomain(domain, kind)) ||
           IsPlainXml(element) && kind == GameIdentifierKind.UnitType &&
           (element.Name == "chainablebuilding" && IsWithinMajorGodDefinition(element) ||
            element.Name == "target" && element.Parent is { } effect && IsTechnologyEffect(effect) &&
            EffectKind(effect) == "enable" && GetAttribute(element, "type")?.Value.Equals("ProtoUnit", StringComparison.OrdinalIgnoreCase) == true);

    private static bool IsUnrelatedAttribute(XElement owner, XAttribute attribute, GameIdentifierKind kind)
    {
        if (!IsPlainXml(owner) || attribute.Name.Namespace != XNamespace.None) return false;
        var domain = Group3AttributeDomain(owner, attribute);
        if (domain != ReferenceDomain.Unknown && !MatchesDomain(domain, kind)) return true;
        var name = attribute.Name.LocalName.ToLowerInvariant();
        var effectKind = EffectKind(owner);
        var effect = IsTechnologyEffect(owner) || IsPowerEffect(owner);
        if (kind == GameIdentifierKind.Tactics && name == "tactic" && effect && effectKind == "tacticenable")
            return true;
        if (kind is not (GameIdentifierKind.ProtoUnit or GameIdentifierKind.UnitType)) return false;
        if (IsWorkRateResourceField(owner, attribute) &&
            ProtoConstants.KnownResourceTypes.Contains(attribute.Value.Trim(), StringComparer.OrdinalIgnoreCase)) return true;
        if (name == "rewardtype" && owner.Name == "onhiteffect" &&
            (IsDirectPowerField(owner) || owner.Ancestors().Any(e => e.Name == "protoaction"))) return true;
        if (kind != GameIdentifierKind.UnitType || !effect) return false;
        if (name == "unit" && effectKind == "createunit") return true;
        return name == "unittype" && IsTechnologyEffect(owner) && effectKind is
            "deadtransform" or "placementrulesoverride" or "timeshiftingadd" or "partisanunit" or
            "protoactionadd" or "actionaddattachingunit" or "projectile" or "sharedbuildlimitunit";
    }

    private static bool ElementContainsIdentifier(XElement element, string value, GameIdentifierKind kind)
        => IsGeneralAbilityAssociation(element, kind)
            ? EqualsIdentifier(element.Nodes().OfType<XText>().Single(node => !string.IsNullOrWhiteSpace(node.Value)).Value, value, kind)
            : kind == GameIdentifierKind.ProtoUnit && element.Name.LocalName.Equals("createunit", StringComparison.OrdinalIgnoreCase)
            ? PipeSeparatedContains(element.Value, value)
            : EqualsIdentifier(element.Value, value, kind);

    private static bool IsReferenceElement(XElement element, GameIdentifierKind kind)
    {
        if (IsGeneralAbilityAssociation(element, kind)) return true;
        if (element.HasElements || !IsPlainXml(element) || IsUnrelatedElement(element, kind))
            return false;
        if (MatchesDomain(Group3ElementDomain(element), kind)) return true;
        if (MatchesDomain(Group5ElementDomain(element), kind)) return true;
        if (kind == GameIdentifierKind.ProtoUnit && IsTransformUnitReference(element)) return true;
        if (IsPowerCreateUnitList(element, kind)) return true;
        var name = element.Name.LocalName;
        if ((kind is GameIdentifierKind.ProtoUnit or GameIdentifierKind.UnitType) && IsMixedPowerUnitReference(element))
            return true;
        if (kind == GameIdentifierKind.ProtoUnit && element.Name.Namespace == XNamespace.None &&
            ((IsDirectPowerField(element) && name.ToLowerInvariant() is
                "shockwavevfx" or "treeproto" or "treeeffectproto" or "effectproto" or "vfxproto") ||
             (name.Equals("vfxproto", StringComparison.OrdinalIgnoreCase) &&
              element.Parent is { } armor && armor.Name == "armor" && IsDirectPowerField(armor))))
            return true;
        if (kind == GameIdentifierKind.ProtoUnit &&
            name.Equals("attachproto", StringComparison.OrdinalIgnoreCase) &&
            IsDirectPowerField(element))
            return true;
        if (kind == GameIdentifierKind.ProtoUnit && ProtoUnitElementNames.Contains(name))
            return true;
        if ((kind == GameIdentifierKind.ProtoUnit || kind == GameIdentifierKind.UnitType) &&
            IsWithinMajorGodDefinition(element) && MajorGodMixedUnitReferenceElements.Contains(name))
            return true;
        if (kind == GameIdentifierKind.ProtoUnit && IsWithinMajorGodDefinition(element) &&
            (name.Equals("protounit", StringComparison.OrdinalIgnoreCase) ||
             name.Equals("vfx", StringComparison.OrdinalIgnoreCase)))
            return true;
        if (kind == GameIdentifierKind.ProtoUnit && UnitTypeElementNames.Contains(name) && IsTechnologyField(element))
            return true;
        if (kind == GameIdentifierKind.UnitType && UnitTypeElementNames.Contains(name))
            return true;
        if (kind == GameIdentifierKind.TechType &&
            name.Equals("techtype", StringComparison.OrdinalIgnoreCase) &&
            !IsDefinitionElement(element, kind))
            return true;
        if (kind == GameIdentifierKind.Culture &&
            (name.Equals("culture", StringComparison.OrdinalIgnoreCase) ||
             name.Equals("culturename", StringComparison.OrdinalIgnoreCase)))
            return true;
        if (kind == GameIdentifierKind.MajorGod &&
            (name.Equals("majorgod", StringComparison.OrdinalIgnoreCase) ||
             name.Equals("civname", StringComparison.OrdinalIgnoreCase)))
            return true;
        if (kind == GameIdentifierKind.Ability && AbilityReferenceElements.Contains(name))
            return !(name.Equals("power", StringComparison.OrdinalIgnoreCase) &&
                     GetAttribute(element, "name") != null);
        if (kind == GameIdentifierKind.ProtoUnitCommand && CommandReferenceElements.Contains(name))
            return !IsDefinitionElement(element, kind);
        if (kind == GameIdentifierKind.Tactics && name.Equals("tactics", StringComparison.OrdinalIgnoreCase))
            return true;

        if (!name.Equals("target", StringComparison.OrdinalIgnoreCase))
            return false;
        if (kind == GameIdentifierKind.TechType)
            return GetAttribute(element, "type")?.Value.Equals("TechType", StringComparison.OrdinalIgnoreCase) == true;
        if (kind == GameIdentifierKind.UnitType && element.Ancestors().Any(ancestor =>
                ancestor.Name.LocalName.Equals("protoaction", StringComparison.OrdinalIgnoreCase)))
            return true;
        var targetType = GetAttribute(element, "type")?.Value.Trim();
        return (kind is GameIdentifierKind.ProtoUnit or GameIdentifierKind.UnitType) &&
               targetType?.Equals("ProtoUnit", StringComparison.OrdinalIgnoreCase) == true;
    }

    // General ability records store their identity as direct text beside editable child fields.
    private static bool IsGeneralAbilityAssociation(XElement element, GameIdentifierKind kind)
        => kind == GameIdentifierKind.Ability && IsPlainXml(element) &&
           element.Name.LocalName.Equals("ability", StringComparison.OrdinalIgnoreCase) &&
           element.Parent?.Parent is { } root && root.Name.Namespace == XNamespace.None &&
           (root.Name.LocalName.Equals("abilitiesmods", StringComparison.OrdinalIgnoreCase) ||
            root.Name.LocalName.Equals("abilities", StringComparison.OrdinalIgnoreCase)) &&
           element.Nodes().OfType<XText>().Count(node => !string.IsNullOrWhiteSpace(node.Value)) == 1;

    private static bool IsReferenceAttribute(
        XElement owner,
        XAttribute attribute,
        GameIdentifierKind kind)
    {
        if (!IsPlainXml(owner) || attribute.Name.Namespace != XNamespace.None ||
            IsUnrelatedAttribute(owner, attribute, kind)) return false;
        if (MatchesDomain(Group3AttributeDomain(owner, attribute), kind)) return true;
        var name = attribute.Name.LocalName;
        if (IsExclusionAttribute(attribute))
            return kind == GameIdentifierKind.TechType && IsTechnologyTypeExclusion(attribute) ||
                   (kind is GameIdentifierKind.ProtoUnit or GameIdentifierKind.UnitType) && IsPowerUnitExclusion(attribute);
        if (kind == GameIdentifierKind.ProtoUnit && attribute.Name.Namespace == XNamespace.None &&
            ((name.Equals("swapto", StringComparison.OrdinalIgnoreCase) && owner.Name == "swap" && IsDirectPowerField(owner)) ||
             (name.Equals("srcproto", StringComparison.OrdinalIgnoreCase) && IsPowerEffect(owner) &&
              GetAttribute(owner, "type")?.Value.Equals("AssignOnHitEffect", StringComparison.OrdinalIgnoreCase) == true)))
            return true;
        if (kind == GameIdentifierKind.ProtoUnit && ProtoUnitAttributeNames.Contains(name))
            return !(owner.Name.LocalName.Equals("unit", StringComparison.OrdinalIgnoreCase) &&
                     name.Equals("unit", StringComparison.OrdinalIgnoreCase));
        if ((kind == GameIdentifierKind.ProtoUnit || kind == GameIdentifierKind.UnitType) &&
            IsWithinMajorGodDefinition(owner) && MajorGodMixedUnitReferenceAttributes.Contains(name))
            return true;
        if (kind == GameIdentifierKind.ProtoUnit && IsWithinMajorGodDefinition(owner) &&
            (name.Equals("protounit", StringComparison.OrdinalIgnoreCase) ||
             name.Equals("vfx", StringComparison.OrdinalIgnoreCase) ||
             name.Equals("small", StringComparison.OrdinalIgnoreCase) ||
             name.Equals("medium", StringComparison.OrdinalIgnoreCase) ||
             name.Equals("large", StringComparison.OrdinalIgnoreCase) ||
             name.Equals("asspawnedunit", StringComparison.OrdinalIgnoreCase) ||
             owner.Name.LocalName.Equals("contributor", StringComparison.OrdinalIgnoreCase) &&
             name.Equals("unit", StringComparison.OrdinalIgnoreCase)))
            return true;
        if (kind == GameIdentifierKind.UnitType && IsWithinMajorGodDefinition(owner) &&
            owner.Name.LocalName.Equals("contributor", StringComparison.OrdinalIgnoreCase) &&
            name.Equals("type", StringComparison.OrdinalIgnoreCase))
            return true;
        if (kind == GameIdentifierKind.ProtoUnit && UnitTypeAttributeNames.Contains(name) && IsTechnologyField(owner))
            return true;
        if (kind == GameIdentifierKind.UnitType && UnitTypeAttributeNames.Contains(name))
            return true;
        if (kind == GameIdentifierKind.TechType && name.Equals("techtype", StringComparison.OrdinalIgnoreCase))
            return true;
        if (kind == GameIdentifierKind.Culture && name.Equals("culture", StringComparison.OrdinalIgnoreCase))
            return true;
        if (kind == GameIdentifierKind.MajorGod &&
            (name.Equals("civname", StringComparison.OrdinalIgnoreCase) ||
             name.Equals("uishowifmajorgod", StringComparison.OrdinalIgnoreCase) ||
             name.Equals("civ", StringComparison.OrdinalIgnoreCase) && IsTechnologyField(owner)))
            return true;
        if (kind == GameIdentifierKind.Ability &&
            (name.Equals("ability", StringComparison.OrdinalIgnoreCase) ||
             name.Equals("power", StringComparison.OrdinalIgnoreCase) ||
             name.Equals("protopower", StringComparison.OrdinalIgnoreCase)))
            return !IsDefinitionAttribute(owner, attribute, kind);
        if (kind == GameIdentifierKind.ProtoUnitCommand && name.Equals("command", StringComparison.OrdinalIgnoreCase))
            return true;
        // @tactic denotes an internal tactic, not a .tactics file. Unknown contexts
        // are refused by FindUnrecognizedReferences rather than globally rewritten.
        if (kind == GameIdentifierKind.UnitType &&
            name.Equals("unit", StringComparison.OrdinalIgnoreCase) &&
            IsTechnologyField(owner))
            return true;
        return kind == GameIdentifierKind.UnitType &&
               name.Equals("type", StringComparison.OrdinalIgnoreCase) &&
               TypeAttributeReferenceOwners.Contains(owner.Name.LocalName);
    }

    private static int RenameProtoActionDynamicUnitTypeElements(
        XContainer document,
        string oldName,
        string newName)
    {
        var changed = 0;
        var empowerSectionNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "empowerdata", "enemyempowerdata", "natureempowerdata"
        };
        foreach (var target in document.Descendants().Where(element =>
                     element.Parent != null &&
                     IsPlainXml(element) && empowerSectionNames.Contains(element.Parent.Name.LocalName) &&
                     element.Name.LocalName.Equals(oldName, StringComparison.OrdinalIgnoreCase)))
        {
            target.Name = target.Name.Namespace + newName;
            changed++;
        }
        return changed;
    }

    private static XAttribute? GetAttribute(XElement element, string name)
        => element.Attributes().FirstOrDefault(attribute =>
            attribute.Name.Namespace == XNamespace.None && attribute.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static bool IsTechnologyField(XElement element)
        => element.AncestorsAndSelf().Any(candidate =>
            candidate.Name.LocalName.Equals("tech", StringComparison.OrdinalIgnoreCase) &&
            GetAttribute(candidate, "name") != null);

    private static bool IsWithinMajorGodDefinition(XElement element)
        => element.AncestorsAndSelf().Any(candidate =>
            candidate.Name.LocalName.Equals("civ", StringComparison.OrdinalIgnoreCase) &&
            candidate.Elements().Any(child => child.Name.LocalName.Equals("name", StringComparison.OrdinalIgnoreCase)));

    private static bool IsDefinitionElement(XElement element, GameIdentifierKind kind)
    {
        if (!IsPlainXml(element)) return false;
        // Definition catalogs occupy a separate identifier namespace. A Unit and
        // an abstract Unit Type may legitimately share a name, so catalog entries
        // must not be reported as suspicious ProtoUnit references.
        if (element.Name.LocalName.Equals("abstractunittype", StringComparison.OrdinalIgnoreCase) &&
            element.Parent?.Name.LocalName.Equals("abstractunittypesmods", StringComparison.OrdinalIgnoreCase) == true)
            return true;
        if (element.Name.LocalName.Equals("techtype", StringComparison.OrdinalIgnoreCase) &&
            element.Parent?.Name.LocalName.Equals("techtypesmods", StringComparison.OrdinalIgnoreCase) == true)
            return true;

        return kind switch
        {
            GameIdentifierKind.UnitType => element.Name.LocalName.Equals("abstractunittype", StringComparison.OrdinalIgnoreCase),
            GameIdentifierKind.Culture => element.Name.LocalName.Equals("name", StringComparison.OrdinalIgnoreCase) &&
                                          element.Parent?.Name.LocalName.Equals("culture", StringComparison.OrdinalIgnoreCase) == true,
            GameIdentifierKind.MajorGod => element.Name.LocalName.Equals("name", StringComparison.OrdinalIgnoreCase) &&
                                           element.Parent?.Name.LocalName.Equals("civ", StringComparison.OrdinalIgnoreCase) == true,
            GameIdentifierKind.ProtoUnitCommand => element.Name.LocalName.Equals("name", StringComparison.OrdinalIgnoreCase) &&
                                                   element.Parent?.Name.LocalName.Equals("protounitcommand", StringComparison.OrdinalIgnoreCase) == true,
            _ => false
        };
    }

    private static bool IsDefinitionAttribute(
        XElement owner,
        XAttribute attribute,
        GameIdentifierKind kind)
        => IsPlainXml(owner) && attribute.Name.Namespace == XNamespace.None && (kind == GameIdentifierKind.ProtoUnit &&
           owner.Name.LocalName.Equals("unit", StringComparison.OrdinalIgnoreCase) &&
           attribute.Name.LocalName.Equals("name", StringComparison.OrdinalIgnoreCase) ||
           kind == GameIdentifierKind.Ability &&
           owner.Name.LocalName.Equals("power", StringComparison.OrdinalIgnoreCase) &&
           attribute.Name.LocalName.Equals("name", StringComparison.OrdinalIgnoreCase));

    private static bool LooksLikeReferenceField(string name, GameIdentifierKind kind)
        => kind == GameIdentifierKind.ProtoUnit
            ? name.Contains("proto", StringComparison.OrdinalIgnoreCase) ||
              name.Contains("unit", StringComparison.OrdinalIgnoreCase) ||
              name.Contains("projectile", StringComparison.OrdinalIgnoreCase) ||
              name.Contains("spawn", StringComparison.OrdinalIgnoreCase) ||
              name.Contains("generator", StringComparison.OrdinalIgnoreCase)
            : kind == GameIdentifierKind.UnitType
                ? name.Contains("unittype", StringComparison.OrdinalIgnoreCase) ||
                  name.Contains("abstracttype", StringComparison.OrdinalIgnoreCase) ||
                  name.Equals("type", StringComparison.OrdinalIgnoreCase) ||
                  name.Equals("unit", StringComparison.OrdinalIgnoreCase)
                : kind == GameIdentifierKind.TechType
                    ? name.Contains("techtype", StringComparison.OrdinalIgnoreCase)
                    : kind == GameIdentifierKind.Culture
                        ? name.Contains("culture", StringComparison.OrdinalIgnoreCase)
                    : kind == GameIdentifierKind.MajorGod
                        ? name.Contains("majorgod", StringComparison.OrdinalIgnoreCase) ||
                          name.Contains("civname", StringComparison.OrdinalIgnoreCase)
                    : kind == GameIdentifierKind.Ability
                        ? name.Contains("ability", StringComparison.OrdinalIgnoreCase) ||
                          name.Contains("power", StringComparison.OrdinalIgnoreCase)
                        : kind == GameIdentifierKind.ProtoUnitCommand
                            ? name.Contains("command", StringComparison.OrdinalIgnoreCase)
                            : name.Contains("tactic", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeReplacement(string value, GameIdentifierKind kind)
        => kind == GameIdentifierKind.Tactics && value.EndsWith(".tactics", StringComparison.OrdinalIgnoreCase)
            ? value[..^".tactics".Length]
            : value;

    private static bool EqualsIdentifier(string? value, string expected, GameIdentifierKind kind)
        => kind == GameIdentifierKind.Tactics
            ? NormalizeReplacement(value?.Trim() ?? "", kind)
                .Equals(NormalizeReplacement(expected.Trim(), kind), StringComparison.OrdinalIgnoreCase)
            : EqualsIdentifier(value, expected);

    private static bool PipeSeparatedContains(string value, string expected)
        => value.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Contains(expected.Trim(), StringComparer.OrdinalIgnoreCase);

    private static int RenamePipeSeparatedIdentifier(XAttribute attribute, string oldName, string newName)
    {
        var values = attribute.Value.Split('|');
        var changed = false;
        for (var index = 0; index < values.Length; index++)
        {
            if (!values[index].Trim().Equals(oldName.Trim(), StringComparison.OrdinalIgnoreCase))
                continue;
            var token = values[index];
            var start = token.Length - token.TrimStart().Length;
            values[index] = token[..start] + newName.Trim() + token[(start + token.Trim().Length)..];
            changed = true;
        }
        if (changed)
            attribute.Value = string.Join("|", values);
        return changed ? 1 : 0;
    }

    private static bool EqualsIdentifier(string? value, string expected)
        => value?.Trim().Equals(expected.Trim(), StringComparison.OrdinalIgnoreCase) == true;
}
