using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using AoMDivineDataEditor.Classes;

namespace AoMDivineDataEditor.Controls;

/// <summary>Shared manager row visuals; callers retain callbacks, tooltips and capability gates.</summary>
internal static class ManagerActionButtons
{
    public static Button Edit(string description = "Edit") => EditorButtonDescriptions.Set(Create(new TextBlock
    {
        Text = "✎",
        FontSize = 16,
        RenderTransform = new ScaleTransform(-1, 1),
        RenderTransformOrigin = RelativePoint.Center
    }), description);

    public static Button Copy(string description = "Duplicate")
    {
        // One inherited-color glyph, rather than white pages with a hard-coded
        // opaque background that stays bright when the button is disabled.
        var button = Create(new PathIcon
        {
            Width = 16, Height = 16,
            Data = Geometry.Parse("M5,1 H15 V13 H13 V3 H5 Z M1,4 H11 V16 H1 Z M3,6 V14 H9 V6 Z")
        });
        button.Classes.Add("copy-import");
        return EditorButtonDescriptions.Set(button, description);
    }

    public static Button Delete(string description = "Delete") => EditorButtonDescriptions.Set(new Button
    {
        Classes = { "remove-button" },
        Margin = new Thickness(4)
    }, description);

    private static Button Create(Control content) => new()
    {
        Content = content,
        Classes = { "manager-action" },
        Margin = new Thickness(4)
    };
}
