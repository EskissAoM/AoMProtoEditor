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
    public static void EnableDropdown(
        AutoCompleteBox autoCompleteBox,
        Func<bool>? isBusy = null,
        bool selectAllOnFirstClick = true)
    {
        autoCompleteBox.MinimumPrefixLength = 0;
        autoCompleteBox.MinimumPopulateDelay = TimeSpan.Zero;
        bool suppressAutoOpen = false;
        bool userInteracted = false;
        bool selectAllAfterPointerRelease = false;

        bool Busy() => isBusy?.Invoke() == true;

        void SelectAllAfterPointerFocus()
        {
            if (!selectAllAfterPointerRelease)
                return;

            selectAllAfterPointerRelease = false;
            Dispatcher.UIThread.Post(() =>
            {
                if (Busy() || !autoCompleteBox.IsEnabled)
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

            Dispatcher.UIThread.Post(() =>
            {
                if (!Busy() && autoCompleteBox.IsEnabled && !suppressAutoOpen)
                    autoCompleteBox.IsDropDownOpen = true;
            });
        }

        autoCompleteBox.AddHandler(InputElement.PointerPressedEvent, (_, _) =>
        {
            if (Busy() || !autoCompleteBox.IsEnabled)
                return;

            userInteracted = true;
            suppressAutoOpen = false;
            selectAllAfterPointerRelease = selectAllOnFirstClick && !autoCompleteBox.IsKeyboardFocusWithin;
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
        var suggestionQuery = suggestions
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var suggestionList = (preserveSuggestionOrder
                ? suggestionQuery
                : suggestionQuery.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            .ToList();

        var normalizedInitial = initialValue?.Trim() ?? string.Empty;
        if (preserveUnknownInitialValue &&
            !string.IsNullOrWhiteSpace(normalizedInitial) &&
            !suggestionList.Any(x => x.Equals(normalizedInitial, StringComparison.OrdinalIgnoreCase)))
        {
            suggestionList.Add(normalizedInitial);
            var distinctSuggestions = suggestionList.Distinct(StringComparer.OrdinalIgnoreCase);
            suggestionList = (preserveSuggestionOrder
                    ? distinctSuggestions
                    : distinctSuggestions.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                .ToList();
        }

        autoCompleteBox.ItemsSource = suggestionList;
        EnableDropdown(autoCompleteBox, isBusy, selectAllOnFirstClick);

        void KeepStartVisible()
        {
            if (!keepStartVisibleAfterCommit)
                return;

            Dispatcher.UIThread.Post(() =>
            {
                if (isBusy?.Invoke() == true)
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
        bool applyingSelection = false;

        autoCompleteBox.SelectionChanged += (_, _) =>
        {
            if (autoCompleteBox.SelectedItem is not string selectedValue)
                return;

            lastValidValue = selectedValue;
            if (!deferSelectionCommit)
            {
                autoCompleteBox.Text = selectedValue;
                valueCommitted?.Invoke(selectedValue);
                KeepStartVisible();
                return;
            }

            applyingSelection = true;
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    if (isBusy?.Invoke() == true)
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
            if (isBusy?.Invoke() == true)
                return;

            Dispatcher.UIThread.Post(() =>
            {
                if (isBusy?.Invoke() == true || applyingSelection)
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
