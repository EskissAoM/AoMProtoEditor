using AoMDivineDataEditor.Classes;

namespace AoMDivineDataEditor.Windows;

public partial class ProtoEditorWindow
{
    private Func<ProtoEditorSettingsLoadResult> _readSettings = ProtoEditorSettings.LoadCachedSettingsResult;
    private Func<ProtoEditorSettingsLoadResult> _refreshSettings = ProtoEditorSettings.LoadSettingsResult;
    private Func<string, ProtoEditorSettingsSaveResult> _rememberModSettings = ProtoEditorSettings.TryRememberLastModPath;
    private Func<ProtoEditorSettingsLoadResult, string, double, ProtoEditorSettingsSaveResult> _rememberBarSettings = ProtoEditorSettings.TryRememberDataBar;
    private string? _settingsReadWarning, _settingsWriteWarning, _settingsReloadWarning;

    private ProtoEditorSettingsLoadResult ReadHostSettings(bool refresh = false)
    {
        var result = (refresh ? _refreshSettings : _readSettings)();
        _settingsReadWarning = result.Error;
        UpdateSettingsNotice();
        return result;
    }

    private void ReportSettingsSave(ProtoEditorSettingsSaveResult result, string successfulOperation)
    {
        _settingsWriteWarning = result.Succeeded ? null
            : $"{successfulOperation}, but saving its preferences was not confirmed ({result.State}). {result.Error}";
        if (!result.Succeeded && result.RecoveryPaths.Count > 0)
            _settingsWriteWarning += "\nRecovery file candidates: " + string.Join("; ", result.RecoveryPaths);
        UpdateSettingsNotice();
    }

    private void RememberLoadedMod(string path)
        => ReportSettingsSave(_rememberModSettings(path), "The mod was loaded successfully");

    private void RememberExtractedDataBar(ProtoEditorSettingsLoadResult observed, string path, double mtime)
        => ReportSettingsSave(_rememberBarSettings(observed, path, mtime), "Base-game data was loaded successfully");

    private void UpdateSettingsNotice()
    {
        if (_settingsNotice == null) return; // Named controls may still be initializing.
        var messages = new[] { _settingsReadWarning, _settingsWriteWarning, _settingsReloadWarning }
            .Where(message => !string.IsNullOrWhiteSpace(message)).Distinct();
        _settingsNotice.Text = string.Join("\n", messages);
        _settingsNotice.IsVisible = !string.IsNullOrEmpty(_settingsNotice.Text);
    }

    private async Task ApplySettingsWindowResultAsync(ProtoSettingsWindow window)
    {
        if (!window.IsSaved) return;
        ReportSaveFeedback(EditorSaveOutcome.Saved);
        _settingsWriteWarning = null;
        _settingsReloadWarning = null;
        ReadHostSettings(refresh: true);
        try
        {
            // Reload base catalogs without rebuilding the browser or any editor.
            // RefreshUnitList can raise selection events and discard live controls.
            await LoadProtoDataFromBar();
            if (_protoDataBarFile == null)
                _settingsReloadWarning = "Settings were applied, but Data.bar could not be loaded. Review its path in Settings. Current mod edits were kept.";
        }
        catch (Exception exception)
        {
            _settingsReloadWarning = $"Settings were applied, but base-game data could not be refreshed: {exception.Message}. Current mod edits were kept.";
        }
        UpdateSettingsNotice();
    }
}
