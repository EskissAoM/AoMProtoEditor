using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace AoMDivineDataEditor.Windows;

public partial class ProtoEditorWindow
{
    private sealed record ResourceRowsBinding(string ModuleKey, string ElementName, string? AttributeName,
        string Prefix, XElement[] Originals, ResourceRowsEditor Editor)
    {
        internal string FieldKey(string resource) => AttributeName == null
            ? Prefix + resource.ToLowerInvariant() : Prefix + resource;
        internal string Resource(XElement element) => AttributeName == null ? element.Name.LocalName : (string?)element.Attribute(AttributeName) ?? "";
        internal IEnumerable<XElement> Write(Func<string, string?> multiplier, IEnumerable<XElement>? currentOriginals = null)
        {
            var current = currentOriginals?.ToArray() ?? Originals;
            foreach (var row in Editor.Rows)
            {
                var value = row.Value.Text?.Trim() ?? "";
                if (value.Length == 0) continue;
                var original = current.FirstOrDefault(element => Resource(element).Equals(row.OriginalResource, StringComparison.OrdinalIgnoreCase))
                    ?? current.FirstOrDefault(element => Resource(element).Equals(row.Resource, StringComparison.OrdinalIgnoreCase))
                    ?? Originals.FirstOrDefault(element => Resource(element).Equals(row.OriginalResource, StringComparison.OrdinalIgnoreCase));
                var element = original == null ? new XElement(ElementName) : new XElement(original);
                element.Name = AttributeName == null ? row.Resource.ToLowerInvariant() : ElementName;
                if (AttributeName != null) element.SetAttributeValue(AttributeName, row.Resource);
                element.Nodes().OfType<XText>().Remove();
                element.AddFirst(new XText(value));
                if (ElementName == "carrycapacity")
                {
                    element.Attributes().Where(attribute => attribute.Name.LocalName.Equals("dropoffmultiplier", StringComparison.OrdinalIgnoreCase)).Remove();
                    var factor = multiplier(row.Resource);
                    if (!string.IsNullOrWhiteSpace(factor)) element.SetAttributeValue("dropoffmultiplier", factor);
                }
                yield return element;
            }
            // Unknown resources and ambiguous extra occurrences belong to the source,
            // not to the closed four-resource selector. Keep them while the module exists.
            foreach (var group in current.GroupBy(Resource, StringComparer.OrdinalIgnoreCase))
            {
                var known = ProtoConstants.KnownResourceTypes.Contains(group.Key, StringComparer.OrdinalIgnoreCase);
                if (!known || Editor.Rows.Any(row => row.Resource.Equals(group.Key, StringComparison.OrdinalIgnoreCase)))
                    foreach (var extra in known ? group.Skip(1) : group) yield return new XElement(extra);
            }
        }
    }

    private readonly Dictionary<string, ResourceRowsBinding> _resourceRowEditors = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _removedResourceModules = new(StringComparer.OrdinalIgnoreCase);

    private Grid CreateResourceRowsGrid(string moduleKey, string title, string elementName, string? attribute,
        string prefix, IEnumerable<XElement> originals, Func<string, TextBox> createValue)
    {
        var snapshot = originals.Select(element => new XElement(element)).ToArray();
        string Resource(XElement element) => attribute == null ? element.Name.LocalName : (string?)element.Attribute(attribute) ?? "";
        var initial = snapshot.Where(element => ProtoConstants.KnownResourceTypes.Contains(Resource(element), StringComparer.OrdinalIgnoreCase))
            .GroupBy(Resource, StringComparer.OrdinalIgnoreCase).Select(group =>
                (Resource: ProtoConstants.KnownResourceTypes.First(resource => resource.Equals(group.Key, StringComparison.OrdinalIgnoreCase)),
                 Value: string.Concat(group.First().Nodes().OfType<XText>().Select(text => text.Value)).Trim())).ToArray();
        var unitName = _currentUnitName;
        ResourceRowsEditor? editor = null;
        editor = new ResourceRowsEditor(initial, _isReadOnly, elementName == "ResourceReturnRate" ? "1" : "0", createValue,
            async () =>
            {
                if (!await CheckStartLocalMod()) return false;
                return unitName == _currentUnitName && _resourceRowEditors.TryGetValue(elementName, out var active) && ReferenceEquals(active.Editor, editor);
            }, MarkDirty);
        var binding = new ResourceRowsBinding(moduleKey, elementName, attribute, prefix, snapshot, editor);
        _resourceRowEditors[elementName] = binding;
        void BindFields()
        {
            foreach (var resource in ProtoConstants.KnownResourceTypes) _fieldControls.Remove(binding.FieldKey(resource));
            foreach (var row in editor.Rows) _fieldControls[binding.FieldKey(row.Resource)] = row.Value;
            AttachProtoUnitStatsNumericBehaviors();
        }
        editor.ResourceChanged += (_, _) => BindFields();
        BindFields();
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions($"{EditorFormLayout.LabelColumnWidth},*,Auto"), Margin = new Thickness(0, 2, 0, 2) };
        grid.Children.Add(new TextBlock { Text = title, TextWrapping = Avalonia.Media.TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, EditorFormLayout.LabelGap, 0) });
        Grid.SetColumn(editor, 1); grid.Children.Add(editor);
        return grid;
    }

    private void ForgetResourceRows(string moduleKey, bool markRemoved = false)
    {
        if (markRemoved) _removedResourceModules.Add(moduleKey);
        foreach (var binding in _resourceRowEditors.Values.Where(binding => binding.ModuleKey == moduleKey).ToArray())
        {
            foreach (var resource in ProtoConstants.KnownResourceTypes) _fieldControls.Remove(binding.FieldKey(resource));
            _resourceRowEditors.Remove(binding.ElementName);
        }
    }

    private void WriteResourceRows(XElement unit, string elementName, XElement originalUnit)
    {
        unit.Elements(elementName).Remove();
        if (_resourceRowEditors.TryGetValue(elementName, out var binding))
            unit.Add(binding.Write(resource => _fieldControls.GetValueOrDefault($"carrycapacity:dropoffmultiplier:{resource}") is TextBox input ? input.Text : null,
                originalUnit.Elements(elementName)));
    }

    private XElement? WriteRespawnResourceRows(XElement? currentOriginal)
    {
        if (!_resourceRowEditors.TryGetValue("respawnrates", out var binding)) return null;
        var original = currentOriginal ?? _resourceRespawnRatesSource;
        var result = original == null ? new XElement("respawnrates") : new XElement(original);
        result.Elements().Where(element => ProtoConstants.KnownResourceTypes.Contains(element.Name.LocalName, StringComparer.OrdinalIgnoreCase)).Remove();
        // Unknown rate children already exist in the cloned wrapper.
        result.Add(binding.Write(_ => null, original?.Elements()).Where(element => ProtoConstants.KnownResourceTypes.Contains(element.Name.LocalName, StringComparer.OrdinalIgnoreCase)));
        return result.HasAttributes || result.Elements().Any() || result.Nodes().Any(node => node is not XText text || !string.IsNullOrWhiteSpace(text.Value)) ? result : null;
    }

    private XElement? _resourceRespawnRatesSource;
}
