using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace AoMDivineDataEditor.Classes;

/// <summary>Display captions only; never use these to rename identifiers or XML keys.</summary>
internal static class EditorDisplayText
{
    internal const string DisplayName = "Display Name";
    internal const string Rollover = "Rollover";
    internal const string ShortRollover = "Short Rollover";
    internal const string AdvancedRollover = "Advanced Rollover";

    internal static string Caption(string key) => key switch
    {
        // The misspelled key is part of the existing Power group/Tag contract.
        "Stun lenght" => "Stun length",
        _ => key
    };

    internal static void ConfigureCaptionPicker(AutoCompleteBox picker)
    {
        picker.ItemTemplate = new FuncDataTemplate<string>((key, _) => new TextBlock { Text = Caption(key ?? "") });
        picker.ItemSelector = (_, item) => Caption(item?.ToString() ?? "");
        picker.FilterMode = AutoCompleteFilterMode.Custom;
        picker.ItemFilter = (search, item) => item is string key &&
            (Caption(key).Contains(search ?? "", StringComparison.OrdinalIgnoreCase) ||
             key.Contains(search ?? "", StringComparison.OrdinalIgnoreCase));
    }
}
