using Avalonia.Automation;
using Avalonia.Controls;

namespace AoMDivineDataEditor.Classes;

/// <summary>Static action descriptions only: no item identities, bindings or event subscriptions.</summary>
internal static class EditorButtonDescriptions
{
    public static Button Set(Button button, string description)
    {
        ToolTip.SetTip(button, description);
        AutomationProperties.SetName(button, description);
        return button;
    }
}
