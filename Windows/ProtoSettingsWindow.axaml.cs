using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using AoMDivineDataEditor.Classes;
using System.IO;
using System.Threading.Tasks;

namespace AoMDivineDataEditor.Windows;

public partial class ProtoSettingsWindow : SimpleWindow
{
    private string? _dataBarPath;
    private string? _userFolderPath;
    private readonly System.Func<ProtoEditorSettingsLoadResult> _load;
    private readonly System.Func<ProtoEditorSettingsLoadResult, ProtoEditorConfig, bool, ProtoEditorSettingsSaveResult> _save;
    private ProtoEditorSettingsLoadResult _observed = null!;
    private string? _statusMessage;
    private bool _confirmRecovery;

    public string? StatusMessage
    {
        get => _statusMessage;
        private set { _statusMessage = value; OnSelfChanged(); OnSelfChanged(nameof(HasStatusMessage)); }
    }

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);
    public bool CanSaveSettings => _observed.CanSave || NeedsRecoveryConfirmation;
    public bool NeedsRecoveryConfirmation => _observed.State == ProtoEditorSettingsLoadState.Recovered;
    public bool ConfirmRecovery
    {
        get => _confirmRecovery;
        set { _confirmRecovery = value; OnSelfChanged(); }
    }

    public string? DataBarPath
    {
        get => _dataBarPath;
        set { _dataBarPath = value; OnSelfChanged(); }
    }

    public string? UserFolderPath
    {
        get => _userFolderPath;
        set { _userFolderPath = value; OnSelfChanged(); }
    }

    public bool IsSaved { get; private set; }

    public ProtoSettingsWindow() : this(ProtoEditorSettings.LoadSettingsResult, ProtoEditorSettings.TrySaveSettings) { }

    internal ProtoSettingsWindow(ProtoEditorSettingsStore store) : this(store.Load, store.TrySave) { }

    private ProtoSettingsWindow(System.Func<ProtoEditorSettingsLoadResult> load,
        System.Func<ProtoEditorSettingsLoadResult, ProtoEditorConfig, bool, ProtoEditorSettingsSaveResult> save)
    {
        _load = load;
        _save = save;
        _observed = _load();
        InitializeComponent();
        DataContext = this;
        ApplyLoadedSettings();
    }

    private void ApplyLoadedSettings()
    {
        DataBarPath = _observed.Config?.DataBarPath;
        UserFolderPath = _observed.Config?.UserFolderPath;
        ConfirmRecovery = false;
        StatusMessage = _observed.State == ProtoEditorSettingsLoadState.Ambiguous
            ? "Conflicting saved settings were found. Existing files were left untouched; saving is unavailable until the conflict is resolved. You can cancel and continue using the editor."
            : _observed.Error;
        OnSelfChanged(nameof(CanSaveSettings));
        OnSelfChanged(nameof(NeedsRecoveryConfirmation));
    }

    private void Reload_Click(object? sender, RoutedEventArgs e)
    {
        _observed = _load();
        ApplyLoadedSettings();
        StatusMessage ??= "Saved settings reloaded. Unsaved settings edits were discarded.";
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        IsSaved = false;
        Close();
    }

    private async void Save_Click(object? sender, RoutedEventArgs e)
    {
        if (!CanSaveSettings || _observed.Config == null)
        {
            StatusMessage = _observed.Error ?? "Settings could not be loaded. Reload saved settings before saving.";
            await ShowSaveIssueAsync(StatusMessage);
            return;
        }
        if (NeedsRecoveryConfirmation && !ConfirmRecovery)
        {
            StatusMessage = $"Confirm recovery from '{_observed.RecoverySource}' before saving. Cancel leaves the settings files unchanged.";
            await ShowSaveIssueAsync(StatusMessage);
            return;
        }
        var config = _observed.Config;
        config.DataBarPath = DataBarPath;
        config.UserFolderPath = UserFolderPath;
        var result = _save(_observed, config, ConfirmRecovery);
        if (!result.Succeeded)
        {
            StatusMessage = result.State == ProtoEditorSettingsSaveState.Failed
                ? $"The settings save could not be confirmed ({result.State}). {result.Error}"
                : $"Settings were not saved ({result.State}). {result.Error}";
            if (result.RecoveryPaths.Count > 0)
                StatusMessage += "\nRecovery file candidates:\n" + string.Join("\n", result.RecoveryPaths);
            await ShowSaveIssueAsync(StatusMessage);
            // Retain the original baseline and typed values. An explicit reload
            // adopts external changes; blindly retrying must not overwrite them.
            return;
        }

        IsSaved = true;
        Close();
    }

    private Task ShowSaveIssueAsync(string details)
        => IsVisible ? new Prompt(PromptType.Error, "Settings not saved", details).ShowDialog(this)
            : Task.CompletedTask;

    private async void BrowseBar_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Data.bar file",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("BAR Files") { Patterns = ["*.bar"] }]
        });

        if (files.Count > 0)
        {
            DataBarPath = files[0].Path.LocalPath;
        }
    }

    private async void BrowseUser_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Steam User Folder or mods\\local Folder",
            AllowMultiple = false
        });

        if (folders.Count > 0)
        {
            UserFolderPath = folders[0].Path.LocalPath;
        }
    }
}
