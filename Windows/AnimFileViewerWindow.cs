using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaEdit;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Controls;

namespace AoMDivineDataEditor.Windows;

internal sealed class AnimFileViewerWindow : SimpleWindow
{
    private XmlAssetEditingSource? _source;
    private readonly TextEditor _preview;
    private readonly Button _editXml;
    private readonly Button _saveButton;
    private readonly XmlEditSession _session;
    private readonly XmlEditController _controller;
    private readonly EditorSaveFeedback _feedback;
    private readonly string _label;
    private readonly string _fileName;
    private bool _saving;
    private bool _closed;
    private bool _allowClose;
    private bool _promptOpen;
    internal event EventHandler? Saved;

    public AnimFileViewerWindow(string fileName, string xml, string assetLabel = "Anim File", XmlAssetEditingSource? source = null)
    {
        _source = source;
        _label = assetLabel;
        _fileName = fileName;
        Width = 1000;
        Height = 720;
        MinWidth = 600;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush.Parse("#111311");
        Foreground = Brush.Parse("#E8DECC");

        var shell = new DockPanel();
        var header = new DockPanel { Margin = new Thickness(12) };
        DockPanel.SetDock(header, Dock.Top);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        DockPanel.SetDock(buttons, Dock.Right);
        _editXml = new Button { Content = "Edit XML", MinWidth = 80 };
        _saveButton = new Button { Content = "Save", MinWidth = 80, Classes = { "positive" } };
        _saveButton.Click += async (_, _) => await SaveAsync();
        var close = new Button { Content = "Close", MinWidth = 80 };
        close.Click += (_, _) => Close();
        buttons.Children.Add(_editXml); buttons.Children.Add(_saveButton); buttons.Children.Add(close);
        header.Children.Add(buttons);
        header.Children.Add(new TextBlock { Text = fileName, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        shell.Children.Add(header);
        var status = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(12, 4), Foreground = Brush.Parse("#E8DECC") };
        DockPanel.SetDock(status, Dock.Bottom); shell.Children.Add(status);
        _feedback = new EditorSaveFeedback(status); _feedback.Register(this);
        _preview = new TextEditor
        {
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
        XmlSyntaxEditorService.Configure(_preview);
        var preview = XmlPreviewFormatter.Beautify(source?.Text ?? xml);
        _session = new XmlEditSession(source == null ? XmlSyntaxEditorService.PreparePreviewText(preview) : preview, source?.CanEdit == true);
        _controller = new XmlEditController(_preview, _editXml, null, () => _feedback.ClearSuccessOnEdit());
        _session.Changed += UpdateState;
        _controller.Bind(_session, false, source?.CanEdit == true);
        shell.Children.Add(_preview); Content = shell;
        UpdateState();
        AddHandler(InputElement.KeyDownEvent, async (_, e) =>
        {
            if (e.Key == Key.S && e.KeyModifiers.HasFlag(KeyModifiers.Control)) { e.Handled = true; await SaveAsync(); }
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        Closed += (_, _) => { _closed = true; _session.Changed -= UpdateState; _session.Reset(""); _controller.Dispose(); };
    }

    private void UpdateState()
    {
        Title = $"{(_source?.CanEdit == true ? "Edit" : "View")} {_label} - {_fileName}{(_session.IsDirty ? " *" : "")}";
        _saveButton.IsVisible = _source?.CanEdit == true;
        _saveButton.IsEnabled = !_saving && _session.IsEditing;
        ToolTip.SetTip(_preview, _source?.ReadOnlyReason);
    }

    private async Task SaveAsync()
    {
        if (_saving || _closed || _source?.CanEdit != true || _session.CaptureForSave() is not { } snapshot) return;
        _saving = true; UpdateState();
        using var feedback = _feedback.BeginSave();
        try
        {
            if (!_session.IsDirty) { _session.AcceptSaved(snapshot, snapshot.Text); feedback.Complete(); return; }
            var saved = await _source.SaveAsync(snapshot.Text);
            Saved?.Invoke(this, EventArgs.Empty);
            if (_closed) return;
            _session.AcceptSaved(snapshot, XmlPreviewFormatter.Beautify(saved));
            _feedback.Report(EditorSaveOutcome.Saved);
            feedback.Complete(_session.IsDirty);
        }
        catch (XmlAssetSourceChangedException exception)
        {
            if (_closed) return;
            var reload = new Prompt(PromptType.Confirm, $"{_label} changed on disk",
                exception.Message + "\n\nReload the external source and discard this window's XML edits? Cancel keeps your edits.", confirmButtonText: "Reload").FitToContent(620, 360);
            await reload.ShowDialog(this);
            if (!reload.Confirmed) { _feedback.Report(EditorSaveOutcome.Cancelled); return; }
            try
            {
                var current = await _source.ReloadAsync();
                if (_closed) return;
                _source = current; _session.Reset(XmlPreviewFormatter.Beautify(current.Text));
                _controller.Bind(_session, false, current.CanEdit);
                _feedback.Report(EditorSaveOutcome.ReviewRequired);
            }
            catch (Exception error) { await ShowFailureAsync(error, false); }
        }
        catch (Exception exception) { await ShowFailureAsync(exception, exception is InvalidDataException or System.Xml.XmlException or ArgumentException); }
        finally { _saving = false; if (!_closed) UpdateState(); }
    }

    private async Task ShowFailureAsync(Exception exception, bool validation)
    {
        if (_closed) return;
        _feedback.Report(validation ? EditorSaveOutcome.Blocked : EditorSaveOutcome.Failed);
        await new Prompt(PromptType.Error, validation ? $"Invalid {_label} XML" : $"Unable to save {_label}", exception.Message).FitToContent(620, 360).ShowDialog(this);
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (_saving) { e.Cancel = true; return; }
        if (!_allowClose && _session.IsDirty)
        {
            e.Cancel = true;
            if (_promptOpen) return;
            _promptOpen = true;
            try
            {
                var discard = new Prompt(PromptType.Confirm, "Discard XML changes?", $"Close '{_fileName}' without saving its XML changes?", confirmButtonText: "Discard", actionRole: PromptActionRole.Destructive);
                await discard.ShowDialog(this);
                if (discard.Confirmed) { _allowClose = true; Close(); }
            }
            finally { _promptOpen = false; }
            return;
        }
        base.OnClosing(e);
    }
}
