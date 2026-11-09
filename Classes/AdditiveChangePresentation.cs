using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace AoMDivineDataEditor.Classes;

internal enum AdditiveChangeListKind { Replaced, Removed, Disabled }

/// <summary>Per-editor disclosure state, shared by all additive summary hosts.</summary>
internal sealed class AdditiveChangeListState
{
    private readonly Dictionary<(string Entry, bool Modified, string Section, AdditiveChangeListKind Kind), bool> _expanded = new();
    private string _entry = "";
    private bool _modified;

    internal void SelectEntry(string name, bool modified)
    { _entry = name.ToUpperInvariant(); _modified = modified; }

    internal void ResetEntry(string name, bool modified)
    {
        foreach (var key in _expanded.Keys.Where(key => key.Entry.Equals(name, StringComparison.OrdinalIgnoreCase) && key.Modified == modified).ToArray())
            _expanded.Remove(key);
    }

    internal Border CreateCard(string section, AdditiveChangeListKind kind, IReadOnlyList<string> lines)
    {
        var key = (_entry, _modified, section, kind);
        return AdditiveChangePresentation.CreateChangeList(kind, lines, _expanded.GetValueOrDefault(key), expanded => _expanded[key] = expanded);
    }
}

/// <summary>Read-only mod decoration, independent of tactics markers and value validation.</summary>
internal static class AdditiveChangePresentation
{
    internal static string WithParentPath(string path, string label, Func<string, string> parentLabel)
        => string.Join(" → ", path.Split('/', StringSplitOptions.RemoveEmptyEntries).Skip(1).SkipLast(1)
            .Select(parentLabel).Where(parent => !string.IsNullOrWhiteSpace(parent)).Append(label));

    internal static Border CreateChangeList(AdditiveChangeListKind kind, IReadOnlyList<string> lines,
        bool isExpanded = false, Action<bool>? expansionChanged = null)
    {
        var color = kind == AdditiveChangeListKind.Replaced ? "#A78BFA" : "#F87171";
        var rows = new StackPanel { Spacing = 3, Margin = new Thickness(0, 4, 0, 0) };
        foreach (var line in lines)
            rows.Children.Add(new TextBlock
            {
                Text = line, TextWrapping = TextWrapping.Wrap,
                Foreground = Brush.Parse(color)
            });
        var expander = new Expander
        {
            Header = new TextBlock
            {
                Text = $"{kind} ({lines.Count})", Foreground = Brush.Parse(color), FontWeight = FontWeight.Bold
            },
            Content = rows, IsExpanded = isExpanded, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            BorderThickness = new Thickness(0), Background = Brushes.Transparent, Padding = new Thickness(0), MinHeight = 0
        };
        // The Fluent Expander theme adds its own boxed, right-chevron header.
        // Keep one outer card and use a flat, full-width disclosure header.
        expander.Template = new FuncControlTemplate<Expander>((owner, _) =>
        {
            var arrow = new TextBlock { Width = 16, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            arrow.Bind(TextBlock.TextProperty, new Binding(nameof(Expander.IsExpanded))
            {
                Source = owner, Converter = new FuncValueConverter<bool, string>(expanded => expanded ? "▾" : "▸")
            });
            var header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), IsHitTestVisible = false };
            header.Children.Add(arrow);
            var title = new ContentPresenter { Content = owner.Header, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
            Grid.SetColumn(title, 1); header.Children.Add(title);
            var toggle = new ToggleButton
            {
                // Keep Fluent's pressed/checked theme from moving this flat header.
                Theme = new ControlTheme(typeof(ToggleButton)),
                Content = header, Padding = new Thickness(0, 3), MinHeight = 0,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                Template = new FuncControlTemplate<ToggleButton>((button, _) => new ContentPresenter
                {
                    Content = button.Content, Padding = new Thickness(0, 3), Background = Brushes.Transparent,
                    HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
                })
            };
            toggle.Bind(ToggleButton.IsCheckedProperty, new Binding(nameof(Expander.IsExpanded)) { Source = owner, Mode = BindingMode.TwoWay });
            var body = new ContentPresenter
            {
                Content = owner.Content, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
            };
            body.Bind(Control.IsVisibleProperty, new Binding(nameof(Expander.IsExpanded)) { Source = owner });
            var layout = new StackPanel { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
            layout.Children.Add(toggle); layout.Children.Add(body);
            return layout;
        });
        expander.PropertyChanged += (_, args) =>
        {
            if (args.Property == Expander.IsExpandedProperty) expansionChanged?.Invoke(expander.IsExpanded);
        };
        var card = new Border
        {
            BorderBrush = Brush.Parse(color), BorderThickness = new Thickness(1),
            Background = Brush.Parse(kind == AdditiveChangeListKind.Replaced ? "#18A78BFA" : "#18F87171"),
            Padding = new Thickness(10, 4), Margin = new Thickness(0, 6, 0, 4),
            CornerRadius = new CornerRadius(3), Child = expander
        };
        card.Classes.Add("additive-" + kind.ToString().ToLowerInvariant() + "-list");
        return card;
    }

    internal static Border CreateNotice(string title, string detail)
    {
        var content = new StackPanel { Spacing = 4 };
        content.Children.Add(new TextBlock
        {
            Text = title, Foreground = Brush.Parse("#A78BFA"), FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        if (!string.IsNullOrWhiteSpace(detail))
            content.Children.Add(new TextBlock { Text = detail, TextWrapping = TextWrapping.Wrap });
        return new Border
        {
            BorderBrush = Brush.Parse("#A78BFA"), BorderThickness = new Thickness(1),
            Background = Brush.Parse("#18A78BFA"), Padding = new Thickness(10, 8),
            Margin = new Thickness(0, 6, 0, 4), CornerRadius = new CornerRadius(3), Child = content
        };
    }

    private enum PresentationScope { Node, TextValue, WholeNode }
    private sealed record PresentationSource(XElement Source, string? Field, PresentationScope Scope);
    private static readonly ConditionalWeakTable<Control, PresentationSource> PresentationSources = new();
    private sealed record Decoration(Action Restore);
    private static readonly ConditionalWeakTable<Control, Decoration> Decorations = new();

    internal static void Clear(Control root)
    {
        foreach (var control in root.GetLogicalDescendants().OfType<Control>().Prepend(root))
        {
            PresentationSources.Remove(control);
            if (!Decorations.TryGetValue(control, out var decoration)) continue;
            control.Classes.Remove("mod-changed");
            decoration.Restore();
            Decorations.Remove(control);
        }
    }

    internal static T Bind<T>(T control, XElement source, string? field = null) where T : Control
        => Bind(control, source, field, PresentationScope.Node);

    internal static T BindValue<T>(T control, XElement source) where T : Control
        => Bind(control, source, null, PresentationScope.TextValue);

    internal static T BindContainer<T>(T control, XElement source) where T : Control
        => Bind(control, source, null, PresentationScope.WholeNode);

    private static T Bind<T>(T control, XElement source, string? field, PresentationScope scope) where T : Control
    {
        PresentationSources.Remove(control);
        PresentationSources.Add(control, new(source, field, scope));
        return control;
    }

    internal static T BindXml<T>(T control, XObject source) where T : Control
    {
        if (source is XElement element) return Bind(control, element);
        if (source is XAttribute { Parent: { } owner } attribute) return Bind(control, owner, "@" + attribute.Name.LocalName);
        return control;
    }

    internal static void DecorateBoundFields(Control root, AdditiveReadResult read)
    {
        foreach (var control in root.GetLogicalDescendants().OfType<Control>())
        {
            string? field = null;
            var scope = PresentationScope.Node;
            XElement? source;
            if (PresentationSources.TryGetValue(control, out var presentation))
            { source = presentation.Source; field = presentation.Field; scope = presentation.Scope; }
            else if (!EditorValidationBindings.TryGetXmlSource(control, out source, out field)) continue;
            if (source == null) continue;
            var owner = source;
            var segments = field?.Split('/') ?? [];
            foreach (var segment in segments.Where(segment => !segment.StartsWith('@')))
                owner = owner?.Elements().FirstOrDefault(element => element.Name.LocalName.Equals(segment, StringComparison.OrdinalIgnoreCase));
            if (owner == null) continue;
            var attribute = segments.LastOrDefault()?.StartsWith('@') == true ? segments[^1][1..] : null;
            var change = read.Changes.LastOrDefault(change => ReferenceEquals(change.Result, owner) &&
                (attribute == null || AdditiveIdentityRules.Attribute(change.Patch, attribute) != null) &&
                (scope != PresentationScope.WholeNode || change.Before == null || change.Mode is "add" or "replace"));
            if (change == null) continue;
            if (attribute == null)
            {
                if (scope == PresentationScope.TextValue && change.Before != null &&
                    string.Equals(change.Before.Value, owner.Value, StringComparison.Ordinal)) continue;
                Decorate(control, change);
                if (scope == PresentationScope.WholeNode)
                    ToolTip.SetTip(control, change.Before == null ? "Added section" : "Replaced section");
            }
            else
            {
                var original = change.Before == null ? null : AdditiveIdentityRules.Attribute(change.Before, attribute);
                var modified = AdditiveIdentityRules.Attribute(owner, attribute);
                // Identity attributes in a patch select the original; their presence
                // alone does not mean that their value changed. For an added node,
                // Before is absent so every supplied attribute is new.
                if (!string.Equals(original, modified, StringComparison.Ordinal))
                    DecorateValues(control, original, modified);
            }
        }
    }

    private static string Display(string? value, string fallback)
        => value == null ? fallback : decimal.TryParse(value, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var number)
            ? number.ToString("G29", System.Globalization.CultureInfo.InvariantCulture) : value;

    internal static void Decorate(Control control, AdditiveChange change)
        => DecorateValues(control, change.Before?.Value, change.Result?.Value);

    internal static void DecorateValues(Control control, string? original, string? modified)
    {
        if (!Decorations.TryGetValue(control, out _))
        {
            var tip = ToolTip.GetTip(control);
            var brush = (control as TemplatedControl)?.BorderBrush ?? (control as Border)?.BorderBrush;
            var thickness = (control as TemplatedControl)?.BorderThickness ?? (control as Border)?.BorderThickness ?? default;
            var adorner = control.GetValue(AdornerLayer.AdornerProperty);
            Decorations.Add(control, new(() =>
            {
                ToolTip.SetTip(control, tip);
                if (control is CheckBox) control.SetValue(AdornerLayer.AdornerProperty, adorner);
                else if (control is TemplatedControl input) { input.BorderBrush = brush; input.BorderThickness = thickness; }
                else if (control is Border border) { border.BorderBrush = brush; border.BorderThickness = thickness; }
            }));
        }
        control.Classes.Add("mod-changed");
        if (control is CheckBox)
        {
            // A visual adorner must not reparent the checkbox: tactics source
            // markers own its layout host and rebuild that host during refresh.
            if (control.GetValue(AdornerLayer.AdornerProperty) == null)
                control.SetValue(AdornerLayer.AdornerProperty, new Border
                {
                    BorderBrush = Brush.Parse("#A78BFA"), BorderThickness = new Thickness(1),
                    IsHitTestVisible = false
                });
        }
        else if (control is TemplatedControl input)
        { input.BorderBrush = Brush.Parse("#A78BFA"); input.BorderThickness = new Thickness(1); }
        else if (control is Border chip)
        { chip.BorderBrush = Brush.Parse("#A78BFA"); chip.BorderThickness = new Thickness(1); }
        ToolTip.SetTip(control, $"Original: {Display(original, "Absent")}\nModified: {Display(modified, "Removed")}");
    }
}
