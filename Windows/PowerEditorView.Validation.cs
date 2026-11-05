using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using AoMDivineDataEditor.Classes;

namespace AoMDivineDataEditor.Windows;

public partial class PowerEditorView
{
    // A fixed field reused by another Power type can retain old display text.
    // Ignore its hidden section, independently of which main tab is selected.
    // The document validator still checks the actual saved XML.
    private bool IsActiveSpecificNumber(Control control)
        => control.IsVisible && control.GetLogicalAncestors().OfType<Control>()
            .TakeWhile(parent => !ReferenceEquals(parent, _specificScroll)).All(parent => parent.IsVisible);

    private string NumericValidationFailureAt(string message, XElement source, string? field = null)
    {
        _numericValidationTarget = EditorValidationBindings.Find(this,
            new LocatedValidationIssue(message, ValidationSourceLocation.From(source, field)));
        return message;
    }

    // Resolve freshly rebuilt rows at the validation boundary using their XML owners.
    private void BindPowerValidationFields()
    {
        if (_currentEntry is not { } entry) return;
        foreach (var control in this.GetLogicalDescendants().OfType<Control>().Distinct())
        {
            // Card/add/remove buttons also carry XML scope in Tag. They are
            // not editable values and must never become a validation target.
            if (control is not (TextBox or AutoCompleteBox or ComboBox)) continue;
            if (control.Tag is XElement element) EditorValidationBindings.Bind(control, element);
            else if (control.Tag is XAttribute attribute && attribute.Parent is { } owner)
                EditorValidationBindings.Bind(control, owner, "@" + attribute.Name.LocalName);
        }
        EditorValidationBindings.Bind(_switchingIntervalBox, entry, "interval");
        EditorValidationBindings.Bind(_swapMinAmountBox, entry, "convertusingminmax/@minswapamount");
        EditorValidationBindings.Bind(_swapMaxAmountBox, entry, "convertusingminmax/@maxswapamount");
        EditorValidationBindings.Bind(_lureAttractTypePicker, entry, "attracttype", repeated: true);
        EditorValidationBindings.Bind(_witherKillTypePicker, entry, "killunittype", repeated: true);
        if (PowerCatalog.FindField(entry, "duplicatetargetreplacement") is { } replacements)
            EditorValidationBindings.Bind(_powerCloneReplacementPicker, replacements, "target", repeated: true);
        foreach (var (type, radius, element) in _placementRestrictionFields)
        { EditorValidationBindings.Bind(type, element); EditorValidationBindings.Bind(radius, element, "@radius"); }
        foreach (var (unit, factor, element) in _droughtDamageFields)
        { EditorValidationBindings.Bind(unit, element); EditorValidationBindings.Bind(factor, element, "@fraction"); }
        foreach (var (unit, cap, element) in _greatFloodDamageCapFields)
        { EditorValidationBindings.Bind(unit, element, "@unittype"); EditorValidationBindings.Bind(cap, element); }
        foreach (var (age, number, element) in _foxCharmAgeFields)
        { EditorValidationBindings.Bind(age, element); EditorValidationBindings.Bind(number, element, "@affectedNumber"); }
        foreach (var (age, food, spawns, element) in _lureAgeInfoFields)
        { EditorValidationBindings.Bind(age, element); EditorValidationBindings.Bind(food, element, "@maxfood"); EditorValidationBindings.Bind(spawns, element, "@numspawns"); }
        foreach (var (target, _, element) in _undermineTargetFields) EditorValidationBindings.Bind(target, element);
        foreach (var (proto, radius, theta, element) in _arcadianGrassFields)
        { EditorValidationBindings.Bind(proto, element); EditorValidationBindings.Bind(radius, element, "@r"); EditorValidationBindings.Bind(theta, element, "@theta"); }
        foreach (var (segment, reduction, element) in _damageTierFields)
        { EditorValidationBindings.Bind(segment, element, "@mindestroyed"); EditorValidationBindings.Bind(reduction, element); }
        foreach (var (type, damage, element) in _rootDamageFields)
        { EditorValidationBindings.Bind(type, element); EditorValidationBindings.Bind(damage, element, "@damage"); }
        foreach (var (quantity, age, adjustment) in _tempUnitAgeFields)
        {
            EditorValidationBindings.Bind(age, adjustment);
        }
        foreach (var (age, food, element) in _greatHuntMaxFoodFields)
        { EditorValidationBindings.Bind(age, element, "@age"); EditorValidationBindings.Bind(food, element); }
        foreach (var (target, drift, element) in _fimbulSnowDriftFields)
        { EditorValidationBindings.Bind(target, element, "@target"); EditorValidationBindings.Bind(drift, element); }
        foreach (var (resource, multiplier, element) in _cageResourceMultiplierFields)
        { EditorValidationBindings.Bind(resource, element, "@resourcetype"); EditorValidationBindings.Bind(multiplier, element); }
    }

    private static void BindPowerEffectTarget(Control control, XElement effect, params string[] kinds)
    {
        var targets = effect.Elements().Where(child => child.Name.LocalName.Equals("target", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (targets.Length == 1 && kinds.Contains(targets[0].Attributes().FirstOrDefault(attribute =>
                attribute.Name.LocalName.Equals("type", StringComparison.OrdinalIgnoreCase))?.Value ?? "", StringComparer.OrdinalIgnoreCase))
            EditorValidationBindings.Bind(control, targets[0]);
    }
}
