using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using AoMDivineDataEditor.Classes;

namespace AoMDivineDataEditor.Windows;

internal sealed class AssetDestinationWindow : SimpleWindow
{
    private readonly string _rootDirectory, _extension, _xmlPrefix, _physicalRootLabel;
    private readonly TextBox _folder = new(), _name = new();
    private readonly TextBlock _physicalPreview = new(), _xmlPreview = new(), _error = new() { Foreground = Brushes.OrangeRed };
    private readonly Button _confirm;
    public AssetDestination? Destination { get; private set; }

    public AssetDestinationWindow(string title, string rootDirectory, string physicalRootLabel, string xmlPrefix, string extension, string defaultName, string defaultFolder = "", string confirmButtonText = "Create")
    {
        _rootDirectory = rootDirectory; _physicalRootLabel = physicalRootLabel; _xmlPrefix = xmlPrefix; _extension = extension;
        Title = title; Width = 680; Height = 390; MinWidth = 560; MinHeight = 350;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush.Parse("#111311"); Foreground = Brush.Parse("#E8DECC");
        var form = new Grid { Margin = new Thickness(18), ColumnDefinitions = new ColumnDefinitions("110,*,Auto"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,*,Auto") };
        AddLabel(form, "Folder", 0); _folder.PlaceholderText = "custom\\units\\special (optional)"; _folder.Text = defaultFolder; Grid.SetColumn(_folder, 1); form.Children.Add(_folder);
        var browse = new Button { Content = "Browse…", Margin = new Thickness(8, 0, 0, 0) }; browse.Click += async (_, _) => await BrowseAsync(); Grid.SetColumn(browse, 2); form.Children.Add(browse);
        AddLabel(form, "File name", 1); _name.Text = defaultName; Grid.SetRow(_name, 1); Grid.SetColumn(_name, 1); form.Children.Add(_name);
        var ext = new TextBlock { Text = extension, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) }; Grid.SetRow(ext, 1); Grid.SetColumn(ext, 2); form.Children.Add(ext);
        AddLabel(form, "Destination", 2); ConfigurePreview(_physicalPreview, 2, form);
        AddLabel(form, "XML value", 3); ConfigurePreview(_xmlPreview, 3, form);
        Grid.SetRow(_error, 4); Grid.SetColumn(_error, 1); Grid.SetColumnSpan(_error, 2); _error.Margin = new Thickness(0, 8, 0, 0); form.Children.Add(_error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancel" }; cancel.Click += (_, _) => Close();
        _confirm = new Button { Content = confirmButtonText, Background = Brush.Parse("#163E26"), BorderBrush = Brush.Parse("#A98243") }; _confirm.Click += (_, _) => Confirm();
        buttons.Children.Add(cancel); buttons.Children.Add(_confirm); Grid.SetRow(buttons, 6); Grid.SetColumn(buttons, 1); Grid.SetColumnSpan(buttons, 2); form.Children.Add(buttons);
        _folder.TextChanged += (_, _) => RefreshPreview(); _name.TextChanged += (_, _) => RefreshPreview(); Content = form; RefreshPreview();
    }

    private static void AddLabel(Grid grid, string text, int row) { var label = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 6, 10, 6) }; Grid.SetRow(label, row); grid.Children.Add(label); }
    private static void ConfigurePreview(TextBlock block, int row, Grid grid) { block.Foreground = Brushes.Gray; block.TextWrapping = TextWrapping.Wrap; block.Margin = new Thickness(0, 8, 0, 0); Grid.SetRow(block, row); Grid.SetColumn(block, 1); Grid.SetColumnSpan(block, 2); grid.Children.Add(block); }
    private void RefreshPreview()
    {
        var valid = AssetDestinationPolicy.TryResolve(_rootDirectory, _folder.Text ?? "", _name.Text ?? "", _extension, _xmlPrefix, out var destination, out var error);
        _confirm.IsEnabled = valid && destination != null && !File.Exists(destination.AbsolutePath);
        _physicalPreview.Text = destination == null ? _physicalRootLabel : _physicalRootLabel.TrimEnd('\\') + "\\" + destination.RelativePath;
        _xmlPreview.Text = destination?.XmlValue ?? "";
        _error.Text = !valid ? error : destination != null && File.Exists(destination.AbsolutePath) ? "A file already exists at this destination." : "";
    }
    private async Task BrowseAsync() { var tree = new AssetFolderTreeWindow(_rootDirectory, Path.GetFileName(_physicalRootLabel.TrimEnd('\\'))); await tree.ShowDialog(this); if (tree.SelectedRelativePath != null) _folder.Text = tree.SelectedRelativePath; }
    private void Confirm()
    {
        if (!AssetDestinationPolicy.TryResolve(_rootDirectory, _folder.Text ?? "", _name.Text ?? "", _extension, _xmlPrefix, out var destination, out var error) || destination == null) { _error.Text = error; return; }
        if (File.Exists(destination.AbsolutePath)) { _error.Text = "A file already exists at this destination."; return; }
        Destination = destination; Close();
    }
}
