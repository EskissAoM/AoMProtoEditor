using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

using AoMDivineDataEditor.Classes;

using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace AoMDivineDataEditor.Windows;

public partial class Prompt : SimpleWindow
{
    string _title = "Prompt title";
    string _text = "Prompt text";
    bool _canClose = true;
    PromptType _type = PromptType.Information;
    Progress<string?>? _progressReporter;
    Progress<double>? _progressValueReporter;
    bool _progressFinished = false;
    bool _showSuccess = false;
    bool _showProgress = false;
    string? _linkUrl;
    string? _openFolderPath;
    double _maximumWidth = 600;
    double _maximumHeight = 400;
    public PromptActionRole ActionRole { get; private set; }

    public string PromptText { get => _text; set { _text = value; OnSelfChanged(); } }
    public string PromptTitle { get => _title; set { _title = value; Title = value; OnSelfChanged(); } }
    public bool CanClose { get => _canClose; set { _canClose = value; OnSelfChanged(); OnPropertyChanged(nameof(CanOpenFolder)); } }
    public bool ProgressFinished { get => _progressFinished; set { _progressFinished = value; OnSelfChanged(); } }
    public string? LinkUrl { get => _linkUrl; set { _linkUrl = value; OnSelfChanged(); OnPropertyChanged(nameof(HasLink)); } }
    public string? OpenFolderPath { get => _openFolderPath; set { _openFolderPath = value; OnSelfChanged(); OnPropertyChanged(nameof(HasOpenFolder)); OnPropertyChanged(nameof(CanOpenFolder)); } }

    public bool HasLink => _linkUrl != null;
    public bool HasOpenFolder => _openFolderPath != null;

    // Hidden while an export is still running (CanClose is false during progress) so it can't
    // dismiss the dialog and orphan the background work.
    public bool CanOpenFolder => HasOpenFolder && CanClose;

    public bool PromptIsError => _type == PromptType.Error;
    public bool PromptIsSuccess => _type == PromptType.Success;
    public bool PromptIsInformation => _type == PromptType.Information;
    public bool PromptIsProgress => _type == PromptType.Progress;
    public bool PromptIsConfirm => _type == PromptType.Confirm;

    public bool Confirmed { get; private set; }

    public bool ShowProgressIcon { get => _showProgress; set { _showProgress = value; OnSelfChanged(); } }
    public bool ShowSuccessIcon { get => _showSuccess; set { _showSuccess = value; OnSelfChanged(); } }

    public Prompt()
    {
        DataContext = this;
        InitializeComponent();
        Opened += (_, _) =>
        {
            if (PromptIsError && Owner is Window owner && EditorSaveFeedback.ObserveErrorPopup(owner))
                PromptText += "\n\nChanges saved earlier in this operation remain saved.";
        };
    }

    public Prompt FitToContent(double maximumWidth, double maximumHeight)
    {
        _maximumWidth = maximumWidth;
        _maximumHeight = maximumHeight;
        SizeContent(maximumWidth, maximumHeight);
        return this;
    }

    private void SizeContent(double maximumWidth, double maximumHeight)
    {
        var limits = DialogLayout.Limits(this, maximumWidth, maximumHeight);
        var iconWidth = PromptIsConfirm ? 0 : 56;
        var body = new TextBlock { Text = PromptText, FontSize = FontSize, FontFamily = FontFamily };
        body.Measure(Size.Infinity);
        var title = new TextBlock { Text = PromptTitle, FontSize = FontSize, FontFamily = FontFamily, FontWeight = FontWeight.Bold };
        title.Measure(Size.Infinity);
        var actions = new TextBlock { Text = "Cancel   " + _confirmButton.Content, FontSize = FontSize, FontFamily = FontFamily };
        actions.Measure(Size.Infinity);
        // Reserve actual text space, icon/padding, and both actions; short prompts
        // shrink while long paths/reference lists use the scrollable height cap.
        Width = Math.Min(limits.Width, Math.Max(360,
            Math.Max(Math.Max(body.DesiredSize.Width, title.DesiredSize.Width) + iconWidth + 40,
                actions.DesiredSize.Width + 80)));
        var textWidth = Math.Max(1, Width - iconWidth - 40);
        body.TextWrapping = title.TextWrapping = TextWrapping.Wrap;
        body.Measure(new Size(textWidth, double.PositiveInfinity));
        title.Measure(new Size(textWidth, double.PositiveInfinity));
        MinWidth = MinHeight = 0;
        Height = Math.Min(limits.Height, Math.Max(150, body.DesiredSize.Height + title.DesiredSize.Height + 100));
    }

    protected override void OnOpened(EventArgs e)
    {
        // Owner/display limits are known here, before SimpleWindow's Opened fit.
        SizeContent(Math.Max(Width, _maximumWidth), Math.Max(Height, _maximumHeight));
        base.OnOpened(e);
        if (PromptIsConfirm) (ActionRole == PromptActionRole.Destructive ? _cancelButton : _confirmButton).Focus();
    }

    public Prompt(PromptType type, string title, string text = "", Progress<string?>? progress_reporter = null, Progress<double>? progress_value = null, string confirmButtonText = "Confirm", PromptActionRole actionRole = PromptActionRole.Neutral) : this()
    {
        _confirmButton.Content = confirmButtonText;
        ActionRole = actionRole;
        if (actionRole == PromptActionRole.Positive) _confirmButton.Classes.Add("positive");
        if (actionRole == PromptActionRole.Destructive) _confirmButton.Classes.Add("danger");
        _confirmButton.IsDefault = actionRole != PromptActionRole.Destructive;
        _cancelButton.IsDefault = actionRole == PromptActionRole.Destructive;
        _type = type;
        PromptText = text;
        PromptTitle = title;
        _progressReporter = progress_reporter;
        OnPropertyChanged(nameof(PromptIsError));
        OnPropertyChanged(nameof(PromptIsSuccess));
        OnPropertyChanged(nameof(PromptIsInformation));
        OnPropertyChanged(nameof(PromptIsProgress));
        OnPropertyChanged(nameof(PromptIsConfirm));

        if (type == PromptType.Success)
            ShowSuccessIcon = true;

        if (_progressReporter != null)
        {
            CanClose = false;
            ProgressFinished = false;
            ShowProgressIcon = true;
            _progressReporter.ProgressChanged += ProgressChanged;
        }

        if (progress_value != null)
        {
            _progressValueReporter = progress_value;
            _progressValueReporter.ProgressChanged += ProgressValueChanged;
            progressBar.IsIndeterminate = false;
            progressBar.Value = 0;
        }
        FitToContent(_maximumWidth, _maximumHeight);
    }

    void ProgressValueChanged(object? sender, double value)
    {
        if (_progressFinished) return;
        progressBar.Value = Math.Clamp(value, 0, 1) * 100;
    }

    void ProgressChanged(object? sender, string? text)
    {
        if (text == null)
        {
            // finished
            CanClose = true;
            ProgressFinished = true;
            ShowProgressIcon = false;
            ShowSuccessIcon = true;

            progressBar.Minimum = 0;
            progressBar.Maximum = 100;
            progressBar.Value = 100;
            progressBar.IsIndeterminate = false;
            return;
        }

        PromptText = text;
    }

    void CloseButtonClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!CanClose)
            return;

        Close();
    }

    void ConfirmButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Confirmed = true;
        Close();
    }

    void CancelButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Confirmed = false;
        Close();
    }

    void OpenLink_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_linkUrl == null) return;
        try { Process.Start(new ProcessStartInfo(_linkUrl) { UseShellExecute = true }); }
        catch { }
    }

    void OpenFolder_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!CanClose) return;
        if (_openFolderPath == null) return;
        var dir = Directory.Exists(_openFolderPath) ? _openFolderPath : Path.GetDirectoryName(_openFolderPath);
        if (dir == null) return;

        // I have to close first, if I do it after opening, our window gets focus
        Close();

        _ = Task.Run(() => Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true }));
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);

        if (_progressReporter != null)
        {
            _progressReporter.ProgressChanged -= ProgressChanged;
        }

        if (_progressValueReporter != null)
        {
            _progressValueReporter.ProgressChanged -= ProgressValueChanged;
        }
    }
}

public enum PromptActionRole { Neutral, Positive, Destructive }

public enum PromptType
{
    Information,
    Error,
    Success,
    Progress,
    Confirm
}
