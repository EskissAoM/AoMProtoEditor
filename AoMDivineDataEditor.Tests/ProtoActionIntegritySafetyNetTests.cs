using System.Reflection;
using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Windows;
using Xunit;

namespace AoMDivineDataEditor.Tests;

/// <summary>
/// Behavioral safety net for the ProtoAction hardening passes. These tests deliberately
/// exercise XML and overlay behavior rather than checking source-code text or layout.
/// </summary>
public sealed class ProtoActionIntegritySafetyNetTests
{
    public static TheoryData<string> RepresentativeStructuredActions => new()
    {
        """
        <protoaction>
          <name>StackController</name>
          <type>StackControl</type>
          <stackcontrol>
            <stackmax>3</stackmax>
            <stackaddaction>AddStack</stackaddaction>
            <stacksubaction>RemoveStack</stacksubaction>
          </stackcontrol>
          <persistent>1</persistent>
        </protoaction>
        """,
        """
        <protoaction>
          <name>EmpowerAura</name>
          <type>Empower</type>
          <empowerdata active="1">
            <target>Building</target>
            <forbidunittype>AbstractWonder</forbidunittype>
          </empowerdata>
          <enemyempowerdata active="0">
            <target>Unit</target>
          </enemyempowerdata>
          <natureempowerdata active="1">
            <target>AbstractFarm</target>
          </natureempowerdata>
        </protoaction>
        """,
        """
        <protoaction>
          <name>ConditionalChange</name>
          <type>ConditionalTransform</type>
          <conditionaltransformrule type="EnemyInRange">2</conditionaltransformrule>
          <modifyprotoid>ReplacementUnit</modifyprotoid>
          <persistent>1</persistent>
        </protoaction>
        """,
        """
        <protoaction>
          <name>GatherOnlyFood</name>
          <type>AutoGather</type>
          <rate type="Food" resource="Food" yield="1.25">2</rate>
          <donotautogatherunlessgatheringtypes>
            <unittype>AbstractFarm</unittype>
            <unittype>Huntable</unittype>
          </donotautogatherunlessgatheringtypes>
        </protoaction>
        """,
        """
        <protoaction>
          <name>SizeAnimations</name>
          <type>SwitchTactic</type>
          <sizeclassanim class="1">CapsizeSmallShip</sizeclassanim>
          <sizeclassanim class="2">CapsizeLargeShip</sizeclassanim>
          <typedanim type="AbstractShip">Sail</typedanim>
        </protoaction>
        """,
    };

    [Theory]
    [MemberData(nameof(RepresentativeStructuredActions))]
    public void ProtoAction_NoEditRoundTrip_PreservesRepresentativeStructuredFamilies(string actionXml)
    {
        var expected = XElement.Parse(actionXml);
        var unit = new XElement("unit", new XElement(expected));
        var output = new XElement("unit");

        ProtoXmlHandler.SetProtoActions(output, ProtoXmlHandler.GetProtoActions(unit));

        AssertXmlEqual(expected, Assert.Single(output.Elements("protoaction")));
    }

    [Fact]
    public void ProtoAction_NoEditRoundTrip_PreservesCanonicalKnownAndUnknownPayload()
    {
        var original = XElement.Parse(
            """
            <unit name="SafetyNetUnit">
              <protoaction editorhint="preserve-me">
                <name>SafetyNetAttack</name>
                <type>Attack</type>
                <rof>1.25</rof>
                <minrange>2</minrange>
                <maxrange>18</maxrange>
                <damage type="Hack">7.5</damage>
                <damage type="Divine">1</damage>
                <damagebonus type="AbstractInfantry">1.5</damagebonus>
                <rate type="LogicalTypeRangedUnitsAttack" resource="Favor">0.75</rate>
                <onhiteffect type="Snare" duration="2" rate="0.5" />
                <futurefield futureattribute="kept">future-value</futurefield>
                <futurecontainer mode="ordered">
                  <entry id="1">alpha</entry>
                  <entry id="2">beta</entry>
                </futurecontainer>
              </protoaction>
            </unit>
            """);

        var expectedAction = new XElement(Assert.Single(original.Elements("protoaction")));
        var parsed = ProtoXmlHandler.GetProtoActions(original);
        var output = new XElement("unit", new XAttribute("name", "SafetyNetUnit"));

        ProtoXmlHandler.SetProtoActions(output, parsed);

        var actualAction = Assert.Single(output.Elements("protoaction"));
        AssertXmlEqual(expectedAction, actualAction);
        Assert.Equal("preserve-me", (string?)actualAction.Attribute("editorhint"));
        Assert.Equal("kept", (string?)actualAction.Element("futurefield")?.Attribute("futureattribute"));
        Assert.Equal(["alpha", "beta"], actualAction.Element("futurecontainer")?.Elements("entry").Select(x => x.Value));
    }

    [Fact]
    public void ProtoAction_NoEditRoundTrip_PreservesRepeatedChargedAndSameTypeOnHitEffects()
    {
        var original = XElement.Parse(
            """
            <unit name="SageLikeUnit">
              <protoaction>
                <name>Convert</name>
                <type>Convert</type>
                <onhiteffect type="Modifier" modifytype="ArmorSpecific" amount="1" />
                <onhiteffect type="Modifier" modifytype="DamageSpecific" amount="2" />
                <charged>
                  <chargedmodelattachment>vfx\success.xml</chargedmodelattachment>
                  <chargedmodelattachmentbone>vfx_top</chargedmodelattachmentbone>
                </charged>
                <charged>
                  <chargedmodelattachment>vfx\failure.xml</chargedmodelattachment>
                  <chargedmodelattachmentbone>vfx_top</chargedmodelattachmentbone>
                </charged>
              </protoaction>
            </unit>
            """);

        var parsed = ProtoXmlHandler.GetProtoActions(original);
        var output = new XElement("unit", new XAttribute("name", "SageLikeUnit"));

        ProtoXmlHandler.SetProtoActions(output, parsed);

        var action = Assert.Single(output.Elements("protoaction"));
        Assert.Equal(2, action.Elements("onhiteffect").Count());
        Assert.Equal(["1", "2"], action.Elements("onhiteffect").Select(x => (string?)x.Attribute("amount")));
        Assert.Equal(2, action.Elements("charged").Count());
        Assert.Equal(
            [@"vfx\success.xml", @"vfx\failure.xml"],
            action.Elements("charged").Select(x => x.Element("chargedmodelattachment")?.Value));
    }

    [Fact]
    public void TacticsOverlay_GoldenMerge_PreservesInheritanceOverridesAndRepeatedContainers()
    {
        var tacticsAction = new ProtoAction
        {
            Name = "SharedAttack",
            Type = "Attack",
            Rof = "1.0",
            MaxRange = "12"
        };
        tacticsAction.Damages.Add(("Hack", "5"));
        tacticsAction.Damages.Add(("Pierce", "2"));
        tacticsAction.DamageBonuses.Add(("AbstractInfantry", "1.25"));
        AddAdditional(
            tacticsAction,
            "<persistent>1</persistent>",
            "<rate type=\"Food\">1</rate>",
            "<modifyabstracttype>AbstractArcher</modifyabstracttype>",
            "<onhiteffect type=\"Boost\" amount=\"1\" />",
            "<charged><chargedmodelattachment>inherited.xml</chargedmodelattachment></charged>",
            "<futurecontainer><mode>Inherited</mode></futurecontainer>");

        var protoOverlay = new ProtoAction { Name = "SharedAttack", Rof = "2" };
        protoOverlay.Damages.Add(("Hack", "8"));
        protoOverlay.DamageBonuses.Add(("AbstractCavalry", "1.5"));
        AddAdditional(
            protoOverlay,
            "<persistent>0</persistent>",
            "<rate type=\"Food\">2</rate>",
            "<modifyabstracttype>AbstractCavalry</modifyabstracttype>",
            "<onhiteffect type=\"Boost\" amount=\"3\" />",
            "<onhiteffect type=\"Freeze\" duration=\"2\" />",
            "<charged><chargedmodelattachment>success.xml</chargedmodelattachment></charged>",
            "<charged><chargedmodelattachment>failure.xml</chargedmodelattachment></charged>",
            "<futurecontainer><mode>Proto</mode></futurecontainer>");

        var effective = CreateEffectiveSnapshot(
            protoOverlay,
            "SharedAttack",
            "Attack",
            new Dictionary<string, ProtoAction>(StringComparer.OrdinalIgnoreCase)
            {
                ["SharedAttack"] = tacticsAction
            });

        Assert.Equal("SharedAttack", effective.Name);
        Assert.Equal("Attack", effective.Type);
        Assert.Equal("2", effective.Rof);
        Assert.Equal("12", effective.MaxRange);
        Assert.Equal([("Hack", "8"), ("Pierce", "2")], effective.Damages);
        Assert.Equal(
            [("AbstractInfantry", "1.25"), ("AbstractCavalry", "1.5")],
            effective.DamageBonuses);

        Assert.Equal("0", SingleAdditional(effective, "persistent").Value);
        Assert.Equal("2", SingleAdditional(effective, "rate").Value);
        Assert.Equal(
            ["AbstractArcher", "AbstractCavalry"],
            effective.AdditionalElements
                .Where(x => x.Name.LocalName.Equals("modifyabstracttype", StringComparison.OrdinalIgnoreCase))
                .Select(x => x.Value));

        var effects = effective.AdditionalElements
            .Where(x => x.Name.LocalName.Equals("onhiteffect", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.Equal(2, effects.Count);
        Assert.Equal("3", (string?)effects.Single(x => (string?)x.Attribute("type") == "Boost").Attribute("amount"));
        Assert.Equal("2", (string?)effects.Single(x => (string?)x.Attribute("type") == "Freeze").Attribute("duration"));

        var charged = effective.AdditionalElements
            .Where(x => x.Name.LocalName.Equals("charged", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.Equal(2, charged.Count);
        Assert.Equal(
            ["success.xml", "failure.xml"],
            charged.Select(x => x.Element("chargedmodelattachment")?.Value));
        Assert.Equal("Proto", SingleAdditional(effective, "futurecontainer").Element("mode")?.Value);
    }

    [Fact]
    public void ProtoActionFieldAccess_IsCaseInsensitiveWithoutCreatingCaseVariants()
    {
        var action = new ProtoAction();
        action.AdditionalElements.Add(new XElement("MaxSizeClass", "2"));
        action.AdditionalElements.Add(new XElement("maxsizeclass", "3"));

        Assert.Equal("2", ProtoXmlHandler.GetProtoActionSimpleFieldValue(action, "MAXSIZECLASS"));

        ProtoXmlHandler.SetProtoActionSimpleFieldValue(action, "maxsizeclass", "4");

        var value = Assert.Single(action.AdditionalElements);
        Assert.Equal("maxsizeclass", value.Name.LocalName);
        Assert.Equal("4", value.Value);
    }

    [Fact]
    public void ProtoActionMetadata_ProfilesDeclareUniqueNonConflictingFieldOwners()
    {
        var profilesField = typeof(ProtoActionMetadataCatalog).GetField(
            "EditorProfiles",
            BindingFlags.NonPublic | BindingFlags.Static);
        var profiles = Assert.IsAssignableFrom<IReadOnlyDictionary<string, ProtoActionTypeEditorProfile>>(
            profilesField?.GetValue(null));
        var knownFields = ProtoActionMetadataCatalog.GetKnownFieldDefinitions()
            .Select(definition => definition.Tag)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var knownFlags = ProtoActionMetadataCatalog.GetKnownFlagTags()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.NotEmpty(profiles);
        Assert.Equal(
            knownFields.Count,
            ProtoActionMetadataCatalog.GetKnownFieldDefinitions()
                .Select(definition => definition.Tag)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count());

        foreach (var (actionType, profile) in profiles)
        {
            Assert.Equal(
                profile.DefaultVisibleTags.Count,
                profile.DefaultVisibleTags.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.Empty(profile.DefaultVisibleTags.Intersect(profile.HiddenByDefaultTags, StringComparer.OrdinalIgnoreCase));

            foreach (var tag in profile.DefaultVisibleTags.Concat(profile.HiddenByDefaultTags))
            {
                var resolved = ProtoActionMetadataCatalog.GetFieldDefinition(tag);
                Assert.False(string.IsNullOrWhiteSpace(resolved.Tag));
                Assert.Equal(tag, resolved.Tag, ignoreCase: true);
            }

            foreach (var flag in profile.DefaultFlagTags ?? [])
                Assert.True(knownFlags.Contains(flag), $"{actionType} declares unknown default flag '{flag}'.");
        }
    }

    [Fact]
    public void ProtoActionSerialization_IsIdempotentAfterCanonicalization()
    {
        var source = XElement.Parse(
            """
            <unit name="CanonicalUnit">
              <protoaction custom="kept">
                <futurebefore>kept</futurebefore>
                <maxrange>10</maxrange>
                <name>CanonicalAction</name>
                <type>AssistAttack</type>
                <rof>1</rof>
                <minrange>2</minrange>
                <damage type="Hack">4</damage>
                <rate type="Unit">1.5</rate>
              </protoaction>
            </unit>
            """);

        var first = new XElement("unit", new XAttribute("name", "CanonicalUnit"));
        ProtoXmlHandler.SetProtoActions(first, ProtoXmlHandler.GetProtoActions(source));
        var second = new XElement("unit", new XAttribute("name", "CanonicalUnit"));
        ProtoXmlHandler.SetProtoActions(second, ProtoXmlHandler.GetProtoActions(first));

        AssertXmlEqual(first, second);
    }

    private static void AddAdditional(ProtoAction action, params string[] elements)
    {
        foreach (var element in elements)
            action.AdditionalElements.Add(XElement.Parse(element));
    }

    private static XElement SingleAdditional(ProtoAction action, string tag)
        => Assert.Single(
            action.AdditionalElements,
            element => element.Name.LocalName.Equals(tag, StringComparison.OrdinalIgnoreCase));

    private static ProtoAction CreateEffectiveSnapshot(
        ProtoAction protoAction,
        string actionName,
        string actionType,
        IReadOnlyDictionary<string, ProtoAction> tacticsActions)
    {
        var method = typeof(ProtoEditorWindow)
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(candidate =>
                candidate.Name == "CreateEffectiveProtoActionSnapshot" &&
                candidate.GetParameters().Length == 4);

        return Assert.IsType<ProtoAction>(method.Invoke(
            null,
            [protoAction, actionName, actionType, tacticsActions]));
    }

    private static void AssertXmlEqual(XElement expected, XElement actual)
    {
        Assert.True(
            XNode.DeepEquals(expected, actual),
            $"Expected XML:\n{expected}\n\nActual XML:\n{actual}");
    }
}
