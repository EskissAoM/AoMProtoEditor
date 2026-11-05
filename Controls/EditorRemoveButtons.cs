using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using AoMDivineDataEditor.Classes;

namespace AoMDivineDataEditor.Controls;

/// <summary>Shared removal scope and placement. The owning editor supplies the removal callback.</summary>
internal static class EditorRemoveButtons
{
    public static Button Field(Thickness? margin = null) => EditorButtonDescriptions.Set(Compact("remove-field", margin), "Remove field");
    public static Button Row(Thickness? margin = null, string description = "Remove row")
        => EditorButtonDescriptions.Set(Compact("remove-row", margin), description);

    public static Button Card(Thickness? margin = null, string description = "Remove card") => EditorButtonDescriptions.Set(new Button
    {
        Classes = { "remove-button", "remove-card" },
        Margin = margin ?? new Thickness(6, 0, 0, 0),
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Top
    }, description);

    private static Button Compact(string scope, Thickness? margin) => new()
    {
        Classes = { "remove-button", scope },
        Margin = margin ?? new Thickness(EditorFormLayout.CrossGap, 0, 0, 0),
        VerticalAlignment = VerticalAlignment.Center
    };
}
