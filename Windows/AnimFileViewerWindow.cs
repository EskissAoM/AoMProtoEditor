using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaEdit;
using AoMDivineDataEditor.Classes;

namespace AoMDivineDataEditor.Windows;

internal sealed class AnimFileViewerWindow : SimpleWindow
{
    public AnimFileViewerWindow(string fileName, string xml, string assetLabel = "Anim File")
    {
        Title = $"View {assetLabel} - {fileName}";
        Width = 1000;
        Height = 720;
        MinWidth = 600;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush.Parse("#111311");
        Foreground = Brush.Parse("#E8DECC");

        var preview = new TextEditor
        {
            Text = xml,
            IsReadOnly = true,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            Background = Brush.Parse("#090C0B"),
            Foreground = Brush.Parse("#E8DECC"),
            Margin = new Thickness(12),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };
        XmlSyntaxEditorService.Configure(preview);
        Content = preview;
    }
}
