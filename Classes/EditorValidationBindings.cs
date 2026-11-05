using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.LogicalTree;

namespace AoMDivineDataEditor.Classes;

/// <summary>Explicit XML-to-control presentation bindings; never infer fields from error prose.</summary>
internal static class EditorValidationBindings
{
    private sealed record Binding(XElement Source, string? Field, bool Repeated);
    private static readonly ConditionalWeakTable<Control, Binding> Bindings = new();
    private sealed record ModelBinding(object Model, string Field);
    private static readonly ConditionalWeakTable<Control, ModelBinding> Models = new();

    internal static T BindModel<T>(T control, object model, string field) where T : Control
    {
        Models.Remove(control); Models.Add(control, new(model, field)); return control;
    }

    internal static Control? FindModel(Control root, object model, string field)
        => root.GetLogicalDescendants().OfType<Control>().FirstOrDefault(control => control.IsEnabled &&
            Models.TryGetValue(control, out var binding) && ReferenceEquals(binding.Model, model) && binding.Field == field);

    internal static T Bind<T>(T control, XElement source, string? field = null, bool repeated = false) where T : Control
    {
        Bindings.Remove(control); Bindings.Add(control, new(source, field, repeated)); return control;
    }

    internal static T BindXml<T>(T control, XObject source) where T : Control
    {
        if (source is XElement element) return Bind(control, element);
        if (source is XAttribute attribute && attribute.Parent is { } owner)
            return Bind(control, owner, "@" + attribute.Name.LocalName);
        return control;
    }

    // A chip removal edits XML without changing the empty search box's Text.
    // Observe only this bound list, and unsubscribe with the failure lifecycle.
    internal static IDisposable? ObserveRepeatedChanges(Control control, Action changed)
    {
        if (!Bindings.TryGetValue(control, out var binding) || !binding.Repeated) return null;
        void Changing(object? sender, XObjectChangeEventArgs args)
        {
            var element = sender as XElement ?? (sender as XNode)?.Parent ?? (sender as XAttribute)?.Parent;
            var fields = binding.Field?.Split('/', 2) ?? [];
            if (element != null && (element.Parent == binding.Source || element.Parent == null) &&
                fields.Length > 0 && element.Name.LocalName.Equals(fields[0], StringComparison.OrdinalIgnoreCase) &&
                (fields.Length == 1 || fields[1].StartsWith('@') &&
                    (sender is XAttribute attribute && attribute.Name.LocalName.Equals(fields[1][1..], StringComparison.OrdinalIgnoreCase) ||
                     sender is XElement && element.Attributes().Any(attribute => attribute.Name.LocalName.Equals(fields[1][1..], StringComparison.OrdinalIgnoreCase))))) changed();
        }
        binding.Source.Changing += Changing;
        return new Subscription(() => binding.Source.Changing -= Changing);
    }

    private sealed class Subscription(Action unsubscribe) : IDisposable
    {
        private Action? _unsubscribe = unsubscribe;
        public void Dispose() { _unsubscribe?.Invoke(); _unsubscribe = null; }
    }

    internal static Control? Find(Control root, LocatedValidationIssue? issue)
    {
        if (issue?.Location is not { } target) return null;
        foreach (var control in root.GetLogicalDescendants().OfType<Control>().Where(control => control.IsEnabled))
        {
            if (!Bindings.TryGetValue(control, out var binding)) continue;
            var source = ValidationSourceLocation.From(binding.Source);
            var location = ValidationSourceLocation.From(binding.Source, binding.Field);
            if (!location.Record.Equals(target.Record, StringComparison.OrdinalIgnoreCase)) continue;
            if (!binding.Repeated && location.Path.Equals(target.Path, StringComparison.OrdinalIgnoreCase)) return control;
            if (binding.Repeated && MatchesRepeated(source.Path, binding.Field ?? "", target.Path)) return control;
        }
        return null;
    }

    private static bool MatchesRepeated(string source, string field, string target)
    {
        var fields = field.Split('/', 2);
        var prefix = (source.Length == 0 ? "" : source + "/") + fields[0];
        var suffix = fields.Length == 1 ? "" : "/" + fields[1];
        if (target.Equals(prefix + suffix, StringComparison.OrdinalIgnoreCase)) return true;
        if (!target.StartsWith(prefix + "[", StringComparison.OrdinalIgnoreCase)) return false;
        var end = target.IndexOf(']', prefix.Length + 1);
        return end >= 0 && target[(end + 1)..].Equals(suffix, StringComparison.OrdinalIgnoreCase);
    }
}
