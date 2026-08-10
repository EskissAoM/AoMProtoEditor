using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace CryBarEditor.Classes;

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
        bool selectedAllForCurrentFocus = false;

        bool Busy() => isBusy?.Invoke() == true;

        void SelectAllOnInitialPointerFocus()
        {
            if (!selectAllOnFirstClick || selectedAllForCurrentFocus)
                return;

            selectedAllForCurrentFocus = true;
            Dispatcher.UIThread.Post(() =>
            {
                if (!autoCompleteBox.IsEnabled)
                    return;

                var textEditor = autoCompleteBox.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
                if (textEditor == null)
                    return;

                textEditor.Focus();
                textEditor.SelectAll();
            }, DispatcherPriority.Input);
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
            SelectAllOnInitialPointerFocus();
            OpenDropdownIfEnabled();
        }, RoutingStrategies.Tunnel, handledEventsToo: true);

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
            selectedAllForCurrentFocus = false;
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
        Action<string>? valueCommitted = null)
    {
        var suggestionList = suggestions
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var normalizedInitial = initialValue?.Trim() ?? string.Empty;
        if (preserveUnknownInitialValue &&
            !string.IsNullOrWhiteSpace(normalizedInitial) &&
            !suggestionList.Any(x => x.Equals(normalizedInitial, StringComparison.OrdinalIgnoreCase)))
        {
            suggestionList.Add(normalizedInitial);
            suggestionList = suggestionList
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        autoCompleteBox.ItemsSource = suggestionList;
        EnableDropdown(autoCompleteBox, isBusy);

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
                return;
            }

            applyingSelection = true;
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    if (!string.Equals(autoCompleteBox.Text, selectedValue, StringComparison.Ordinal))
                        autoCompleteBox.Text = selectedValue;
                    valueCommitted?.Invoke(selectedValue);
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
                    return;
                }

                autoCompleteBox.SelectedItem = null;
                autoCompleteBox.Text = lastValidValue;
                valueCommitted?.Invoke(lastValidValue);
            }, DispatcherPriority.Background);
        };
    }
}
