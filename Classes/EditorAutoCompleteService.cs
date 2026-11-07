using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AoMDivineDataEditor.Classes;

/// <summary>
/// Shared, hardened AutoCompleteBox behavior used by the editor surfaces.
/// Keep pointer/focus/dropdown and strict lost-focus validation here so standalone
/// editors do not grow subtly different Avalonia event-order handling.
/// </summary>
public static class EditorAutoCompleteService
{
    private sealed class CatalogBinding
    {
        internal Action<Action>? RefreshStrict;
        internal Func<IEnumerable<string>>? Display;
        internal bool PreserveDisplayOrder;
        internal Func<bool>? IsVisible;
    }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<AutoCompleteBox, CatalogBinding> CatalogBindings = new();
    private static CatalogBinding BindingFor(AutoCompleteBox box) => CatalogBindings.GetValue(box, _ => new CatalogBinding());

    private sealed class DropdownBinding
    {
        internal Func<bool>? IsBusy;
        internal bool SelectAllOnFirstClick;
    }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<AutoCompleteBox, DropdownBinding> DropdownBindings = new();
    internal static Func<bool>? GetBusyPredicate(AutoCompleteBox box)
        => DropdownBindings.TryGetValue(box, out var binding) ? binding.IsBusy : null;

    private static List<string> CatalogOptions(IEnumerable<string> values, bool preserveOrder, string? legacy = null)
    {
        var query = values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim());
        if (!string.IsNullOrWhiteSpace(legacy)) query = query.Append(legacy);
        query = query.Distinct(StringComparer.OrdinalIgnoreCase);
        return (preserveOrder ? query : query.OrderBy(value => value, StringComparer.OrdinalIgnoreCase)).ToList();
    }

    // Keep a deferred source for filtered chip pickers, so the owning view can
    // refresh them without rebuilding chips or clearing in-progress text.
    public static void SetCatalogSuggestions(AutoCompleteBox box, IEnumerable<string> values,
        bool preserveOrder = false, Func<bool>? isVisible = null)
    {
        var binding = BindingFor(box);
        binding.Display = () => values;
        binding.PreserveDisplayOrder = preserveOrder;
        binding.IsVisible = isVisible;
        var text = box.Text;
        var selected = box.SelectedItem as string;
        box.ItemsSource = CatalogOptions(values, preserveOrder);
        if (selected != null) box.SelectedItem = box.ItemsSource.Cast<string>()
            .FirstOrDefault(value => value.Equals(selected, StringComparison.OrdinalIgnoreCase));
        box.Text = text;
    }

    internal static void RefreshCatalogSuggestions(AutoCompleteBox box)
    {
        if (!CatalogBindings.TryGetValue(box, out var binding))
        {
            if (box.ItemsSource is not SelectorCatalog source) return;
            binding = BindingFor(box); binding.Display = () => source;
        }
        var text = box.Text;
        var selected = box.SelectedItem as string;
        void RestoreDisplayAndInput()
        {
            if (binding.Display != null) box.ItemsSource = CatalogOptions(binding.Display(), binding.PreserveDisplayOrder);
            if (selected != null)
                box.SelectedItem = box.ItemsSource?.Cast<object>().OfType<string>()
                    .FirstOrDefault(value => value.Equals(selected, StringComparison.OrdinalIgnoreCase));
            box.Text = text;
            if (binding.IsVisible != null) box.IsVisible = binding.IsVisible();
        }
        if (binding.RefreshStrict != null) binding.RefreshStrict(RestoreDisplayAndInput);
        else RestoreDisplayAndInput();
    }

    public static void FreezeOwnerScrollWhileDropDownIsOpen(AutoCompleteBox autoCompleteBox,
        ScrollViewer ownerScroll, bool closeOnOwnerScroll = false)
    {
        var unsubscribe = SubscribeOwnerScroll(autoCompleteBox, ownerScroll, closeOnOwnerScroll);
        void Detached(object? sender, VisualTreeAttachmentEventArgs args)
        {
            unsubscribe();
            autoCompleteBox.DetachedFromVisualTree -= Detached;
        }
        autoCompleteBox.DetachedFromVisualTree += Detached;
    }

    private sealed class ScrollOwnerBinding
    {
        internal readonly List<Action> Unsubscribe = [];
        internal void Clear()
        {
            foreach (var unsubscribe in Unsubscribe) unsubscribe();
            Unsubscribe.Clear();
        }
    }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<AutoCompleteBox, ScrollOwnerBinding> ScrollOwnerBindings = new();

    /// <summary>Keep a dropdown anchored through nested scroll owners and control reattachment.</summary>
    public static void KeepDropDownAnchored(AutoCompleteBox box)
    {
        if (ScrollOwnerBindings.TryGetValue(box, out _)) return;
        var binding = new ScrollOwnerBinding();
        ScrollOwnerBindings.Add(box, binding);
        void Attach()
        {
            binding.Clear();
            foreach (var owner in box.GetVisualAncestors().OfType<ScrollViewer>())
                binding.Unsubscribe.Add(SubscribeOwnerScroll(box, owner, closeOnOwnerScroll: true));
        }
        box.AttachedToVisualTree += (_, _) => Attach();
        box.DetachedFromVisualTree += (_, _) =>
        {
            box.IsDropDownOpen = false;
            binding.Clear();
        };
        if (TopLevel.GetTopLevel(box) != null) Attach();
    }

    private static Action SubscribeOwnerScroll(AutoCompleteBox autoCompleteBox,
        ScrollViewer ownerScroll, bool closeOnOwnerScroll)
    {
        void FreezeEditorWheel(object? sender, PointerWheelEventArgs args)
        {
            var sourceIsInsideEditor = args.Source is Visual source &&
                (ReferenceEquals(source, ownerScroll) ||
                 source.GetVisualAncestors().Any(ancestor => ReferenceEquals(ancestor, ownerScroll)));
            if (autoCompleteBox.IsDropDownOpen && sourceIsInsideEditor)
                args.Handled = true;
        }

        ownerScroll.AddHandler(InputElement.PointerWheelChangedEvent, FreezeEditorWheel,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        void CloseOnOwnerScroll(object? sender, ScrollChangedEventArgs args)
        {
            // ScrollChanged bubbles: scrolling the popup's list must not be
            // mistaken for movement of this editor owner.
            if (ReferenceEquals(args.Source, ownerScroll) && args.OffsetDelta != default && autoCompleteBox.IsDropDownOpen)
                autoCompleteBox.IsDropDownOpen = false;
        }
        if (closeOnOwnerScroll) ownerScroll.ScrollChanged += CloseOnOwnerScroll;
        return () =>
        {
            ownerScroll.RemoveHandler(InputElement.PointerWheelChangedEvent, FreezeEditorWheel);
            if (closeOnOwnerScroll) ownerScroll.ScrollChanged -= CloseOnOwnerScroll;
        };
    }

    public static void EnableDropdown(
        AutoCompleteBox autoCompleteBox,
        Func<bool>? isBusy = null,
        bool selectAllOnFirstClick = true)
    {
        if (DropdownBindings.TryGetValue(autoCompleteBox, out var existing))
        {
            if (isBusy != null) existing.IsBusy = isBusy;
            existing.SelectAllOnFirstClick = selectAllOnFirstClick;
            EditorFieldInteraction.UpdateBusy(autoCompleteBox, existing.IsBusy);
            return;
        }
        var binding = new DropdownBinding { IsBusy = isBusy, SelectAllOnFirstClick = selectAllOnFirstClick };
        DropdownBindings.Add(autoCompleteBox, binding);
        EditorFieldInteraction.UpdateBusy(autoCompleteBox, isBusy);
        autoCompleteBox.MinimumPrefixLength = 0;
        autoCompleteBox.MinimumPopulateDelay = TimeSpan.Zero;
        bool suppressAutoOpen = false;
        bool userInteracted = false;
        bool selectAllAfterPointerRelease = false;
        int visualLifetime = 0;

        autoCompleteBox.DetachedFromVisualTree += (_, _) =>
        {
            visualLifetime++;
            selectAllAfterPointerRelease = false;
            suppressAutoOpen = true;
        };

        bool Busy() => binding.IsBusy?.Invoke() == true;
        Visual VisualOwner() => autoCompleteBox.GetVisualAncestors().LastOrDefault() ?? autoCompleteBox;

        void SelectAllAfterPointerFocus()
        {
            if (!selectAllAfterPointerRelease || EditorFieldInteraction.IsConfigured(autoCompleteBox))
                return;

            selectAllAfterPointerRelease = false;
            var lifetime = visualLifetime;
            var visualOwner = VisualOwner();
            Dispatcher.UIThread.Post(() =>
            {
                if (lifetime != visualLifetime || !ReferenceEquals(visualOwner, VisualOwner()) ||
                    Busy() || !autoCompleteBox.IsEnabled || EditorFieldInteraction.IsConfigured(autoCompleteBox))
                    return;

                var textEditor = autoCompleteBox.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
                if (textEditor == null)
                    return;

                textEditor.Focus();
                textEditor.SelectAll();
            }, DispatcherPriority.Background);
        }

        void OpenDropdownIfEnabled()
        {
            if (Busy() || !autoCompleteBox.IsEnabled || suppressAutoOpen)
                return;

            var lifetime = visualLifetime;
            var visualOwner = VisualOwner();
            Dispatcher.UIThread.Post(() =>
            {
                if (lifetime == visualLifetime && ReferenceEquals(visualOwner, VisualOwner()) &&
                    !Busy() && autoCompleteBox.IsEnabled && !suppressAutoOpen)
                    autoCompleteBox.IsDropDownOpen = true;
            });
        }

        autoCompleteBox.AddHandler(InputElement.PointerPressedEvent, (_, args) =>
        {
            if (Busy() || !autoCompleteBox.IsEnabled)
                return;
            if (EditorFieldInteraction.IsConfigured(autoCompleteBox) &&
                !EditorFieldInteraction.IsInputSource(autoCompleteBox, args.Source)) return;

            userInteracted = true;
            suppressAutoOpen = false;
            selectAllAfterPointerRelease = binding.SelectAllOnFirstClick && !autoCompleteBox.IsKeyboardFocusWithin;
            OpenDropdownIfEnabled();
        }, RoutingStrategies.Tunnel, handledEventsToo: true);

        autoCompleteBox.AddHandler(InputElement.PointerReleasedEvent, (_, _) =>
        {
            if (Busy() || !autoCompleteBox.IsEnabled)
            {
                selectAllAfterPointerRelease = false;
                return;
            }

            SelectAllAfterPointerFocus();
        }, RoutingStrategies.Bubble, handledEventsToo: true);

        autoCompleteBox.SelectionChanged += (_, _) =>
        {
            suppressAutoOpen = true;
            autoCompleteBox.IsDropDownOpen = false;
        };

        autoCompleteBox.TextChanged += (_, _) =>
        {
            if (Busy() || !autoCompleteBox.IsEnabled)
                return;

            if (userInteracted && string.IsNullOrWhiteSpace(autoCompleteBox.Text))
            {
                suppressAutoOpen = false;
                OpenDropdownIfEnabled();
            }
        };

        autoCompleteBox.LostFocus += (_, _) =>
        {
            suppressAutoOpen = false;
        };
    }

    public static void ConfigureStrict(
        AutoCompleteBox autoCompleteBox,
        IEnumerable<string> suggestions,
        string initialValue,
        Func<bool>? isBusy = null,
        bool preserveUnknownInitialValue = true,
        bool allowEmpty = true,
        bool commitEmptyAsValid = false,
        bool deferSelectionCommit = false,
        bool selectAllOnFirstClick = true,
        bool keepStartVisibleAfterCommit = false,
        bool preserveSuggestionOrder = false,
        Action<string>? valueCommitted = null)
    {
        var normalizedInitial = initialValue?.Trim() ?? string.Empty;
        var suggestionList = CatalogOptions(suggestions, preserveSuggestionOrder,
            preserveUnknownInitialValue ? normalizedInitial : null);

        autoCompleteBox.ItemsSource = suggestionList;
        EnableDropdown(autoCompleteBox, isBusy, selectAllOnFirstClick);
        int visualLifetime = 0;
        bool applyingSelection = false;
        bool refreshingCatalog = false;

        autoCompleteBox.DetachedFromVisualTree += (_, _) =>
        {
            visualLifetime++;
            applyingSelection = false;
        };
        Visual VisualOwner() => autoCompleteBox.GetVisualAncestors().LastOrDefault() ?? autoCompleteBox;

        void KeepStartVisible()
        {
            if (EditorFieldInteraction.IsConfigured(autoCompleteBox))
            {
                EditorFieldInteraction.ShowStart(autoCompleteBox, allowFocused: true);
                return;
            }
            if (!keepStartVisibleAfterCommit)
                return;

            var lifetime = visualLifetime;
            var visualOwner = VisualOwner();
            Dispatcher.UIThread.Post(() =>
            {
                if (lifetime != visualLifetime || !ReferenceEquals(visualOwner, VisualOwner()) ||
                    isBusy?.Invoke() == true)
                    return;

                var textEditor = autoCompleteBox.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
                if (textEditor != null)
                {
                    // Do not let a deferred "show the start" update erase Select All
                    // or a selection the user made while commit work was queued.
                    if (textEditor.SelectionStart == textEditor.SelectionEnd)
                    {
                        textEditor.SelectionStart = 0;
                        textEditor.SelectionEnd = 0;
                        textEditor.CaretIndex = 0;
                    }
                }

                var scrollViewer = autoCompleteBox.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
                if (scrollViewer != null)
                    scrollViewer.Offset = new Vector(0, scrollViewer.Offset.Y);
            }, DispatcherPriority.Background);
        }

        string lastValidValue = suggestionList.FirstOrDefault(x => x.Equals(normalizedInitial, StringComparison.OrdinalIgnoreCase))
            ?? (preserveUnknownInitialValue ? normalizedInitial : string.Empty);
        BindingFor(autoCompleteBox).RefreshStrict = restore =>
        {
            EditorFieldInteraction.Invalidate(autoCompleteBox);
            refreshingCatalog = true;
            try
            {
                visualLifetime++;
                applyingSelection = false;
                suggestionList = CatalogOptions(suggestions, preserveSuggestionOrder,
                    preserveUnknownInitialValue ? lastValidValue : null);
                autoCompleteBox.ItemsSource = suggestionList;
                restore();
                EditorFieldInteraction.ShowStart(autoCompleteBox);
            }
            finally { refreshingCatalog = false; }
        };

        // Editors configure reusable selectors once, then populate them for each selected record.
        // Treat values assigned during population as the current baseline even when a mod value is
        // absent from the loaded catalog. Otherwise a focus/lost-focus cycle can replace untouched
        // legacy data with the previous record's value (or with an empty string).
        autoCompleteBox.PropertyChanged += (_, change) =>
        {
            if (refreshingCatalog || change.Property != AutoCompleteBox.TextProperty) return;
            if (isBusy?.Invoke() == true)
                lastValidValue = autoCompleteBox.Text?.Trim() ?? string.Empty;
            else if (allowEmpty && commitEmptyAsValid && !applyingSelection && autoCompleteBox.IsEnabled &&
                     string.IsNullOrEmpty(autoCompleteBox.Text))
            {
                // Clearing is a draft edit, including before focus leaves the field.
                // Do not let the previous selection restore a required value silently.
                lastValidValue = string.Empty;
                applyingSelection = true;
                try
                {
                    // Let AutoCompleteBox finish clearing its native selection.
                    // Setting SelectedItem here, inside TextProperty's notification,
                    // can restore the previous search text before the edit completes.
                    valueCommitted?.Invoke(string.Empty);
                }
                finally { applyingSelection = false; }
            }
        };

        autoCompleteBox.SelectionChanged += (_, _) =>
        {
            if (refreshingCatalog) return;
            if (autoCompleteBox.SelectedItem is not string selectedValue)
                return;

            lastValidValue = selectedValue;
            if (!autoCompleteBox.IsEnabled)
            {
                if (!string.Equals(autoCompleteBox.Text, selectedValue, StringComparison.Ordinal))
                    autoCompleteBox.Text = selectedValue;
                return;
            }
            if (!deferSelectionCommit)
            {
                autoCompleteBox.Text = selectedValue;
                valueCommitted?.Invoke(selectedValue);
                KeepStartVisible();
                return;
            }

            applyingSelection = true;
            var lifetime = visualLifetime;
            var visualOwner = VisualOwner();
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    if (lifetime != visualLifetime || !ReferenceEquals(visualOwner, VisualOwner()) ||
                        isBusy?.Invoke() == true)
                        return;

                    if (!string.Equals(autoCompleteBox.Text, selectedValue, StringComparison.Ordinal))
                        autoCompleteBox.Text = selectedValue;
                    valueCommitted?.Invoke(selectedValue);
                    KeepStartVisible();
                }
                finally
                {
                    applyingSelection = false;
                }
            }, DispatcherPriority.Background);
        };

        autoCompleteBox.LostFocus += (_, _) =>
        {
            if (isBusy?.Invoke() == true || !autoCompleteBox.IsEnabled)
                return;

            var lifetime = visualLifetime;
            var visualOwner = VisualOwner();
            Dispatcher.UIThread.Post(() =>
            {
                if (lifetime != visualLifetime || !ReferenceEquals(visualOwner, VisualOwner()) ||
                    isBusy?.Invoke() == true ||
                    !autoCompleteBox.IsEnabled || applyingSelection)
                    return;

                var input = autoCompleteBox.Text?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(input))
                {
                    if (!allowEmpty)
                    {
                        autoCompleteBox.Text = lastValidValue;
                        return;
                    }

                    if (!commitEmptyAsValid)
                        return;

                    lastValidValue = string.Empty;
                    autoCompleteBox.SelectedItem = null;
                    autoCompleteBox.Text = string.Empty;
                    valueCommitted?.Invoke(string.Empty);
                    return;
                }

                var match = suggestionList.FirstOrDefault(x => x.Equals(input, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(match))
                {
                    autoCompleteBox.Text = match;
                    lastValidValue = match;
                    valueCommitted?.Invoke(match);
                    KeepStartVisible();
                    return;
                }

                autoCompleteBox.SelectedItem = null;
                autoCompleteBox.Text = lastValidValue;
                valueCommitted?.Invoke(lastValidValue);
            }, DispatcherPriority.Background);
        };
    }
}
