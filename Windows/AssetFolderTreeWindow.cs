using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using AoMDivineDataEditor.Classes;

namespace AoMDivineDataEditor.Windows;

internal sealed class AssetFolderTreeWindow : SimpleWindow
{
    private readonly string _rootDirectory;
    private readonly TreeView _tree = new();
    public string? SelectedRelativePath { get; private set; }

    public AssetFolderTreeWindow(string rootDirectory, string rootLabel)
    {
        _rootDirectory = Path.GetFullPath(rootDirectory);
        Directory.CreateDirectory(_rootDirectory);
        Title = $"Choose {rootLabel} Folder";
        Width = 520; Height = 560; MinWidth = 400; MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush.Parse("#141414"); Foreground = Brush.Parse("#d9d9d9");

        var root = new Grid { Margin = new Thickness(16), RowDefinitions = new RowDefinitions("*,Auto") };
        root.Children.Add(_tree);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var newFolder = new Button { Content = "New Folder" };
        var cancel = new Button { Content = "Cancel" };
        var choose = new Button { Content = "Choose", Background = Brush.Parse("#2b7a0b") };
        newFolder.Click += async (_, _) => await CreateFolderAsync(rootLabel);
        cancel.Click += (_, _) => Close();
        choose.Click += (_, _) => Choose();
        buttons.Children.Add(newFolder); buttons.Children.Add(cancel); buttons.Children.Add(choose);
        Grid.SetRow(buttons, 1); root.Children.Add(buttons); Content = root;
        Rebuild(rootLabel);
    }

    private void Rebuild(string rootLabel)
    {
        var rootItem = BuildItem(_rootDirectory, rootLabel, "");
        rootItem.IsExpanded = true;
        _tree.ItemsSource = new[] { rootItem };
        _tree.SelectedItem = rootItem;
    }

    private TreeViewItem BuildItem(string directory, string label, string relative)
    {
        var item = new TreeViewItem { Header = label, Tag = relative };
        try
        {
            foreach (var child in Directory.EnumerateDirectories(directory).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            {
                var childName = Path.GetFileName(child);
                var childRelative = relative.Length == 0 ? childName : relative + "\\" + childName;
                item.Items.Add(BuildItem(child, childName, childRelative));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        return item;
    }

    private async Task CreateFolderAsync(string rootLabel)
    {
        var parentRelative = (_tree.SelectedItem as TreeViewItem)?.Tag as string ?? "";
        var prompt = new InputPromptWindow("New folder name:", confirmButtonText: "Create");
        await prompt.ShowDialog(this);
        if (string.IsNullOrWhiteSpace(prompt.InputText)) return;
        if (!AssetDestinationPolicy.TryResolve(_rootDirectory, parentRelative, prompt.InputText, ".folder", "", out var candidate, out var error))
        { await new Prompt(PromptType.Error, "Invalid folder", error).ShowDialog(this); return; }
        var directory = Path.GetDirectoryName(candidate!.AbsolutePath)!;
        if (Directory.Exists(Path.Combine(directory, prompt.InputText)))
        { await new Prompt(PromptType.Error, "Folder exists", "A folder with this name already exists.").ShowDialog(this); return; }
        Directory.CreateDirectory(Path.Combine(directory, prompt.InputText));
        Rebuild(rootLabel);
    }

    private void Choose()
    {
        SelectedRelativePath = (_tree.SelectedItem as TreeViewItem)?.Tag as string ?? "";
        Close();
    }
}
