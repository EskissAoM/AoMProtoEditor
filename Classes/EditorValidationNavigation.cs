using System.Runtime.CompilerServices;
using AoMDivineDataEditor.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Media;
using Avalonia.LogicalTree;
using Avalonia.Threading;

namespace AoMDivineDataEditor.Classes;

/// <summary>Presentation of an existing validation failure; never validates or changes data.</summary>
internal static class EditorValidationNavigation
{
    private static readonly ConditionalWeakTable<Control, Failure> Current = new();

    internal static void Clear(Control root)
    {
        if (Current.TryGetValue(root, out var failure)) failure.Clear();
        Current.Remove(root);
    }

    internal static Failure Mark(Control root, Control target, Func<bool>? resolved = null)
    {
        Clear(root);
        var failure = new Failure(root, target, resolved);
        Current.Add(root, failure);
        return failure;
    }

    internal sealed class Failure
    {
        private readonly Control _root;
        private readonly Control _target;
        private readonly Func<bool>? _resolved;
        private readonly object? _reportedValue;
        private bool _cleared;
        private readonly List<IDisposable> _appearance = [];
        private IDisposable? _boundChanges;

        internal Failure(Control root, Control target, Func<bool>? resolved)
        {
            _root = root; _target = target; _resolved = resolved;
            _reportedValue = FieldValue();
            target.Classes.Add("invalid-field");
            var brush = root.TryFindResource("DangerBorderBrush", out var resource) && resource is IBrush themed
                ? themed : Brush.Parse("#B55640");
            var painted = target is AssetPathEditor path ? new Control[] { path.CompactPresenter, path.Editor } : [target];
            foreach (var control in painted)
            {
                if (control is TemplatedControl templated)
                {
                    if (templated.SetValue(TemplatedControl.BorderBrushProperty, brush, BindingPriority.Animation) is { } border) _appearance.Add(border);
                    if (templated.SetValue(TemplatedControl.BorderThicknessProperty, new Thickness(2), BindingPriority.Animation) is { } thickness) _appearance.Add(thickness);
                }
                else if (control is Border card)
                {
                    if (card.SetValue(Border.BorderBrushProperty, brush, BindingPriority.Animation) is { } border) _appearance.Add(border);
                    if (card.SetValue(Border.BorderThicknessProperty, new Thickness(2), BindingPriority.Animation) is { } thickness) _appearance.Add(thickness);
                }
            }
            target.PropertyChanged += Changed;
            _boundChanges = EditorValidationBindings.ObserveRepeatedChanges(target, Clear);
            target.DetachedFromVisualTree += Detached;
            root.DetachedFromVisualTree += Detached;
            if (target is AssetPathEditor asset) asset.FullValueChanged += AssetChanged;
            if (target is AutoCompleteBox auto) auto.IsDropDownOpen = false;
            if (target is ComboBox combo) combo.IsDropDownOpen = false;
        }

        private void Changed(object? sender, AvaloniaPropertyChangedEventArgs args)
        {
            if (args.Property == TextBox.TextProperty || args.Property == AutoCompleteBox.TextProperty ||
                args.Property == SelectingItemsControl.SelectedItemProperty || args.Property == ToggleButton.IsCheckedProperty)
            {
                // Without a field-local predicate the next edit invalidates the
                // old report. The authoritative Save validator still checks it.
                if (!Equals(_reportedValue, FieldValue()) && (_resolved == null || _resolved())) Clear();
            }
        }

        private object? FieldValue() => _target switch
        {
            AssetPathEditor asset => asset.FullValue,
            AutoCompleteBox auto => auto.Text ?? "",
            TextBox text => text.Text ?? "",
            SelectingItemsControl selection => selection.SelectedItem,
            ToggleButton toggle => toggle.IsChecked,
            _ => null
        };

        private void Detached(object? sender, VisualTreeAttachmentEventArgs args) => Clear();
        private void AssetChanged(object? sender, EventArgs args)
        {
            if (_resolved == null || _resolved()) Clear();
        }

        internal void Clear()
        {
            if (_cleared) return;
            _cleared = true;
            _target.Classes.Remove("invalid-field");
            foreach (var appearance in _appearance) appearance.Dispose();
            _appearance.Clear();
            _boundChanges?.Dispose(); _boundChanges = null;
            _target.PropertyChanged -= Changed;
            _target.DetachedFromVisualTree -= Detached;
            _root.DetachedFromVisualTree -= Detached;
            if (_target is AssetPathEditor asset) asset.FullValueChanged -= AssetChanged;
        }

        internal async Task RevealAsync(Func<bool>? stillCurrent = null, Action? revealSection = null)
        {
            bool Valid() => !_cleared && (stillCurrent?.Invoke() ?? true) &&
                Current.TryGetValue(_root, out var current) && ReferenceEquals(current, this) &&
                TopLevel.GetTopLevel(_root) != null;
            if (!Valid()) return;
            revealSection?.Invoke();
            foreach (var tab in _target.GetLogicalAncestors().OfType<TabItem>().Reverse())
                if (tab.Parent is TabControl tabs) tabs.SelectedItem = tab;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!Valid()) return;
                _root.UpdateLayout();
                if (!_target.IsEffectivelyVisible || !_target.IsEffectivelyEnabled) return;
                _target.BringIntoView();
                _root.UpdateLayout();
                var focus = _target is AssetPathEditor asset
                    ? (Control)(asset.Editor.IsVisible ? asset.Editor : asset.CompactPresenter) : _target;
                focus.Focus();
            }, DispatcherPriority.Loaded);
        }
    }
}
