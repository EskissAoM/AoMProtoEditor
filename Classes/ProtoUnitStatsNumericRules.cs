using System.Globalization;

namespace AoMDivineDataEditor.Classes;

public enum ProtoUnitNumericKind
{
    SignedInteger,
    UnsignedInteger,
    PositiveInteger,
    UnsignedFloat,
    PositiveFloat,
    SignedFloat,
    ClampZeroToOne,
    ClampZeroToFiveInteger,
    ClampMinimumZeroInteger,
    ClampMinimumZeroFloat,
    ClampMinimumOneInteger,
    ClampMinimumTwoInteger,
    ClampZeroToThreeSixtyInteger,
    ClampRgbInteger,
}

public readonly record struct ProtoUnitNumericRule(
    string Label,
    ProtoUnitNumericKind Kind,
    bool AllowEmpty = true);

public readonly record struct ProtoUnitNumericValidation(
    bool IsValid,
    string NormalizedValue,
    string ErrorMessage);

public static class ProtoUnitStatsNumericRules
{
    private static readonly Dictionary<string, ProtoUnitNumericRule> ExactRules =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["obstructionradiusx"] = Float("Obstruction Radius X"),
            ["obstructionradiusz"] = Float("Obstruction Radius Z"),
            ["turnrate"] = Float("Turn Rate"),
            ["maxhitpoints"] = UInt("Hitpoints Max"),
            ["initialhitpoints"] = UInt("Hitpoints Initial"),
            ["unitregen"] = Signed("Hitpoint Regen", false),
            ["unitregen.idletimeout"] = Float("HP Idle Timeout"),
            ["unitregen.damagetimeout"] = Float("HP Damage Timeout"),
            ["unitregen.combatmultiplier"] = Signed("HP Combat Multiplier"),
            ["unitregen.ratelimit"] = Clamp01("HP Rate Limit"),
            ["maxshieldpoints"] = UInt("Shield Max", false),
            ["initialshieldpoints"] = UInt("Shield Initial", false),
            ["unitshieldregen"] = Signed("Shield Regen", false),
            ["unitshieldregen.idletimeout"] = Float("Shield Idle Timeout"),
            ["unitshieldregen.damagetimeout"] = Float("Shield Damage Timeout"),
            ["unitshieldregen.combatmultiplier"] = Signed("Shield Combat Multiplier"),
            ["unitshieldregen.ratelimit"] = Clamp01("Shield Rate Limit"),
            ["trainpoints"] = Float("Train Points"),
            ["buildpoints"] = Float("Build Points"),
            ["populationcount"] = UInt("Population Count"),
            ["los"] = Float("Line of Sight"),
            ["weightclass"] = UInt("Weight Class"),
            ["maxvelocity"] = Float("Max Velocity"),
            ["maxrunvelocity"] = Float("Max Run Velocity"),
            ["buildlimit"] = new("Build Limit", ProtoUnitNumericKind.ClampMinimumOneInteger, false),
            ["maxcontained"] = UInt("Contain"),
            ["containedhitpointbonus"] = Signed("Contain HP Bonus"),
            ["containedspeedbonus"] = Signed("Contain Speed Bonus"),
            ["containedregenrate"] = Signed("Contain Regen Rate"),

            ["autoattackrange"] = Float("Auto Attack Range"),
            ["lifespan"] = Float("Lifespan"),
            ["resourcepriority"] = PosFloat("Resource Priority"),
            ["resourcedecay"] = Float("Resource Decay"),
            ["wanderdistance"] = Float("Wander Distance"),
            ["workersoftlimit"] = UInt("Worker Soft Limit"),
            ["formationorder"] = new("Formation Order", ProtoUnitNumericKind.ClampZeroToFiveInteger),
            ["screenshakeondestruction"] = Float("Screenshake On Destruction"),
            ["displayedrange"] = Float("Displayed Range"),
            ["aistancebasedistance"] = Float("AI Stance Base Distance"),
            ["heighthitpointbaroffset"] = Signed("Height Hitpoint Bar Offset"),
            ["allowedheightvariance"] = Signed("Allowed Height Variance"),
            ["populationcapaddition"] = PosInt("Population Cap Addition"),
            ["gathererlimit"] = UInt("Gatherer Limit"),
            ["prioritybonusfactor"] = Float("Priority Bonus Factor"),
            ["buildingworkrate"] = Float("Building Work Rate"),
            ["trainingrate"] = Float("Training Rate"),
            ["gatherratemultiplier"] = Float("Gather Rate Multiplier"),
            ["partisancount"] = UInt("Partisan Count"),
            ["decaytime"] = Float("Decay Time"),
            ["decaydelaytime"] = Float("Decay Delay Time"),
            ["researchrate"] = Float("Research Rate"),
            ["projectilespinperiod"] = Float("Projectile Spin Period"),
            ["autobuildrate"] = Float("Auto Build Rate"),
            ["godpowerblockradius"] = Float("God Power Block Radius"),
            ["godpowercostfactor"] = Float("God Power Cost Factor"),
            ["builderlimit"] = UInt("Builder Limit"),
            ["corpsedecaydelay"] = Float("Corpse Decay Delay"),
            ["costescalation"] = Float("Cost Escalation"),
            ["bloodscalemodify"] = Signed("Blood Scale"),
            ["bonescalemodify"] = Signed("Bone Scale"),
            ["dodgechance"] = Clamp01("Dodge Chance"),
            ["conversionresistance"] = PosFloat("Conversion Resistance"),
            ["stealthdetectionradius"] = Float("Stealth Detection Radius"),
            ["stealthrevealselfradius"] = Float("Stealth Reveal Self Radius"),
            ["stealthshowsilhouetteradius"] = Float("Stealth Show Silhouette Radius"),

            ["selectionradiusx"] = Float("Selection Radius X"),
            ["selectionradiusz"] = Float("Selection Radius Z"),
            ["placementobstructionradiusx"] = Float("Placement Obstruction X"),
            ["placementobstructionradiusz"] = Float("Placement Obstruction Z"),
            ["farmingradiusx"] = Float("Farming Radius X"),
            ["farmingradiusz"] = Float("Farming Radius Z"),
            ["farmingobstructionradiusx"] = Float("Farming Obstruct X"),
            ["farmingobstructionradiusz"] = Float("Farming Obstruct Z"),
            ["farmingnumstops"] = new("Farming Num Stops", ProtoUnitNumericKind.ClampMinimumTwoInteger),
            ["creationfadetime.value"] = Float("Creation Fade Time"),
            ["creationfadetime.initalpha"] = Signed("Creation Init Alpha"),
            ["heightbob.period"] = Float("Height Bob Period"),
            ["heightbob.magnitude"] = Signed("Height Bob Magnitude"),
            ["initialshading.factor"] = Float("Initial Shading Factor"),
            ["damageshading.threshold"] = Clamp01("Damage Shading Threshold"),
            ["damageshading.rate"] = Float("Damage Shading Rate"),
            ["damageshading.time"] = UInt("Damage Shading Time"),
            ["respawntraindata.respawntime"] = Float("Respawn Time"),
            ["respawntraindata.respawnlimit"] = UInt("Respawn Limit"),
            ["decay.delay"] = Float("Decay Delay"),
            ["decay.duration"] = Float("Decay Duration"),
            ["minimapcolor.red"] = new("Minimap Red", ProtoUnitNumericKind.ClampRgbInteger),
            ["minimapcolor.green"] = new("Minimap Green", ProtoUnitNumericKind.ClampRgbInteger),
            ["minimapcolor.blue"] = new("Minimap Blue", ProtoUnitNumericKind.ClampRgbInteger),
            ["minimapsize"] = Float("Minimap Size"),
            ["replacement.lifespan"] = Float("Replacement Lifespan"),
            ["directionalarmor.angle"] = new("Directional Armor Angle", ProtoUnitNumericKind.ClampZeroToThreeSixtyInteger),
            ["directionalarmor.value"] = Clamp01("Directional Armor Value"),
        };

    public static IReadOnlyDictionary<string, ProtoUnitNumericRule> Rules => ExactRules;

    public static bool TryGetRule(string fieldKey, out ProtoUnitNumericRule rule)
    {
        if (ExactRules.TryGetValue(fieldKey, out rule))
            return true;

        if (fieldKey.StartsWith("carrycapacity:dropoffmultiplier:", StringComparison.OrdinalIgnoreCase))
        {
            rule = Float("Drop Off Multiplier");
            return true;
        }
        if (fieldKey.StartsWith("respawntraindata.", StringComparison.OrdinalIgnoreCase) &&
            fieldKey["respawntraindata.".Length..] is "food" or "wood" or "gold" or "favor")
        {
            rule = Float("Respawn Rate");
            return true;
        }

        var separator = fieldKey.IndexOf(':');
        var family = separator < 0 ? fieldKey : fieldKey[..separator];
        rule = family.ToLowerInvariant() switch
        {
            "cost" => new("Cost", ProtoUnitNumericKind.ClampMinimumZeroInteger),
            "armor" => Clamp01("Armor"),
            "killreward" => UInt("Kill Reward"),
            "resourcereturn" => UInt("Resource Return"),
            "resourcereturnrate" => PosFloat("Resource Return Rate"),
            "carrycapacity" => UInt("Carry Capacity"),
            "initialresource" => UInt("Initial Resource"),
            _ => default,
        };
        return rule.Label is not null;
    }

    public static ProtoUnitNumericValidation Validate(string text, ProtoUnitNumericRule rule)
    {
        var raw = text.Trim();
        if (raw.Length == 0)
            return rule.AllowEmpty
                ? new(true, "", "")
                : Invalid($"{rule.Label} requires a value.");

        if (rule.Kind is ProtoUnitNumericKind.SignedInteger or
            ProtoUnitNumericKind.UnsignedInteger or
            ProtoUnitNumericKind.PositiveInteger or
            ProtoUnitNumericKind.ClampZeroToFiveInteger or
            ProtoUnitNumericKind.ClampMinimumZeroInteger or
            ProtoUnitNumericKind.ClampMinimumOneInteger or
            ProtoUnitNumericKind.ClampMinimumTwoInteger or
            ProtoUnitNumericKind.ClampZeroToThreeSixtyInteger or
            ProtoUnitNumericKind.ClampRgbInteger)
        {
            if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
                return Invalid($"{rule.Label} must be an integer.");

            return rule.Kind switch
            {
                ProtoUnitNumericKind.UnsignedInteger when integer < 0 => Invalid($"{rule.Label} cannot be negative."),
                ProtoUnitNumericKind.PositiveInteger when integer <= 0 => Invalid($"{rule.Label} must be greater than zero."),
                ProtoUnitNumericKind.ClampZeroToFiveInteger => Valid(Math.Clamp(integer, 0, 5)),
                ProtoUnitNumericKind.ClampMinimumZeroInteger => Valid(Math.Max(integer, 0)),
                ProtoUnitNumericKind.ClampMinimumOneInteger => Valid(Math.Max(integer, 1)),
                ProtoUnitNumericKind.ClampMinimumTwoInteger => Valid(Math.Max(integer, 2)),
                ProtoUnitNumericKind.ClampZeroToThreeSixtyInteger => Valid(Math.Clamp(integer, 0, 360)),
                ProtoUnitNumericKind.ClampRgbInteger => Valid(Math.Clamp(integer, 0, 255)),
                _ => Valid(integer),
            };
        }

        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number))
            return Invalid($"{rule.Label} must be a finite number using '.' as the decimal separator.");

        if (rule.Kind == ProtoUnitNumericKind.UnsignedFloat && number < 0)
            return Invalid($"{rule.Label} cannot be negative.");
        if (rule.Kind == ProtoUnitNumericKind.PositiveFloat && number <= 0)
            return Invalid($"{rule.Label} must be greater than zero.");
        if (rule.Kind == ProtoUnitNumericKind.ClampMinimumZeroFloat)
            number = Math.Max(number, 0d);
        if (rule.Kind == ProtoUnitNumericKind.ClampZeroToOne)
            number = Math.Clamp(number, 0d, 1d);

        return new(true, number.ToString("0.################", CultureInfo.InvariantCulture), "");
    }

    public static bool IsIntegerKind(ProtoUnitNumericKind kind)
        => kind is ProtoUnitNumericKind.SignedInteger or
            ProtoUnitNumericKind.UnsignedInteger or
            ProtoUnitNumericKind.PositiveInteger or
            ProtoUnitNumericKind.ClampZeroToFiveInteger or
            ProtoUnitNumericKind.ClampMinimumZeroInteger or
            ProtoUnitNumericKind.ClampMinimumOneInteger or
            ProtoUnitNumericKind.ClampMinimumTwoInteger or
            ProtoUnitNumericKind.ClampZeroToThreeSixtyInteger or
            ProtoUnitNumericKind.ClampRgbInteger;

    public static bool IsClampKind(ProtoUnitNumericKind kind)
        => kind is ProtoUnitNumericKind.ClampZeroToOne or
            ProtoUnitNumericKind.ClampZeroToFiveInteger or
            ProtoUnitNumericKind.ClampMinimumZeroInteger or
            ProtoUnitNumericKind.ClampMinimumZeroFloat or
            ProtoUnitNumericKind.ClampMinimumOneInteger or
            ProtoUnitNumericKind.ClampMinimumTwoInteger or
            ProtoUnitNumericKind.ClampZeroToThreeSixtyInteger or
            ProtoUnitNumericKind.ClampRgbInteger;

    public static bool AllowsNegativeInput(ProtoUnitNumericKind kind)
        => kind is ProtoUnitNumericKind.SignedInteger or
            ProtoUnitNumericKind.SignedFloat or
            ProtoUnitNumericKind.ClampZeroToOne or
            ProtoUnitNumericKind.ClampZeroToFiveInteger or
            ProtoUnitNumericKind.ClampMinimumZeroInteger or
            ProtoUnitNumericKind.ClampMinimumZeroFloat or
            ProtoUnitNumericKind.ClampMinimumOneInteger or
            ProtoUnitNumericKind.ClampMinimumTwoInteger or
            ProtoUnitNumericKind.ClampZeroToThreeSixtyInteger or
            ProtoUnitNumericKind.ClampRgbInteger;

    private static ProtoUnitNumericValidation Valid(long value)
        => new(true, value.ToString(CultureInfo.InvariantCulture), "");

    private static ProtoUnitNumericValidation Invalid(string message) => new(false, "", message);
    private static ProtoUnitNumericRule UInt(string label, bool empty = true) => new(label, ProtoUnitNumericKind.UnsignedInteger, empty);
    private static ProtoUnitNumericRule PosInt(string label, bool empty = true) => new(label, ProtoUnitNumericKind.PositiveInteger, empty);
    private static ProtoUnitNumericRule Float(string label, bool empty = true) => new(label, ProtoUnitNumericKind.UnsignedFloat, empty);
    private static ProtoUnitNumericRule PosFloat(string label, bool empty = true) => new(label, ProtoUnitNumericKind.PositiveFloat, empty);
    private static ProtoUnitNumericRule Signed(string label, bool empty = true) => new(label, ProtoUnitNumericKind.SignedFloat, empty);
    private static ProtoUnitNumericRule Clamp01(string label, bool empty = true) => new(label, ProtoUnitNumericKind.ClampZeroToOne, empty);
}
