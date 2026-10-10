using System.Globalization;
using System.Xml.Linq;

namespace AoMDivineDataEditor.Classes;

public sealed record PowerSaveCatalogs(
    IReadOnlyCollection<string> KnownPowerTypes,
    IReadOnlyCollection<string>? ProtoUnitOrTypeNames = null,
    IReadOnlyCollection<string>? ProtoUnitNames = null,
    IReadOnlyCollection<string>? ProtoActionNames = null,
    IReadOnlyCollection<string>? TechnologyNames = null,
    IReadOnlyCollection<string>? AnimationNames = null,
    IReadOnlyCollection<string>? BoneNames = null,
    IReadOnlyCollection<string>? TacticNames = null,
    IReadOnlyCollection<string>? PowerNames = null,
    IReadOnlyCollection<XElement>? LegacyPowers = null,
    IReadOnlyCollection<string>? TerrainCreepNames = null);

public sealed record PowerSaveValidation(IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// Validates editor-owned Power XML immediately before persistence. Rules are path-aware so that
/// identically named fields in unrelated nested structures cannot validate each other. Unknown
/// Power types remain lossless raw XML and are deliberately not interpreted here.
/// </summary>
public static class PowerSaveValidator
{
    public static IReadOnlyList<string> KnownEffectTypes { get; } =
    [
        "WorkRate", "WorkRateSpecific", "CannotSnare", "BaseSpeedRunAnim",
        "EnemyAttachProtoUnit", "CantBeSlowed", "TacticEnable", "RateOfFire",
        "ActionEnable", "MaximumVelocity", "DamageBonus", "Invulnerable",
        "CanTargetButTakesNoDamage", "Damage", "HitPoints", "Market", "SharedLOS",
        "MinimumResourceTrickleRate", "MaximumResourceTrickleRate", "TrainPoints",
        "ResourceTrickleRate", "Cost", "OnHitEffectProbability", "GodPowerROFFactor",
        "WorkRateAll", "Resource", "ProtoActionAdd", "ShieldPoints", "OnHitEffectRate",
        "AssignOnHitEffect", "UpdateVisuals", "ArmorVulnerability", "RechargeTime",
        "TechActivate", "RevealEnemyLOS", "BuildPoints", "ModifyRate", "EqualizeResources",
        "BuildingWorkRate", "AutoGatherBonus"
    ];

    private enum NumberKind
    {
        Number,
        WholeNumber
    }

    private sealed record NumericRule(
        string Path,
        NumberKind Kind = NumberKind.Number,
        double? Minimum = null,
        double? Maximum = null);

    private static readonly NumericRule[] SharedNumericRules =
    [
        new("@orderhint"),
        new("activetime"),
        new("builduptime"),
        new("shutdowntime"),
        new("shutdownfadetime"),
        new("radius", Minimum: 0),
        new("minimapeventtime", Minimum: 0),
        new("limitglobal", NumberKind.WholeNumber, 0),
        new("limitplayer", NumberKind.WholeNumber, 0),
        new("rangeindicator/@range", Minimum: 0),
        new("rangeindicator/@width", Minimum: 0),
        new("rangeindicator/@speed", Minimum: 0),
        new("soundset/@delay", Minimum: 0),
        new("soundset/@fadein", Minimum: 0)
    ];

    private static readonly HashSet<string> NumericElementNames = Names("""
        accuracy activetime allydamageratio armoramount attackdelay attackinterval attackintervalseconds
        attacktreeaccuracy attractiondistancetocountagainstcap attractrate baseshearincrement baseshearmax
        blockerradius blowtime builduptime burnfadelength burnlength burntime camerashakedelay
        chompthrowheight chompthrowswallowtimer cloudbuildupendtime cloudbuildupstarttime
        cloudfadeoutendtime cloudfadeoutstarttime cost crackintervalmax crackintervalmin creepgenerators
        damagedepth damageinterval damageintervalseconds damagepercentofmaxhp damageradius damagesearchradius
        damagewidth damagecap damagetier debrisangularvelocity debrisxzvelocity debrisyvelocity deformdepth delay dragonattackinterval
        dragonspawndelayseconds dropintervalmax dropintervalmin dualpowermindistance eidolonmaxhpmultiplier
        eidolonmaxspeedmultiplier eidolonmin eidolonspawnduration eidolonspawnradius eidolonstep emberlifespan
        epicenterzonedamageratio epicenterzoneradius exploderadius explosioncamerashakeduration
        explosioncamerashakestrength explosiondelay explosionwinduplength fastestdrifttime fasteststriketime
        finalvelocity freeresearchrate freezeexpansiontime freezeholdtime freezetransitiontime funnelheight
        funnelradius generateinterval greaterdamageradius groupsearchradius growthradiusincreaserate
        growthradiusstart gustlength gustmaxspeedfactor gustminspeedfactor gustspeed gusttime hatchintervalmax
        hatchintervalmin hatchtime healspeed initialdelay initialrumblecamerashakeduration
        initialrumblecamerashakestrength initialvelocity innerorbitangularacceleration innerorbitradiussquared
        interval lightsetbuilduptime lightsetduration lightsetshutdowntime limitglobal limitplayer
        maxeidoloncount maxfreeresearches maxgrowspeed maximumaccumulatedpower maximumpullstrength maxnumspawns
        maxpacktargetspawnradius maxpackwanderdistance maxrange maxresources maxretryseconds maxrotatespeed
        maxscale maxthrowdistance maxthrowheight maxthrowvelocity maxunitheight middleorbitangularacceleration
        middleorbitradiussquared middlezonedamageratio middlezoneradius mingrowspeed minimumgroupsize
        minipacksize minpacktargetspawnradius minrange minrotatespeed minscale minthrowdistance minthrowheight
        minthrowvelocity mintimeinstageexploding mintimeinstagepullingunits mintimeinstagestarting
        numberofadditionalstrikes numberofprioritystrikes numberofstrikes numberofwaypoints numbertocreate
        numbertotransform numextramainpackspawns outerorbitangularacceleration outerorbitradiussquared
        outerzonedamageratio outerzoneradius outportalspawntime overallchance packattacksearchradius packscaninterval packsize
        packspawnradius packwanderdistance percentdamageonconvert placementradius poweraccumulationincrement
        powerblocker powerslot previousstrikeavoidanceradius pullradius pullstrengthadjustfactor pullstrengthincrement
        pullstrengthoverkillincrement radius radiusincreasestep rainbuildupendtime rainbuildupstarttime
        rainfadeoutendtime rainfadeoutstarttime ramptime regurgitateanimtime regurgitateheight regurgitatetime
        regurgitateunitstime repeatcost repeatdelay rescaninterval resourceconvertratio resourcevaluecap
        restrictplacementdistance rofpercentpenalty rootedtime rumbletime scaninterval secondaryaccuracy
        secondsbetweenstrikes separationdistance sharedeffecttimeseconds shearmultiplier shockwavevelocity
        shutdownfadetime shutdowntime sinkholeshakestrength sinkholetime slowestdrifttime sloweststriketime
        slowhealmultiplier spacingdistance spawnfoodratio spawnintervalmax spawnintervalmin spawntargetevalradius
        spawntimedelay speed sphereheightadjustfactor spheremovementdelay spherescalemultiplier
        spheretargetheight spiderspawnoffset spiralsize staytime strikeheight strikemaxoffset strikeminoffset
        strikeunitlimit stunduration stunlength suddendeathhpmultiplier swapdelay swarmcount swarmradius
        swarmwaypointoffset swarmwaypoints targetfloatheight throwoutwardsradius throwsizelimit
        throwupwardsradius timefactor totalnumberoftrees trainratepenalty treebendangleoffset treebendingduration
        treebendmaxamplitude treebendmaxperiod treebendmaxstrength treebendminamplitude treebendminperiod
        treebendminstrength treedestructionseconds treesearchradius treespawnintervalmax treespawnintervalmin
        typhoonattackdelayseconds typhoonattackinterval typhoonradiusend typhoonradiusexpansionseconds
        typhoonradiusstart typhoonspawndelayseconds unitacceleration unitdeathcamerashakeduration
        unitdeathcamerashakestrength uniteffecttime uniteffecttimeseconds unitescaperadius unitfadetime
        unitfloatadjustfactor unitimplodeendtimedelaymax unitimplodeendtimedelaymin unitimplodetimedelaymax
        unitimplodetimedelaymin variance vfxinterval wallsegmentcount warntime wavecount waveduration waveradius
        wolfcounthardcap wolfcountsoftcap
        """);

    private static readonly HashSet<string> WholeNumberElementNames = Names("""
        creepgenerators eidolonmin eidolonstep limitglobal limitplayer maxeidoloncount maxfreeresearches
        maxnumspawns minimumgroupsize minipacksize numberofadditionalstrikes numberofprioritystrikes
        numberofstrikes numberofwaypoints numbertocreate numbertotransform numextramainpackspawns packsize powerslot
        strikeunitlimit swarmcount swarmwaypoints totalnumberoftrees wallsegmentcount wavecount
        wolfcounthardcap wolfcountsoftcap
        """);

    private static readonly HashSet<string> NonNegativeElementNames = Names("""
        accuracy attackdelay attackinterval attackintervalseconds blockerradius cost
        damageinterval damageintervalseconds delay dragonattackinterval dragonspawndelayseconds dropintervalmax
        dropintervalmin dualpowermindistance eidolonmin eidolonspawnduration eidolonspawnradius eidolonstep
        generateinterval interval limitglobal limitplayer maxeidoloncount maxfreeresearches maxnumspawns
        maxpacktargetspawnradius maxpackwanderdistance maxrange maxresources maxretryseconds maxunitheight
        minimumgroupsize minipacksize minpacktargetspawnradius minrange numberofadditionalstrikes
        numberofprioritystrikes numberofstrikes numberofwaypoints numbertocreate numextramainpackspawns
        packattacksearchradius packscaninterval packsize packspawnradius packwanderdistance placementradius
        powerblocker previousstrikeavoidanceradius pullradius radius repeatcost repeatdelay rescaninterval
        restrictplacementdistance secondsbetweenstrikes separationdistance spawnintervalmax
        spawnintervalmin spawntargetevalradius spawntimedelay spiderattackrange staytime strikeunitlimit
        swarmcount swarmradius swarmwaypoints throwsizelimit timefactor totalnumberoftrees wallsegmentcount
        wavecount waveradius wolfcounthardcap wolfcountsoftcap
        """);

    private static readonly HashSet<string> NumericAttributeNames = Names("""
        affectednumber amount altamount animrate attackinterval cap chance count damage decayrate delay duration
        factor fadein fadetime final fraction freezeduration gatherratemultiplier globalprob goldamount growthrate
        init lifespan maxfood maxradius maxswapamount maxupdateinterval minradius minswapamount minupdateinterval
        mindestroyed numspawns prob quantity quantityaddon r radius rate resourcecap resourcevaluemultiplier
        scale select speed stacklimit strength theta timeout value x y z
        """);

    private static readonly HashSet<string> NumericAttributeOwnerNames = Names("""
        ageadjustment ageinfo camerashake convertusingminmax createagespecificunit createunit damage damagetier
        effect initialdamage modify modifyramp onhiteffect placementrestriction rangeindicator
        resourcevaluemultiplier rooteddamage soundset stampede terraincreep vfxgrass
        """);

    private static readonly HashSet<string> WholeNumberAttributeNames = Names("""
        affectednumber count maxfood maxswapamount mindestroyed minswapamount numspawns quantity quantityaddon resourcecap select stacklimit
        """);

    private static readonly HashSet<string> NonNegativeAttributeNames = Names("""
        affectednumber cap chance count delay duration fadein fadetime freezeduration maxfood maxradius
        maxswapamount maxupdateinterval minradius minswapamount minupdateinterval numspawns prob quantity
        quantityaddon radius rate resourcecap select speed stacklimit timeout
        """);

    private static readonly HashSet<string> ProtoUnitElementNames = Names("""
        areavfx attackproto birthvfx burstvfx chargingvfx cloudvfxproto damagereceiverproto damagingproto
        decalproto dragonprotounit dustlargevfxproto dustmediumvfxproto dustsmallvfxproto earthmonsterprotounit
        effectproto embersproto farmproto gatebirthproto gateflameproto groundimpactvfxproto groundsparksvfxproto
        groundvfxproto gustproto healvfx icesheetvfxproto inportal inportalprotounit landimpactprotounit
        landprotounit largesfxprotoid largestrikeproto largevfxproto lureproto meteorprotounit modelattachment
        outportal outportalprotounit placementvfx pseedsfarmtargetvfx pseedsfarmvfx rainprotounit rainvfxproto
        reappearunitvfx revealerproto scorchvfxproto sfx shockwaveproto smallrainspotproto smallsfx
        smallsfxprotoid smallvfxproto snowdriftprotounit sparksvfxproto spawnproto spawnvfx sphereproto
        spiderdoorproto spidereggproto spiderproto spiderwarnproto spyprotounit startingvfx stormprotounit
        strikeproto swarmprotounit targetunitvfx tartariangateproto tornadoprotounit treeareavfx treevfx
        typhoonprotounit unitvfx vanishunitvfx vanishvfx vfxadditionalimpact vfxarea vfxcurseproto
        vfxeidolonspawnproto vfxforunitswithoutbirthanims vfxmaintarget vfxmaintargetimpact vfxmarkanim
        vfxproto vfxpulleffectproto vortexfinishlinkedproto vortexlandingproto vortexstartlinkedproto
        vortexstartproto watersplashprotounit waveprotounit wallproto wolfvfx
        """);

    private static readonly HashSet<string> NoAmountEffectTypes = Names("""
        techactivate updatevisuals assignonhiteffect tacticenable actionenable enemyattachprotounit
        protoactionadd cannotsnare cantbeslowed invulnerable cantargetbuttakesnodamage equalizeresources
        revealenemylos sharedlos
        """);

    private static readonly HashSet<string> PlayerTargetEffectTypes = Names("""
        market minimumresourcetricklerate maximumresourcetricklerate resourcetricklerate
        godpowerroffactor resource autogatherbonus updatevisuals equalizeresources revealenemylos
        sharedlos enemyattachprotounit
        """);

    public static PowerSaveValidation Validate(XDocument document, PowerSaveCatalogs catalogs)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(catalogs);

        var errors = new List<string>();
        var structural = ModXmlDocumentSafety.Validate(document, ModXmlDocumentKind.Powers);
        errors.AddRange(structural.Errors.Select(issue => $"{issue.ObjectName}: {issue.Reason}"));
        if (!structural.IsSafeToEdit)
            return Result(errors);

        var knownPowerTypes = catalogs.KnownPowerTypes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var power in document.Root!.Elements().Where(IsNamed("power")))
        {
            if (!PowerCatalog.IsModCatalogPower(power))
                continue;

            var powerName = PowerCatalog.GetName(power);
            var owner = $"Power '{(powerName.Length == 0 ? "(unnamed)" : powerName)}'";
            var powerType = AttributeValue(power, "type");
            if (powerType.Length == 0)
            {
                errors.Add($"{owner} /power/@type is required.");
                continue;
            }

            // A future or mod-defined type is preserved without imposing assumptions about its schema.
            if (!knownPowerTypes.Contains(powerType))
                continue;

            ValidateSharedNumbers(power, powerType, owner, errors);
            ValidateNumericFields(power, powerType, owner, errors);
            ValidateCatalogFields(power, owner, catalogs, errors);
            ValidateRepeatedStructures(power, powerType, owner, catalogs, errors);
            ValidateUnitModify(power, powerType, owner, catalogs, errors);
            ValidateStructuredEffects(power, powerType, owner, catalogs, errors);
            ValidateEffectInArea(power, powerType, owner, catalogs, errors);
            ValidateSwitchingEffects(power, powerType, owner, catalogs, errors);
        }

        return Result(errors);
    }

    private static void ValidateSharedNumbers(
        XElement power,
        string powerType,
        string owner,
        List<string> errors)
    {
        foreach (var rule in SharedNumericRules)
        {
            foreach (var value in ValuesAtPath(power, rule.Path))
                ValidateNumber(value, $"{owner} /power/{rule.Path}", rule, errors);
        }

        var costKind = powerType.Equals("PowerClone", StringComparison.OrdinalIgnoreCase)
            ? NumberKind.Number
            : NumberKind.WholeNumber;
        foreach (var tag in new[] { "cost", "repeatcost" })
        foreach (var value in ValuesAtPath(power, tag))
            ValidateNumber(value, $"{owner} /power/{tag}", new NumericRule(tag, costKind, 0), errors);
    }

    private static void ValidateStructuredEffects(
        XElement power,
        string powerType,
        string owner,
        PowerSaveCatalogs catalogs,
        List<string> errors)
    {
        foreach (var effect in power.Elements().Where(IsNamed("effect")))
            ValidateEffect(power, effect, $"{owner} /power/effect", catalogs, errors);
    }

    private static void ValidateSwitchingEffects(
        XElement power,
        string powerType,
        string owner,
        PowerSaveCatalogs catalogs,
        List<string> errors)
    {
        if (!powerType.Equals("SwitchingEffects", StringComparison.OrdinalIgnoreCase))
            return;

        var tierType = power.Elements().FirstOrDefault(IsNamed("tiertype"))?.Value.Trim() ?? "Time";
        if (!Names("Time TotalResources CombatXP").Contains(tierType) &&
            !IsLegacyElementValue(power.Elements().FirstOrDefault(IsNamed("tiertype")) ?? power,
                tierType, catalogs))
            errors.Add($"{owner} /power/tiertype has unsupported value '{tierType}'.");
        if (tierType.Equals("Time", StringComparison.OrdinalIgnoreCase))
        {
            var intervals = ValuesAtPath(power, "interval").ToList();
            if (intervals.Count == 0)
                errors.Add($"{owner} /power/interval is required when Tier Type is Time.");
            foreach (var value in intervals)
                ValidateNumber(value, $"{owner} /power/interval", new NumericRule("interval"), errors);
        }

        var progress = power.Elements().FirstOrDefault(IsNamed("progresstype"));
        var hidden = power.Elements().Any(IsNamed("hidewheelview"));
        if (hidden && progress != null &&
            !LegacyPowerHasElements(power, catalogs, "hidewheelview", "progresstype"))
            errors.Add($"{owner} cannot contain both /power/hidewheelview and /power/progresstype.");
        if (hidden && power.Elements().FirstOrDefault(IsNamed("timerrolloverid")) is { } timerRollover &&
            !IsLegacyElementValue(timerRollover, timerRollover.Value, catalogs))
            errors.Add($"{owner} /power/timerrolloverid must be omitted when Progress is Hide.");
        if (progress != null && !Names("Default ShieldBlessing SpawnReward").Contains(progress.Value.Trim()) &&
            !IsLegacyElementValue(progress, progress.Value, catalogs))
            errors.Add($"{owner} /power/progresstype has unsupported value '{progress.Value.Trim()}'.");

        var requirementTag = tierType.Equals("TotalResources", StringComparison.OrdinalIgnoreCase)
            ? "resourcetierreq"
            : tierType.Equals("CombatXP", StringComparison.OrdinalIgnoreCase)
                ? "combatxptierreq"
                : null;
        foreach (var (container, index) in power.Elements().Where(IsNamed("effects"))
                     .Select((element, index) => (element, index + 1)))
        {
            var containerPath = $"{owner} /power/effects[{index}]";
            if (requirementTag != null)
            {
                foreach (var value in ValuesAtPath(container, requirementTag))
                    ValidateNumber(value,
                        $"{owner} /power/effects[{index}]/{requirementTag}",
                        new NumericRule(requirementTag, NumberKind.WholeNumber, 0), errors);
                if (requirementTag == "resourcetierreq" &&
                    container.Elements().FirstOrDefault(IsNamed(requirementTag)) is { } requirement)
                    ValidateCatalogValue(power, requirement, "type", AttributeValue(requirement, "type"),
                        ProtoConstants.KnownResourceTypes, containerPath + "/resourcetierreq/@type",
                        catalogs, required: true, errors);
            }

            foreach (var effect in container.Elements().Where(IsNamed("effect")))
                ValidateEffect(power, effect, $"{owner} /power/effects[{index}]/effect", catalogs, errors);
        }
    }

    private static void ValidateEffect(
        XElement power,
        XElement effect,
        string path,
        PowerSaveCatalogs catalogs,
        List<string> errors)
    {
        var type = AttributeValue(effect, "type");
        if (type.Length == 0)
        {
            errors.Add($"{path}/@type is required.");
            return;
        }

        if (!KnownEffectTypes.Contains(type, StringComparer.OrdinalIgnoreCase))
            return;

        var targets = effect.Elements().Where(IsNamed("target")).ToList();
        if (targets.Count != 1)
            errors.Add($"{path} must contain exactly one <target> element.");
        else
            ValidateEffectTarget(power, effect, targets[0], type, path, catalogs, errors);

        var normalized = type.ToLowerInvariant();
        if (NoAmountEffectTypes.Contains(normalized))
        {
            ValidateForbiddenNewAttribute(power, effect, "relativity", path, catalogs, errors);
            ValidateForbiddenNewAttribute(power, effect, "amount", path, catalogs, errors);
            ValidateForbiddenNewAttribute(power, effect, "altamount", path, catalogs, errors);
        }
        else
        {
            ValidateRequiredAttribute(effect, "relativity", path, errors);
            ValidateRequiredNumberAttribute(effect, "amount", path,
                normalized is "minimumresourcetricklerate" or "maximumresourcetricklerate"
                    ? NumberKind.WholeNumber
                    : NumberKind.Number, errors);
            var relativity = AttributeValue(effect, "relativity");
            if (relativity.Length > 0 && !Names("Absolute Percent BasePercent Assign Override").Contains(relativity) &&
                !IsLegacyAttributeValue(effect, "relativity", relativity, catalogs))
                errors.Add($"{path}/@relativity has unsupported value '{relativity}'.");
        }

        foreach (var attributeName in new[] { "amount", "altamount" })
        {
            var attribute = effect.Attributes().FirstOrDefault(attribute =>
                attribute.Name.LocalName.Equals(attributeName, StringComparison.OrdinalIgnoreCase));
            if (attribute != null)
                ValidateNumber(attribute.Value, $"{path}/@{attributeName}",
                    new NumericRule(attributeName), errors);
        }

        ValidateEffectSpecificFields(power, effect, type, path, catalogs, errors);
    }

    private static void ValidateNumericFields(
        XElement power,
        string powerType,
        string owner,
        List<string> errors)
    {
        foreach (var element in power.Descendants().Where(element => !element.HasElements &&
                     NumericElementNames.Contains(element.Name.LocalName) &&
                     (ReferenceEquals(element.Parent, power) ||
                      IsAny(element.Parent?.Name.LocalName ?? "", "damagetiers", "effects", "onhiteffect")) &&
                     element.Name.LocalName is not ("cost" or "repeatcost")))
        {
            var name = element.Name.LocalName;
            double? maximum = name.Equals("accuracy", StringComparison.OrdinalIgnoreCase) ||
                          name.Equals("secondaryaccuracy", StringComparison.OrdinalIgnoreCase) ||
                          name.Equals("overallchance", StringComparison.OrdinalIgnoreCase) ||
                          name.Equals("damagepercentofmaxhp", StringComparison.OrdinalIgnoreCase)
                ? 1d
                : name.Equals("powerslot", StringComparison.OrdinalIgnoreCase) ? 5d : null;
            ValidateNumber(element.Value, $"{owner} /power/{RelativePath(power, element)}",
                new NumericRule(name,
                    WholeNumberElementNames.Contains(name) ? NumberKind.WholeNumber : NumberKind.Number,
                    name.Equals("powerslot", StringComparison.OrdinalIgnoreCase)
                        ? 1d
                        : NonNegativeElementNames.Contains(name) ? 0d : null,
                    maximum), errors);
        }

        foreach (var element in power.DescendantsAndSelf())
        foreach (var attribute in element.Attributes().Where(attribute =>
                     NumericAttributeOwnerNames.Contains(element.Name.LocalName) &&
                     NumericAttributeNames.Contains(attribute.Name.LocalName)))
        {
            var name = attribute.Name.LocalName;
            var maximum = name.Equals("chance", StringComparison.OrdinalIgnoreCase) ||
                          name.Equals("prob", StringComparison.OrdinalIgnoreCase) ||
                          name.Equals("globalprob", StringComparison.OrdinalIgnoreCase)
                ? 1d
                : (double?)null;
            ValidateNumber(attribute.Value,
                $"{owner} /power/{RelativePath(power, element)}/@{name}",
                new NumericRule(name,
                    WholeNumberAttributeNames.Contains(name) ? NumberKind.WholeNumber : NumberKind.Number,
                    NonNegativeAttributeNames.Contains(name) ? 0 : null,
                    maximum), errors);
        }
    }

    private static void ValidateCatalogFields(
        XElement power,
        string owner,
        PowerSaveCatalogs catalogs,
        List<string> errors)
    {
        foreach (var element in power.Elements().Where(element => !element.HasElements &&
                     ProtoUnitElementNames.Contains(element.Name.LocalName)))
        {
            ValidateCatalogValue(power, element, null, element.Value, catalogs.ProtoUnitNames,
                $"{owner} /power/{RelativePath(power, element)}", catalogs, required: false, errors);
        }

        foreach (var tag in new[] { "strikeaction", "crushaction", "attackaction", "protoaction" })
        foreach (var element in power.Elements().Where(IsNamed(tag)))
            ValidateCatalogValue(power, element, null, element.Value, catalogs.ProtoActionNames,
                $"{owner} /power/{RelativePath(power, element)}", catalogs, required: false, errors);

        foreach (var element in power.Elements().Where(element =>
                     element.Name.LocalName.Equals("regurgitateanim", StringComparison.OrdinalIgnoreCase)))
            ValidateCatalogValue(power, element, null, element.Value, catalogs.AnimationNames,
                $"{owner} /power/{RelativePath(power, element)}", catalogs, required: false, errors);

        foreach (var tag in new[] { "vfxattachbone", "modelattachmentbone", "vfxmaintargetbone", "vfxmarkanimbone" })
        foreach (var element in power.Elements().Where(IsNamed(tag)))
            ValidateCatalogValue(power, element, null, element.Value, catalogs.BoneNames,
                $"{owner} /power/{RelativePath(power, element)}", catalogs, required: false, errors);

        foreach (var tag in new[] { "durationtecheffect", "onlyapplydurationtecheffectiftechactive" })
        foreach (var element in power.Elements().Where(IsNamed(tag)))
            ValidateCatalogValue(power, element, null, element.Value, catalogs.TechnologyNames,
                $"{owner} /power/{RelativePath(power, element)}", catalogs, required: false, errors);

        foreach (var terrain in power.Elements().Where(IsNamed("terraincreep")))
            ValidateCatalogValue(power, terrain, "creep", AttributeValue(terrain, "creep"),
                catalogs.TerrainCreepNames, $"{owner} /power/terraincreep/@creep", catalogs,
                required: true, errors);

        var powerType = AttributeValue(power, "type");
        if (powerType.Equals("SwapUnit", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var swap in power.Elements().Where(IsNamed("abstracttype")))
            {
                ValidateCatalogValue(power, swap, null, swap.Value, catalogs.ProtoUnitOrTypeNames,
                    $"{owner} /power/abstracttype", catalogs, required: true, errors);
                ValidateCatalogValue(power, swap, "swapto", AttributeValue(swap, "swapto"),
                    catalogs.ProtoUnitNames, $"{owner} /power/abstracttype/@swapto", catalogs,
                    required: true, errors);
            }
        }
        if (powerType.Equals("Undermine", StringComparison.OrdinalIgnoreCase))
            foreach (var target in power.Elements().Where(IsNamed("attacktarget")))
                ValidateCatalogValue(power, target, null, target.Value, catalogs.ProtoUnitOrTypeNames,
                    $"{owner} /power/attacktarget", catalogs, required: true, errors);
        foreach (var target in power.Elements().Where(IsNamed("killunittype")))
            ValidateCatalogValue(power, target, null, target.Value, catalogs.ProtoUnitOrTypeNames,
                $"{owner} /power/killunittype", catalogs, required: true, errors);

        if (powerType.Equals("Fimbulwinter", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var wolf in power.Elements().Where(IsNamed("wolfproto")))
                ValidateCatalogValue(power, wolf, null, wolf.Value, catalogs.ProtoUnitNames,
                    $"{owner} /power/wolfproto", catalogs, required: true, errors);
            foreach (var drift in power.Elements().Where(IsNamed("snowdrift")))
            {
                ValidateCatalogValue(power, drift, null, drift.Value, catalogs.ProtoUnitNames,
                    $"{owner} /power/snowdrift", catalogs, required: true, errors);
                ValidateCatalogValue(power, drift, "target", AttributeValue(drift, "target"),
                    catalogs.ProtoUnitOrTypeNames, $"{owner} /power/snowdrift/@target", catalogs,
                    required: true, errors);
            }
        }

        if (powerType.Equals("PowerClone", StringComparison.OrdinalIgnoreCase) &&
            power.Elements().FirstOrDefault(IsNamed("duplicatetargetreplacement")) is { } replacement)
            foreach (var target in replacement.Elements().Where(IsNamed("target")))
                ValidateCatalogValue(power, target, null, target.Value, catalogs.PowerNames,
                    $"{owner} /power/duplicatetargetreplacement/target", catalogs, required: true, errors);

        if (IsAny(powerType, "TempUnit", "SacredGate", "DwarvenMine"))
            foreach (var create in power.Elements().Where(IsNamed("createunit")))
            foreach (var unit in create.Value.Split('|', StringSplitOptions.RemoveEmptyEntries |
                                                        StringSplitOptions.TrimEntries))
                ValidateCatalogValue(power, create, null, unit, catalogs.ProtoUnitNames,
                    $"{owner} /power/createunit", catalogs, required: true, errors);
    }

    private static void ValidateRepeatedStructures(
        XElement power,
        string powerType,
        string owner,
        PowerSaveCatalogs catalogs,
        List<string> errors)
    {
        if (powerType.Equals("FreyrsGift", StringComparison.OrdinalIgnoreCase))
        {
            var creates = power.Elements().Where(IsNamed("createagespecificunit")).ToList();
            ValidateMaximumCount(creates, 5, owner, "createagespecificunit", errors);
            ValidateUniqueAttribute(creates, "age", owner, "createagespecificunit", errors);
            foreach (var (create, index) in creates.Select((value, index) => (value, index + 1)))
            {
                var path = $"{owner} /power/createagespecificunit[{index}]";
                ValidateCatalogValue(power, create, null, create.Value, catalogs.ProtoUnitNames,
                    path, catalogs, required: true, errors);
                ValidateAgeAttribute(create, "age", path, catalogs, errors);
                ValidateRequiredNumberAttribute(create, "quantity", path, NumberKind.WholeNumber, errors, 0);
            }
        }

        if (powerType.Equals("FoxCharm", StringComparison.OrdinalIgnoreCase))
        {
            var adjustments = power.Elements().Where(IsNamed("ageadjustment")).ToList();
            ValidateMaximumCount(adjustments, 5, owner, "ageadjustment", errors);
            ValidateUniqueText(adjustments, owner, "ageadjustment Age", errors);
            foreach (var (adjustment, index) in adjustments.Select((value, index) => (value, index + 1)))
            {
                var path = $"{owner} /power/ageadjustment[{index}]";
                ValidateAgeValue(adjustment.Value, path, catalogs, adjustment, errors);
                ValidateRequiredNumberAttribute(adjustment, "affectedNumber", path,
                    NumberKind.WholeNumber, errors);
            }
        }

        if (powerType.Equals("Lure", StringComparison.OrdinalIgnoreCase))
        {
            var ageInfo = power.Elements().Where(IsNamed("ageinfo")).ToList();
            ValidateMaximumCount(ageInfo, 5, owner, "ageinfo", errors);
            ValidateUniqueText(ageInfo, owner, "ageinfo Age", errors);
            foreach (var (age, index) in ageInfo.Select((value, index) => (value, index + 1)))
            {
                var path = $"{owner} /power/ageinfo[{index}]";
                if (!int.TryParse(age.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture,
                        out var ageNumber) || ageNumber is < 1 or > 5)
                    errors.Add($"{path} must identify an Age from 1 through 5.");
                ValidateRequiredNumberAttribute(age, "maxfood", path, NumberKind.WholeNumber, errors, 0);
                ValidateRequiredNumberAttribute(age, "numspawns", path, NumberKind.WholeNumber, errors, 0);
            }
        }

        if (powerType.Equals("CageUnit", StringComparison.OrdinalIgnoreCase))
        {
            var multipliers = power.Elements().Where(IsNamed("resourcevaluemultiplier")).ToList();
            ValidateMaximumCount(multipliers, ProtoConstants.KnownResourceTypes.Length, owner,
                "resourcevaluemultiplier", errors);
            ValidateUniqueAttribute(multipliers, "resourcetype", owner, "resourcevaluemultiplier", errors);
            foreach (var (multiplier, index) in multipliers.Select((value, index) => (value, index + 1)))
            {
                var path = $"{owner} /power/resourcevaluemultiplier[{index}]";
                ValidateCatalogValue(power, multiplier, "resourcetype", AttributeValue(multiplier, "resourcetype"),
                    ProtoConstants.KnownResourceTypes, path + "/@resourcetype", catalogs, required: true, errors);
                if (string.IsNullOrWhiteSpace(multiplier.Value))
                    errors.Add($"{path} requires a numeric multiplier.");
                else
                    ValidateNumber(multiplier.Value, path, new NumericRule("resourcevaluemultiplier"), errors);
            }

            foreach (var unitType in power.Elements().Where(IsNamed("similarunittypes"))
                         .SelectMany(container => container.Elements().Where(IsNamed("unittype"))))
                ValidateCatalogValue(power, unitType, null, unitType.Value, catalogs.ProtoUnitOrTypeNames,
                    $"{owner} /power/{RelativePath(power, unitType)}", catalogs, required: true, errors);
        }

        if (powerType.Equals("Bronze", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var (armor, index) in power.Elements().Where(IsNamed("armor"))
                         .Select((value, index) => (value, index + 1)))
            {
                var armorTypes = armor.Elements().Where(IsNamed("armortype")).ToList();
                ValidateUniqueText(armorTypes, owner, $"armor[{index}] Armor Type", errors);
                foreach (var armorType in armorTypes)
                    ValidateCatalogValue(power, armorType, null, armorType.Value,
                        ProtoConstants.KnownArmorTypes,
                        $"{owner} /power/{RelativePath(power, armorType)}", catalogs, required: true, errors);
            }
        }

        foreach (var initialDamage in power.Elements().Where(IsNamed("initialdamage")))
        {
            ValidateCatalogValue(power, initialDamage, null, initialDamage.Value,
                catalogs.ProtoUnitOrTypeNames, $"{owner} /power/initialdamage", catalogs,
                required: true, errors);
            ValidateRequiredNumberAttribute(initialDamage, "fraction", $"{owner} /power/initialdamage",
                NumberKind.Number, errors);
        }
        foreach (var damageCap in power.Elements().Where(IsNamed("damagecap")))
        {
            ValidateCatalogValue(power, damageCap, "unittype", AttributeValue(damageCap, "unittype"),
                catalogs.ProtoUnitOrTypeNames, $"{owner} /power/damagecap/@unittype", catalogs,
                required: true, errors);
            if (string.IsNullOrWhiteSpace(damageCap.Value))
                errors.Add($"{owner} /power/damagecap requires a numeric value.");
            else
                ValidateNumber(damageCap.Value, $"{owner} /power/damagecap",
                    new NumericRule("damagecap"), errors);
        }
        foreach (var rootDamage in power.Elements().Where(IsNamed("rooteddamage")))
        {
            ValidateCatalogValue(power, rootDamage, null, rootDamage.Value,
                catalogs.ProtoUnitOrTypeNames, $"{owner} /power/rooteddamage", catalogs,
                required: true, errors);
            ValidateRequiredNumberAttribute(rootDamage, "damage", $"{owner} /power/rooteddamage",
                NumberKind.Number, errors, 0);
        }
        foreach (var tier in power.Elements().Where(IsNamed("damagetiers"))
                     .SelectMany(container => container.Elements().Where(IsNamed("damagetier"))))
        {
            var path = $"{owner} /power/{RelativePath(power, tier)}";
            ValidateRequiredNumberAttribute(tier, "mindestroyed", path,
                NumberKind.WholeNumber, errors);
            if (string.IsNullOrWhiteSpace(tier.Value))
                errors.Add($"{path} requires an HP Loss Multiplier.");
            else
                ValidateNumber(tier.Value, path, new NumericRule("damagetier"), errors);
        }

        if (IsAny(powerType, "TempUnit", "SacredGate", "DwarvenMine"))
        {
            foreach (var (create, index) in power.Elements().Where(IsNamed("createunit"))
                         .Select((value, index) => (value, index + 1)))
            {
                var path = $"{owner} /power/createunit[{index}]";
                if (string.IsNullOrWhiteSpace(create.Value))
                    errors.Add($"{path} requires at least one Unit.");
                ValidateRequiredNumberAttribute(create, "quantity", path,
                    NumberKind.WholeNumber, errors, 0);
            }
        }
    }

    private static void ValidateUnitModify(
        XElement power,
        string powerType,
        string owner,
        PowerSaveCatalogs catalogs,
        List<string> errors)
    {
        if (!IsAny(powerType, "UnitModify", "SingleModify", "NewMoon"))
            return;

        foreach (var (modify, index) in power.Elements().Where(IsNamed("unitmodify"))
                     .Select((value, index) => (value, index + 1)))
        {
            var path = $"{owner} /power/unitmodify[{index}]";
            var modifyType = modify.Value.Trim();
            if (modifyType.Length == 0)
                errors.Add($"{path} requires a Modify Type.");
            else if (!ProtoConstants.KnownModifyTypes.Contains(modifyType, StringComparer.OrdinalIgnoreCase) &&
                     !IsLegacyElementValue(modify, modifyType, catalogs))
                errors.Add($"{path} has unknown Modify Type '{modifyType}'.");

            var apply = AttributeValue(modify, "apply");
            if (!Names("Add Multiply Set").Contains(apply) && !IsLegacyAttributeValue(modify, "apply", apply, catalogs))
                errors.Add($"{path}/@apply must be Add, Multiply, or Set.");
            ValidateRequiredNumberAttribute(modify, "amount", path, NumberKind.Number, errors);
            if (AttributeValue(modify, "age") is { Length: > 0 } age)
                ValidateAgeValue(age, path + "/@age", catalogs, modify, errors);

            var damageType = AttributeValue(modify, "damagetype");
            if (modifyType.Equals("DamageSpecific", StringComparison.OrdinalIgnoreCase))
                ValidateCatalogValue(power, modify, "damagetype", damageType, ProtoConstants.KnownDamageTypes,
                    path + "/@damagetype", catalogs, required: true, errors);
            else if (modifyType.Equals("ArmorSpecific", StringComparison.OrdinalIgnoreCase))
                ValidateCatalogValue(power, modify, "damagetype", damageType, ProtoConstants.KnownArmorTypes,
                    path + "/@damagetype", catalogs, required: true, errors);

            var excludes = AttributeValue(modify, "excludetypes");
            foreach (var exclude in excludes.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                ValidateCatalogValue(power, modify, "excludetypes", exclude, catalogs.ProtoUnitOrTypeNames,
                    path + "/@excludetypes", catalogs, required: true, errors);
        }
    }

    private static void ValidateEffectInArea(
        XElement power,
        string powerType,
        string owner,
        PowerSaveCatalogs catalogs,
        List<string> errors)
    {
        if (!powerType.Equals("EffectInArea", StringComparison.OrdinalIgnoreCase))
            return;

        foreach (var (effect, index) in power.Elements().Where(IsNamed("onhiteffect"))
                     .Select((value, index) => (value, index + 1)))
        {
            var path = $"{owner} /power/onhiteffect[{index}]";
            var type = AttributeValue(effect, "type");
            if (type.Length == 0)
            {
                errors.Add($"{path}/@type is required.");
                continue;
            }
            if (!ProtoConstants.KnownOnHitEffectTypes.Contains(type, StringComparer.OrdinalIgnoreCase) &&
                !IsLegacyAttributeValue(effect, "type", type, catalogs))
                continue; // Unknown modded on-hit schemas remain preserve-only.

            ValidateOptionalCatalogAttribute(power, effect, "proto", catalogs.ProtoUnitNames, path, catalogs, errors);
            ValidateOptionalCatalogAttribute(power, effect, "protoaction", catalogs.ProtoActionNames, path, catalogs, errors);
            ValidateOptionalCatalogAttribute(power, effect, "attachbone", catalogs.BoneNames, path, catalogs, errors);
            ValidateOptionalCatalogAttribute(power, effect, "anim", catalogs.AnimationNames, path, catalogs, errors);
            ValidateOptionalCatalogAttribute(power, effect, "rewardtype", ProtoConstants.KnownResourceTypes, path, catalogs, errors);
            foreach (var damage in effect.Elements().Where(IsNamed("damage")))
            {
                ValidateCatalogValue(power, damage, "type", AttributeValue(damage, "type"),
                    ProtoConstants.KnownDamageTypes, path + "/damage/@type", catalogs, required: true, errors);
                ValidateNumber(damage.Value, path + "/damage", new NumericRule("damage"), errors);
            }
            foreach (var modify in effect.Elements().Where(element =>
                         element.Name.LocalName.Equals("modify", StringComparison.OrdinalIgnoreCase) ||
                         element.Name.LocalName.Equals("modifyramp", StringComparison.OrdinalIgnoreCase)))
                ValidateOnHitModify(power, modify, path + "/" + modify.Name.LocalName, catalogs, errors);
            foreach (var target in effect.Elements().Where(IsNamed("target")))
            foreach (var attribute in target.Attributes().Where(attribute =>
                         IsAny(attribute.Name.LocalName, "attacktype", "ignoretype")))
                ValidateCatalogValue(power, target, attribute.Name.LocalName, attribute.Value,
                    catalogs.ProtoUnitOrTypeNames, path + "/target/@" + attribute.Name.LocalName,
                    catalogs, required: true, errors);
        }
    }

    private static void ValidateEffectTarget(
        XElement power,
        XElement effect,
        XElement target,
        string effectType,
        string path,
        PowerSaveCatalogs catalogs,
        List<string> errors)
    {
        var targetType = AttributeValue(target, "type");
        var normalized = effectType.ToLowerInvariant();
        if (PlayerTargetEffectTypes.Contains(normalized))
        {
            if (!targetType.Equals("Player", StringComparison.OrdinalIgnoreCase))
                errors.Add($"{path}/target/@type must be Player for {effectType}.");
            return;
        }

        if (normalized == "techactivate")
        {
            if (!targetType.Equals("Tech", StringComparison.OrdinalIgnoreCase))
                errors.Add($"{path}/target/@type must be Tech for TechActivate.");
            ValidateCatalogValue(power, target, null, target.Value, catalogs.TechnologyNames,
                path + "/target", catalogs, required: true, errors);
            return;
        }

        if (normalized == "cost")
        {
            if (targetType.Equals("TechAll", StringComparison.OrdinalIgnoreCase))
                return;
            if (targetType.Equals("Tech", StringComparison.OrdinalIgnoreCase))
                ValidateCatalogValue(power, target, null, target.Value, catalogs.TechnologyNames,
                    path + "/target", catalogs, required: true, errors);
            else if (targetType.Equals("ProtoUnit", StringComparison.OrdinalIgnoreCase))
                ValidateCatalogValue(power, target, null, target.Value, catalogs.ProtoUnitOrTypeNames,
                    path + "/target", catalogs, required: true, errors);
            else
                errors.Add($"{path}/target/@type must be ProtoUnit, Tech, or TechAll for Cost.");
            return;
        }

        if (!targetType.Equals("ProtoUnit", StringComparison.OrdinalIgnoreCase))
            errors.Add($"{path}/target/@type must be ProtoUnit for {effectType}.");
        ValidateCatalogValue(power, target, null, target.Value, catalogs.ProtoUnitOrTypeNames,
            path + "/target", catalogs, required: true, errors);
    }

    private static void ValidateEffectSpecificFields(
        XElement power,
        XElement effect,
        string type,
        string path,
        PowerSaveCatalogs catalogs,
        List<string> errors)
    {
        var normalized = type.ToLowerInvariant();
        if (normalized == "actionenable")
            ValidateRequiredCatalogAttribute(power, effect, "action", catalogs.ProtoActionNames, path, catalogs, errors);
        else if (normalized == "tacticenable")
            ValidateRequiredCatalogAttribute(power, effect, "tactic", catalogs.TacticNames, path, catalogs, errors);
        else if (normalized == "enemyattachprotounit")
        {
            ValidateRequiredCatalogAttribute(power, effect, "attachProtoUnit", catalogs.ProtoUnitNames, path, catalogs, errors);
            ValidateRequiredCatalogAttribute(power, effect, "unittype", catalogs.ProtoUnitOrTypeNames, path, catalogs, errors);
        }
        else if (normalized == "updatevisuals")
            ValidateRequiredCatalogAttribute(power, effect, "unittype", catalogs.ProtoUnitOrTypeNames, path, catalogs, errors);
        else if (normalized == "assignonhiteffect")
        {
            if (AttributeValue(effect, "action").Length == 0 && AttributeValue(effect, "allactions") != "1")
                errors.Add($"{path} requires an Action or allactions=\"1\".");
            ValidateOptionalCatalogAttribute(power, effect, "action", catalogs.ProtoActionNames, path, catalogs, errors);
            ValidateRequiredCatalogAttribute(power, effect, "srcproto", catalogs.ProtoUnitNames, path, catalogs, errors);
            ValidateRequiredCatalogAttribute(power, effect, "srcaction", catalogs.ProtoActionNames, path, catalogs, errors);
            ValidateRequiredCatalogAttribute(power, effect, "effecttype", ProtoConstants.KnownOnHitEffectTypes, path, catalogs, errors);
        }
        else if (normalized is "onhiteffectprobability" or "onhiteffectrate")
        {
            ValidateRequiredCatalogAttribute(power, effect, "effecttype", ProtoConstants.KnownOnHitEffectTypes, path, catalogs, errors);
            ValidateOptionalCatalogAttribute(power, effect, "targettype", catalogs.ProtoUnitOrTypeNames, path, catalogs, errors);
            ValidateOptionalCatalogAttribute(power, effect, "action", catalogs.ProtoActionNames, path, catalogs, errors);
            if (normalized == "onhiteffectrate")
                ValidateOptionalCatalogAttribute(power, effect, "dmgtype",
                    ProtoConstants.KnownDamageTypes.Prepend("All").ToList(), path, catalogs, errors);
        }
        else if (normalized == "protoactionadd")
        {
            ValidateRequiredCatalogAttribute(power, effect, "protoaction", catalogs.ProtoActionNames, path, catalogs, errors);
            ValidateRequiredCatalogAttribute(power, effect, "unittype", catalogs.ProtoUnitNames, path, catalogs, errors);
        }
        else if (normalized == "workratespecific")
        {
            ValidateRequiredCatalogAttribute(power, effect, "unittype", catalogs.ProtoUnitOrTypeNames, path, catalogs, errors);
            ValidateRequiredCatalogAttribute(power, effect, "resource", ProtoConstants.KnownResourceTypes, path, catalogs, errors);
        }
        else if (normalized == "damagebonus")
            ValidateRequiredCatalogAttribute(power, effect, "unittype", catalogs.ProtoUnitOrTypeNames, path, catalogs, errors);
        else if (normalized is "resourcetricklerate" or "resource" or "cost" or
                 "minimumresourcetricklerate" or "maximumresourcetricklerate")
            ValidateRequiredCatalogAttribute(power, effect, "resource", ProtoConstants.KnownResourceTypes, path, catalogs, errors);
        else if (normalized == "armorvulnerability")
        {
            ValidateRequiredCatalogAttribute(power, effect, "armortype", ProtoConstants.KnownArmorTypes,
                path, catalogs, errors);
            if (AttributeValue(effect, "relativity").Equals("Assign", StringComparison.OrdinalIgnoreCase))
                errors.Add($"{path}/@relativity cannot be Assign for ArmorVulnerability.");
        }
        else if (normalized == "damage")
        {
            var damageType = AttributeValue(effect, "damagetype");
            if (damageType.Equals("All", StringComparison.OrdinalIgnoreCase))
                errors.Add($"{path}/@damagetype must be omitted when all damage types are intended.");
            else
                ValidateOptionalCatalogAttribute(power, effect, "damagetype", ProtoConstants.KnownDamageTypes,
                    path, catalogs, errors);
            if (AttributeValue(effect, "allactions").Length > 0)
                errors.Add($"{path}/@allactions must be omitted when all actions are intended for Damage.");
        }
        else if (normalized == "market")
        {
            var component = AttributeValue(effect, "component");
            var components = Names("BuyFactor SellFactor BuyDelta SellDelta BuyFactorSpecific SellFactorSpecific");
            if (component.Length == 0 || (!components.Contains(component) &&
                                         !IsLegacyAttributeValue(effect, "component", component, catalogs)))
                errors.Add($"{path}/@component requires a supported Market component.");
            if (component.EndsWith("Specific", StringComparison.OrdinalIgnoreCase))
                ValidateRequiredCatalogAttribute(power, effect, "resource", ProtoConstants.KnownResourceTypes,
                    path, catalogs, errors);
        }

        if (normalized is "workrate" or "workratespecific" or "rateoffire" or "damagebonus" or
            "workrateall" or "modifyrate")
            ValidateOptionalCatalogAttribute(power, effect, "action", catalogs.ProtoActionNames, path, catalogs, errors);
    }

    private static void ValidateOnHitModify(
        XElement power,
        XElement modify,
        string path,
        PowerSaveCatalogs catalogs,
        List<string> errors)
    {
        var type = AttributeValue(modify, "type");
        if (type.Length == 0)
            errors.Add($"{path}/@type requires a Modify Type.");
        else if (!ProtoConstants.KnownModifyTypes.Contains(type, StringComparer.OrdinalIgnoreCase) &&
                 !IsLegacyAttributeValue(modify, "type", type, catalogs))
            errors.Add($"{path}/@type has unknown Modify Type '{type}'.");
        ValidateNumber(modify.Value, path, new NumericRule("value"), errors);
        if (IsAny(type, "DamageSpecific", "ArmorSpecific"))
        {
            var catalog = type.Equals("ArmorSpecific", StringComparison.OrdinalIgnoreCase)
                ? ProtoConstants.KnownArmorTypes
                : ProtoConstants.KnownDamageTypes;
            ValidateCatalogValue(power, modify, "dmgtype", AttributeValue(modify, "dmgtype"), catalog,
                path + "/@dmgtype", catalogs, required: true, errors);
        }
        foreach (var attribute in new[] { "init", "final" })
        {
            var value = AttributeValue(modify, attribute);
            if (value.Length > 0)
                ValidateNumber(value, path + "/@" + attribute, new NumericRule(attribute), errors);
        }
    }

    private static void ValidateRequiredAttribute(
        XElement element,
        string name,
        string path,
        List<string> errors)
    {
        if (AttributeValue(element, name).Length == 0)
            errors.Add($"{path}/@{name} is required.");
    }

    private static void ValidateRequiredNumberAttribute(
        XElement element,
        string name,
        string path,
        NumberKind kind,
        List<string> errors,
        double? minimum = null)
    {
        var value = AttributeValue(element, name);
        if (value.Length == 0)
        {
            errors.Add($"{path}/@{name} is required.");
            return;
        }
        ValidateNumber(value, $"{path}/@{name}", new NumericRule(name, kind, minimum), errors);
    }

    private static void ValidateRequiredCatalogAttribute(
        XElement power,
        XElement element,
        string name,
        IReadOnlyCollection<string>? catalog,
        string path,
        PowerSaveCatalogs catalogs,
        List<string> errors)
        => ValidateCatalogValue(power, element, name, AttributeValue(element, name), catalog,
            path + "/@" + name, catalogs, required: true, errors);

    private static void ValidateOptionalCatalogAttribute(
        XElement power,
        XElement element,
        string name,
        IReadOnlyCollection<string>? catalog,
        string path,
        PowerSaveCatalogs catalogs,
        List<string> errors)
    {
        var value = AttributeValue(element, name);
        if (value.Length > 0)
            ValidateCatalogValue(power, element, name, value, catalog, path + "/@" + name,
                catalogs, required: false, errors);
    }

    private static void ValidateCatalogValue(
        XElement power,
        XElement element,
        string? attributeName,
        string value,
        IReadOnlyCollection<string>? catalog,
        string path,
        PowerSaveCatalogs catalogs,
        bool required,
        List<string> errors)
    {
        value = value.Trim();
        if (value.Length == 0)
        {
            if (required) errors.Add($"{path} is required.");
            return;
        }
        if (catalog == null || catalog.Count == 0 || catalog.Contains(value, StringComparer.OrdinalIgnoreCase))
            return;
        var legacy = attributeName == null
            ? IsLegacyElementValue(element, value, catalogs)
            : IsLegacyAttributeValue(element, attributeName, value, catalogs);
        if (!legacy)
            errors.Add($"{path} has unknown catalog value '{value}'.");
    }

    private static void ValidateForbiddenNewAttribute(
        XElement power,
        XElement element,
        string name,
        string path,
        PowerSaveCatalogs catalogs,
        List<string> errors)
    {
        var value = AttributeValue(element, name);
        if (value.Length > 0 && !IsLegacyAttributeValue(element, name, value, catalogs))
            errors.Add($"{path}/@{name} is not supported for {AttributeValue(element, "type")}.");
    }

    private static bool IsLegacyElementValue(XElement element, string value, PowerSaveCatalogs catalogs)
        => catalogs.LegacyPowers?.SelectMany(power => power.Descendants())
            .Any(candidate => candidate.Name.LocalName.Equals(element.Name.LocalName,
                                  StringComparison.OrdinalIgnoreCase) &&
                              (candidate.Value.Trim().Equals(value.Trim(), StringComparison.OrdinalIgnoreCase) ||
                               candidate.Value.Split('|', StringSplitOptions.RemoveEmptyEntries |
                                                           StringSplitOptions.TrimEntries)
                                   .Contains(value.Trim(), StringComparer.OrdinalIgnoreCase))) == true;

    private static bool IsLegacyAttributeValue(
        XElement element,
        string attributeName,
        string value,
        PowerSaveCatalogs catalogs)
        => catalogs.LegacyPowers?.SelectMany(power => power.DescendantsAndSelf())
            .Any(candidate => candidate.Name.LocalName.Equals(element.Name.LocalName,
                                  StringComparison.OrdinalIgnoreCase) &&
                              (AttributeValue(candidate, attributeName).Equals(value.Trim(),
                                   StringComparison.OrdinalIgnoreCase) ||
                               attributeName.Equals("excludetypes", StringComparison.OrdinalIgnoreCase) &&
                               AttributeValue(candidate, attributeName)
                                   .Split('|', StringSplitOptions.RemoveEmptyEntries |
                                               StringSplitOptions.TrimEntries)
                                   .Contains(value.Trim(), StringComparer.OrdinalIgnoreCase))) == true;

    private static bool LegacyPowerHasElements(
        XElement currentPower,
        PowerSaveCatalogs catalogs,
        params string[] names)
        => catalogs.LegacyPowers?.Any(legacy =>
            (PowerCatalog.GetName(legacy).Equals(PowerCatalog.GetName(currentPower),
                 StringComparison.OrdinalIgnoreCase) ||
             AttributeValue(legacy, "type").Equals(AttributeValue(currentPower, "type"),
                 StringComparison.OrdinalIgnoreCase)) &&
            names.All(name => legacy.Elements().Any(IsNamed(name)))) == true;

    private static void ValidateAgeAttribute(
        XElement element,
        string attribute,
        string path,
        PowerSaveCatalogs catalogs,
        List<string> errors)
    {
        var age = AttributeValue(element, attribute);
        if (age.Length == 0)
            errors.Add($"{path}/@{attribute} is required.");
        else
            ValidateAgeValue(age, path + "/@" + attribute, catalogs, element, errors);
    }

    private static void ValidateAgeValue(
        string age,
        string path,
        PowerSaveCatalogs catalogs,
        XElement element,
        List<string> errors)
    {
        var ages = Names("ArchaicAge ClassicalAge HeroicAge MythicAge WonderAge");
        if (!ages.Contains(age.Trim()) && !IsLegacyElementValue(element, age, catalogs) &&
            !IsLegacyAttributeValue(element, "age", age, catalogs))
            errors.Add($"{path} has unsupported Age '{age.Trim()}'.");
    }

    private static void ValidateMaximumCount(
        IReadOnlyCollection<XElement> elements,
        int maximum,
        string owner,
        string label,
        List<string> errors)
    {
        if (elements.Count > maximum)
            errors.Add($"{owner} /power/{label} supports at most {maximum} entries.");
    }

    private static void ValidateUniqueAttribute(
        IEnumerable<XElement> elements,
        string attribute,
        string owner,
        string label,
        List<string> errors)
    {
        var duplicate = elements.Select(element => AttributeValue(element, attribute))
            .Where(value => value.Length > 0)
            .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate != null)
            errors.Add($"{owner} /power/{label} contains duplicate {attribute} '{duplicate.Key}'.");
    }

    private static void ValidateUniqueText(
        IEnumerable<XElement> elements,
        string owner,
        string label,
        List<string> errors)
    {
        var duplicate = elements.Select(element => element.Value.Trim()).Where(value => value.Length > 0)
            .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate != null)
            errors.Add($"{owner} /power/{label} contains duplicate value '{duplicate.Key}'.");
    }

    private static string RelativePath(XElement power, XElement element)
    {
        var segments = new Stack<string>();
        for (var current = element; current != power && current != null; current = current.Parent)
        {
            var siblings = current.Parent?.Elements().Where(sibling =>
                sibling.Name.LocalName.Equals(current.Name.LocalName, StringComparison.OrdinalIgnoreCase)).ToList();
            var suffix = siblings is { Count: > 1 } ? $"[{siblings.IndexOf(current) + 1}]" : "";
            segments.Push(current.Name.LocalName + suffix);
        }
        return string.Join('/', segments);
    }

    private static HashSet<string> Names(string values)
        => values.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static bool IsAny(string value, params string[] candidates)
        => candidates.Contains(value, StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<string> ValuesAtPath(XElement owner, string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        IEnumerable<XElement> elements = [owner];
        foreach (var segment in segments)
        {
            if (segment.StartsWith('@'))
            {
                var attributeName = segment[1..];
                return elements.SelectMany(element => element.Attributes()
                    .Where(attribute => attribute.Name.LocalName.Equals(attributeName,
                        StringComparison.OrdinalIgnoreCase))
                    .Select(attribute => attribute.Value));
            }

            elements = elements.SelectMany(element => element.Elements().Where(IsNamed(segment)));
        }

        return elements.Select(element => element.Value);
    }

    private static void ValidateNumber(
        string text,
        string path,
        NumericRule rule,
        List<string> errors)
    {
        // Several shipped records use an empty optional numeric element only to carry attributes
        // (for example <minimapeventtime sendalertto="None" />). Its absence of text is not an
        // invalid numeric edit; required-value rules belong to the owning structured field.
        if (string.IsNullOrWhiteSpace(text))
            return;

        double value = 0;
        bool valid;
        if (rule.Kind == NumberKind.WholeNumber)
        {
            valid = double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture,
                        out value) &&
                    double.IsFinite(value) &&
                    Math.Truncate(value) == value;
        }
        else
        {
            valid = double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture,
                        out value) &&
                    double.IsFinite(value);
        }
        if (!valid)
        {
            errors.Add($"{path} must be a valid " +
                       (rule.Kind == NumberKind.WholeNumber ? "whole number." : "number."));
            return;
        }

        if (rule.Minimum is { } minimum && value < minimum)
            errors.Add($"{path} cannot be less than {minimum.ToString(CultureInfo.InvariantCulture)}.");
        if (rule.Maximum is { } maximum && value > maximum)
            errors.Add($"{path} cannot be greater than {maximum.ToString(CultureInfo.InvariantCulture)}.");
    }

    private static string AttributeValue(XElement element, string name)
        => element.Attributes().FirstOrDefault(attribute =>
            attribute.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value.Trim() ?? "";

    private static Func<XElement, bool> IsNamed(string name)
        => element => element.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase);

    private static PowerSaveValidation Result(IEnumerable<string> errors)
        => new(errors.Distinct(StringComparer.Ordinal).ToList());
}
