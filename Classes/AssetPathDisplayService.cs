using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;

namespace CryBarEditor.Classes;

public sealed record PathSuggestion(string FullValue, string DisplayValue)
{
    public override string ToString() => DisplayValue;
}

public static class AssetPathDisplayService
{
    private static readonly string[] IgnoredDisplayPrefixes = ["resources"];
    private static readonly ConditionalWeakTable<Control, PathState> States = new();

    public static IReadOnlyList<PathSuggestion> CreateSuggestions(IEnumerable<string> values)
    {
        var fullPaths = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .GroupBy(NormalizeForComparison, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        var normalized = fullPaths.ToDictionary(value => value, NormalizeForComparison, StringComparer.OrdinalIgnoreCase);
        var displayByFullValue = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in fullPaths.GroupBy(value => FileName(normalized[value]), StringComparer.OrdinalIgnoreCase))
        {
            var paths = group.ToList();
            if (paths.Count == 1)
            {
                displayByFullValue[paths[0]] = group.Key;
                continue;
            }

            var segments = paths.ToDictionary(path => path, path => PrefixSegments(normalized[path]), StringComparer.OrdinalIgnoreCase);
            foreach (var path in paths)
            {
                var prefix = segments[path];
                var length = 1;
                while (length < prefix.Length && paths.Any(other =>
                    !other.Equals(path, StringComparison.OrdinalIgnoreCase) &&
                    SameLeadingSegments(prefix, segments[other], length)))
                {
                    length++;
                }

                var labelPrefix = string.Join("\\", prefix.Take(length));
                displayByFullValue[path] = string.IsNullOrEmpty(labelPrefix)
                    ? group.Key
                    : $"{labelPrefix}\\...\\{group.Key}";
            }
        }

        return fullPaths
            .Select(value => new PathSuggestion(value, displayByFullValue[value]))
            .OrderBy(value => value.DisplayValue, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static void ConfigureSelector(AutoCompleteBox control, IEnumerable<string> sourceValues, string fullValue)
    {
        EditorTextFieldStyle.ConfigureSelector(control);
        var allValues = sourceValues.Append(fullValue);
        var suggestions = CreateSuggestions(allValues);
        var state = new PathState(suggestions, fullValue.Trim());
        States.Remove(control);
        States.Add(control, state);
        control.ItemsSource = suggestions;
        SetDisplayText(control, state, state.FullValue);
        ToolTip.SetTip(control, state.FullValue);

        control.SelectionChanged += (_, _) =>
        {
            if (state.IsUpdatingDisplay)
                return;

            if (control.SelectedItem is PathSuggestion suggestion)
            {
                state.FullValue = suggestion.FullValue;
                SetDisplayText(control, state, state.FullValue);
                ToolTip.SetTip(control, state.FullValue);
            }
        };
        control.GotFocus += (_, _) => SetText(control, state, state.FullValue);
        control.LostFocus += (_, _) =>
        {
            if (!state.IsUpdatingDisplay)
                state.FullValue = control.Text?.Trim() ?? string.Empty;
            SetDisplayText(control, state, state.FullValue);
            ToolTip.SetTip(control, state.FullValue);
        };
        control.TextChanged += (_, _) =>
        {
            if (!state.IsUpdatingDisplay && control.IsFocused)
                state.FullValue = control.Text?.Trim() ?? string.Empty;
        };
    }

    public static void ConfigureTextBox(TextBox control, string fullValue)
    {
        EditorTextFieldStyle.ConfigureTextBox(control);
        var state = new PathState(CreateSuggestions([fullValue]), fullValue.Trim());
        States.Remove(control);
        States.Add(control, state);
        SetDisplayText(control, state, state.FullValue);
        ToolTip.SetTip(control, state.FullValue);
        control.GotFocus += (_, _) => SetText(control, state, state.FullValue);
        control.LostFocus += (_, _) =>
        {
            if (!state.IsUpdatingDisplay)
                state.FullValue = control.Text?.Trim() ?? string.Empty;
            SetDisplayText(control, state, state.FullValue);
            ToolTip.SetTip(control, state.FullValue);
        };
        control.TextChanged += (_, _) =>
        {
            if (!state.IsUpdatingDisplay && control.IsFocused)
                state.FullValue = control.Text?.Trim() ?? string.Empty;
        };
    }

    public static string GetFullValue(Control control) => States.TryGetValue(control, out var state)
        ? state.FullValue
        : control switch
        {
            TextBox textBox => textBox.Text?.Trim() ?? string.Empty,
            AutoCompleteBox selector => selector.Text?.Trim() ?? string.Empty,
            _ => string.Empty,
        };

    private static void SetDisplayText(AutoCompleteBox control, PathState state, string fullValue)
        => SetText(control, state, state.Suggestions.FirstOrDefault(item => item.FullValue.Equals(fullValue, StringComparison.OrdinalIgnoreCase))?.DisplayValue ?? fullValue);

    private static void SetDisplayText(TextBox control, PathState state, string fullValue)
        => SetText(control, state, state.Suggestions.FirstOrDefault(item => item.FullValue.Equals(fullValue, StringComparison.OrdinalIgnoreCase))?.DisplayValue ?? fullValue);

    private static void SetText(AutoCompleteBox control, PathState state, string value)
    {
        state.IsUpdatingDisplay = true;
        control.Text = value;
        state.IsUpdatingDisplay = false;
    }

    private static void SetText(TextBox control, PathState state, string value)
    {
        state.IsUpdatingDisplay = true;
        control.Text = value;
        state.IsUpdatingDisplay = false;
    }

    private static string NormalizeForComparison(string value) => value.Replace('/', '\\').Trim();

    private static string FileName(string normalizedPath)
        => normalizedPath.Split('\\', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? normalizedPath;

    private static string[] PrefixSegments(string normalizedPath)
    {
        var segments = normalizedPath.Split('\\', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (segments.Count > 0 && IgnoredDisplayPrefixes.Contains(segments[0], StringComparer.OrdinalIgnoreCase))
            segments.RemoveAt(0);
        return segments.Take(Math.Max(0, segments.Count - 1)).ToArray();
    }

    private static bool SameLeadingSegments(string[] left, string[] right, int length)
        => left.Length >= length && right.Length >= length &&
           left.Take(length).SequenceEqual(right.Take(length), StringComparer.OrdinalIgnoreCase);

    private sealed class PathState(IReadOnlyList<PathSuggestion> suggestions, string fullValue)
    {
        public IReadOnlyList<PathSuggestion> Suggestions { get; } = suggestions;
        public string FullValue { get; set; } = fullValue;
        public bool IsUpdatingDisplay { get; set; }
    }
}
