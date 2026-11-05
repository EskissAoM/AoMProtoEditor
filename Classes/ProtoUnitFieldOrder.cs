namespace AoMDivineDataEditor.Classes;

/// <summary>Fixed form groups, independent of menu sorting, wrapping or edit timing.</summary>
internal static class ProtoUnitFieldOrder
{
    internal sealed record OptionalGroup(string Key, params string[] Tags);

    // This sequence is also the form's existing module construction sequence.
    internal static readonly IReadOnlyList<OptionalGroup> OptionalGroups = Array.AsReadOnly<OptionalGroup>([
        new("selectionradius", "selectionradiusx", "selectionradiusz"),
        new("autoattackrange", "autoattackrange"), new("lifespan", "lifespan"),
        new("resourcesubtype", "resourcesubtype"),
        new("minimapvisuals", "minimapicon", "minimapsize", "minimapcolor"),
        new("resourcepriority", "resourcepriority"), new("hotkeycontext", "hotkeycontext"),
        new("allyhotkeycontext", "allyhotkeycontext"), new("resourcedecay", "resourcedecay"),
        new("culture", "culture"), new("wanderdistance", "wanderdistance"),
        new("workersoftlimit", "workersoftlimit"), new("formationorder", "formationorder"),
        new("screenshakeondestruction", "screenshakeondestruction"),
        new("initialunitaistance", "initialunitaistance"), new("displayedrange", "displayedrange"),
        new("aistancebasedistance", "aistancebasedistance"),
        new("heighthitpointbaroffset", "heighthitpointbaroffset"),
        new("allowedheightvariance", "allowedheightvariance"), new("creationfade", "creationfadetime"),
        new("stealth", "stealthdetectionradius", "stealthrevealselfradius", "stealthshowsilhouetteradius"),
        new("selfdestructprotoaction", "selfdestructprotoaction"),
        // Its form choice remains here; XML is a member of the Commands section.
        new("transformcommand"), new("pathabilityflags", "pathabilityflags"),
        new("heightbobdata", "heightbob"), new("birthprotoaction", "birthprotoaction"),
        new("placementfile", "placementfile"), new("ondiscoverlos", "ondiscoverlos"),
        new("populationcapaddition", "populationcapaddition"), new("gathererlimit", "gathererlimit"),
        new("prioritybonusfactor", "prioritybonusfactor"), new("buildingworkrate", "buildingworkrate"),
        new("trainingrate", "trainingrate"), new("gatherratemultiplier", "gatherratemultiplier"),
        new("partisans", "partisantype", "partisancount"),
        new("bloodandbones", "bloodgroupoverride", "bloodscalemodify", "bonescalemodify"),
        new("decaytime", "decaytime"), new("decaydelaytime", "decaydelaytime"),
        new("researchrate", "researchrate"), new("killrewarddata", "killreward"),
        new("resourcereturn", "ResourceReturn"), new("resourcereturnrate", "ResourceReturnRate"),
        new("autobuildrate", "autobuildrate"), new("godpowerblockradius", "godpowerblockradius"),
        new("godpowercostfactor", "godpowercostfactor"), new("builderlimit", "builderlimit"),
        new("corpsedecaydelay", "corpsedecaydelay"), new("costescalation", "costescalation"),
        new("damageshading", "damageshading"), new("initialshading", "initialshading"),
        new("deadreplacement", "deadreplacement"), new("deadtransform", "deadtransform"),
        new("dependentunitdata", "dependentunit"), new("eidolonprotoid", "eidolonprotoid"),
        new("enemyshortrollovertextid", "enemyshortrollovertextid"),
        new("socketdata", "socketunittype", "nonsocketplaceprotoid"),
        new("disguiseprotoid", "disguiseprotoid"), new("spawndata", "spawn"),
        new("veterancydata", "veterancyranks", "veterancybonus"),
        new("stackprotoaction", "stackprotoaction"),
        new("dodgechance", "dodgechance", "dodgesoundset", "dodgemessageid"),
        new("directionalarmor", "directionalarmor"),
        new("placementobstruction", "placementobstructionradiusx", "placementobstructionradiusz"),
        new("farming", "farmingradiusx", "farmingradiusz", "farmingobstructionradiusx", "farmingobstructionradiusz", "farmingnumstops"),
        new("carrycapacity", "carrycapacity"), new("initialresource", "initialresource"),
        new("resourceconversion", "resourceconversion"), new("conversionresistance", "conversionresistance"),
        new("respawntraindata", "respawntraindata"), new("sharedselectionunittypes", "sharedselectionunittypes"),
        new("decay", "decay"),
        new("recharge", "rechargetime", "recharge", "rechargeincludetypes", "rechargeexcludetypes"),
        new("auxrecharge", "auxrechargetime", "auxrecharge", "auxrechargeincludetypes", "auxrechargeexcludetypes"),
        new("replacement", "replacement"), new("ondamagemodifiers", "OnDamageModifiers")
    ]);

    internal static readonly IReadOnlyDictionary<string, int> Ranks = BuildRanks();

    private static IReadOnlyDictionary<string, int> BuildRanks()
    {
        var ranks = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var next = 0;
        void Fields(params string[] tags)
        {
            foreach (var tag in tags) ranks.Add(tag, next++);
        }
        Fields("displaynameid", "editornameid", "rollovertextid", "shortrollovertextid");
        Fields("icon", "animfile", "soundsetfile");
        Fields("obstructionradiusx", "obstructionradiusz", "turnrate");
        Fields("maxhitpoints", "initialhitpoints", "unitregen");
        Fields("maxshieldpoints", "initialshieldpoints", "unitshieldregen");
        Fields("trainpoints", "buildpoints", "populationcount", "los");
        Fields("unitaitype", "impacttype", "weightclass", "movementtype", "maxvelocity", "maxrunvelocity");
        Fields("buildlimit", "dynamicbuildlimitunittypes", "sharedbuildlimitunittypes");
        Fields("maxcontained", "contain", "notcontain", "containedhitpointbonus", "containedspeedbonus", "containedregenrate");
        foreach (var group in OptionalGroups) Fields(group.Tags);
        // Original-only field has no optional add-menu choice.
        Fields("projectilespinperiod");
        Fields("cost", "armor", "unittype", "flag", "train", "tech");
        // Preserve ordering across command kinds as well as within each kind.
        foreach (var tag in new[] { "command", "optionalcommand", "transformcommand" }) ranks.Add(tag, next);
        next++;
        Fields("tactics", "protoaction");
        return new System.Collections.ObjectModel.ReadOnlyDictionary<string, int>(ranks);
    }
}
