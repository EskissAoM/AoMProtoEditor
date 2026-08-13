using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using AoMDivineDataEditor.Classes;

namespace AoMDivineDataEditor.Windows;

internal sealed class AnimFileViewerWindow : SimpleWindow
{
    public AnimFileViewerWindow(string fileName, string xml)
    {
        Title = $"View Anim File - {fileName}";
        Width = 1000;
        Height = 720;
        MinWidth = 600;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush.Parse("#141414");
        Foreground = Brush.Parse("#d9d9d9");

        var preview = new TextBox
        {
            Text = xml,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            Background = Brush.Parse("#101010"),
            Foreground = Brush.Parse("#a9a9a9"),
            Margin = new Thickness(12),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(
            preview,
            Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(
            preview,
            Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        Content = preview;
    }
}
