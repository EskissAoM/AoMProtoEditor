using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class ProtoActionStateStabilityPass4Tests
{
    [Fact]
    public void Draft_DeepCopiesEveryMutableCollection()
    {
        var simple = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["anim"] = "Attack"
        };
        var rate = new ProtoActionStructuredFieldEntry { Value = "2" };
        rate.Attributes["type"] = "Food";
        var structured = new Dictionary<string, List<ProtoActionStructuredFieldEntry>>(StringComparer.OrdinalIgnoreCase)
        {
            ["rate"] = [rate]
        };
        var effect = XElement.Parse("<onhiteffect type=\"Snare\" duration=\"2\" />");
        var charged = XElement.Parse("<charged><chargedremove>true</chargedremove></charged>");
        var selectedFlags = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "rangedlogic", "active" };
        var forcedVisibleFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "charged" };
        var customValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["attack.mode"] = "Ranged Attack"
        };
        var empowerElement = XElement.Parse("<empowerdata><Building><active>1</active></Building></empowerdata>");
        var empowerSections = new Dictionary<string, XElement?>(StringComparer.OrdinalIgnoreCase)
        {
            ["empowerdata"] = empowerElement
        };
        var draft = new ProtoActionDraft();

        draft.Replace(
            7,
            "AttackAction",
            "Attack",
            "Ranged Attack",
            "1.5",
            "2",
            "12",
            [("Hack", "10")],
            [("MythUnit", "2")],
            simple,
            structured,
            selectedFlags,
            forcedVisibleFields,
            customValues,
            empowerSections,
            [new ProtoActionDraft.ElementSnapshot(effect, false)],
            [charged],
            null);

        simple["anim"] = "Changed";
        rate.Value = "99";
        rate.Attributes["type"] = "Wood";
        effect.SetAttributeValue("duration", "99");
        charged.SetElementValue("chargedremove", "false");
        selectedFlags.Clear();
        forcedVisibleFields.Clear();
        customValues["attack.mode"] = "Hand Attack";
        empowerElement.RemoveNodes();

        Assert.Equal(7, draft.Revision);
        Assert.Equal("AttackAction", draft.Name);
        Assert.Equal("Attack", draft.Type);
        Assert.Equal("Ranged Attack", draft.AttackMode);
        Assert.Equal("1.5", draft.RateOfFire);
        Assert.Equal("2", draft.MinRange);
        Assert.Equal("12", draft.MaxRange);
        Assert.Equal(("Hack", "10"), Assert.Single(draft.Damages));
        Assert.Equal(("MythUnit", "2"), Assert.Single(draft.DamageBonuses));
        Assert.Contains("rangedlogic", draft.SelectedFlagTags);
        Assert.Contains("charged", draft.ForcedVisibleFieldTags);
        Assert.Equal("Ranged Attack", draft.CustomValues["attack.mode"]);
        Assert.Equal("Building", Assert.Single(draft.EmpowerSections!["empowerdata"]!.Elements()).Name.LocalName);
        Assert.Equal("Attack", draft.SimpleValues["anim"]);
        var savedRate = Assert.Single(draft.StructuredValues["rate"]);
        Assert.Equal("2", savedRate.Value);
        Assert.Equal("Food", savedRate.Attributes["type"]);
        var savedEffect = Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<ProtoActionDraft.ElementSnapshot>>(draft.OnHitEffects));
        Assert.False(savedEffect.IsSupported);
        Assert.Equal("2", (string?)savedEffect.Element.Attribute("duration"));
        Assert.Equal("true", Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<XElement>>(draft.FullChargedElements)).Element("chargedremove")?.Value);
    }

    [Fact]
    public void Draft_PreservesMissingSnapshotVersusExplicitEmptySnapshot()
    {
        var draft = new ProtoActionDraft();
        var emptySimple = new Dictionary<string, string>();
        var emptyStructured = new Dictionary<string, List<ProtoActionStructuredFieldEntry>>();

        draft.Replace(1, "A", "Attack", "", "", "", "", [], [], emptySimple, emptyStructured, [], [], emptySimple, null, null, null, null);
        Assert.Null(draft.EmpowerSections);
        Assert.Null(draft.OnHitEffects);
        Assert.Null(draft.FullChargedElements);
        Assert.Null(draft.ChargedElements);

        draft.Replace(
            2, "A", "Attack", "", "", "", "", [], [], emptySimple, emptyStructured, [], [], emptySimple,
            new Dictionary<string, XElement?>(), [], [], []);
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyDictionary<string, XElement?>>(draft.EmpowerSections));
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<ProtoActionDraft.ElementSnapshot>>(draft.OnHitEffects));
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<XElement>>(draft.FullChargedElements));
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<XElement>>(draft.ChargedElements));
    }

    [Fact]
    public void RenderTransaction_InvalidatesDeferredAutocompleteWorkFromPriorRevision()
    {
        var source = File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "Windows", "ProtoEditorWindow.axaml.cs")));
        var autocompleteSource = File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "Classes", "EditorAutoCompleteService.cs")));

        Assert.Contains("state.RenderRevision++;", source, StringComparison.Ordinal);
        Assert.Contains("renderState.RenderRevision != renderRevision", source, StringComparison.Ordinal);
        Assert.Contains("_isPopulating = true;", source, StringComparison.Ordinal);
        Assert.Contains("if (isBusy?.Invoke() == true)", autocompleteSource, StringComparison.Ordinal);
    }

    [Fact]
    public void SavePipeline_ConsumesTheCapturedDraftInsteadOfDynamicCardControls()
    {
        var source = File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "Windows", "ProtoEditorWindow.axaml.cs")));

        Assert.Contains("pw.Draft.RateOfFire", source, StringComparison.Ordinal);
        Assert.Contains("pw.Draft.MinRange", source, StringComparison.Ordinal);
        Assert.Contains("pw.Draft.MaxRange", source, StringComparison.Ordinal);
        Assert.Contains("foreach (var kvp in draftSimpleValues)", source, StringComparison.Ordinal);
        Assert.Contains("foreach (var kvp in draftStructuredValues)", source, StringComparison.Ordinal);
        Assert.Contains("pw.Draft.Damages.ToList()", source, StringComparison.Ordinal);
        Assert.Contains("pw.Draft.DamageBonuses.ToList()", source, StringComparison.Ordinal);
        Assert.Contains("pw.Draft.AttackMode", source, StringComparison.Ordinal);
        Assert.Contains("pw.Draft.CopySelectedFlagTags()", source, StringComparison.Ordinal);
        Assert.Contains("pw.Draft.CopyCustomValues()", source, StringComparison.Ordinal);
        Assert.Contains("pw.Draft.CopyEmpowerSections()", source, StringComparison.Ordinal);
        Assert.Contains("RenderProtoActionEmpowerSections(state, currentEmpowerSections)", source, StringComparison.Ordinal);
        Assert.Contains("pw.Draft.CopyOnHitEffects()", source, StringComparison.Ordinal);
        Assert.Contains("pw.Draft.CopyFullChargedElements()", source, StringComparison.Ordinal);
        Assert.Contains("pw.Draft.CopyChargedElements()", source, StringComparison.Ordinal);

        var captureAllIndex = source.IndexOf("foreach (var actionState in _protoActionWidgets)", StringComparison.Ordinal);
        var serializeIndex = source.IndexOf("foreach (var pw in _protoActionWidgets)", captureAllIndex, StringComparison.Ordinal);
        Assert.True(captureAllIndex >= 0 && serializeIndex > captureAllIndex);
        Assert.Contains("pa.Name = pw.Draft.Name;", source, StringComparison.Ordinal);
        Assert.Contains("pa.Type = pw.Draft.Type;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("pa.Name = pw.NameAcb.Text", source, StringComparison.Ordinal);
    }
}
