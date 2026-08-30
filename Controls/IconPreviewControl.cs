using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using AoMDivineDataEditor.Classes;

namespace AoMDivineDataEditor.Controls;

/// <summary>A framed 128 x 128 icon preview.</summary>
public sealed class IconPreviewControl : Border
{
    public const double PropertyGridLeftOffset = 546;

    private readonly IconPreviewService? _service;
    private readonly Image _image;
    private readonly TextBlock _placeholder;
    private readonly Button _cycleButton;
    private IReadOnlyList<(string Path, string? Culture)> _options = [];
    private int _optionIndex;
    private Bitmap? _bitmap;
    private int _loadGeneration;

    public IconPreviewControl(IconPreviewService? service)
    {
        _service = service;
        Width = 132;
        Height = 132;
        BorderThickness = new Thickness(2);
        BorderBrush = Brush.Parse("#E2B84F");
        Background = Brush.Parse("#0B0E0D");

        _image = new Image
        {
            Width = 128,
            Height = 128,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        RenderOptions.SetBitmapInterpolationMode(_image, BitmapInterpolationMode.HighQuality);
        _placeholder = new TextBlock
        {
            Text = "No icon",
            Foreground = Brush.Parse("#8F918F"),
            FontSize = 11,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var content = new Grid { Width = 128, Height = 128 };
        content.Children.Add(_placeholder);
        content.Children.Add(_image);
        _cycleButton = new Button
        {
            Content = new TextBlock
            {
                Text = "»",
                FontSize = 18,
                FontWeight = FontWeight.Bold,
                Foreground = Brush.Parse("#F1D07A"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            },
            Width = 27,
            Height = 24,
            MinWidth = 0,
            MinHeight = 0,
            Padding = new Thickness(0),
            Margin = new Thickness(4),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = Brush.Parse("#E6090C0B"),
            BorderBrush = Brush.Parse("#E2B84F"),
            BorderThickness = new Thickness(1),
            Focusable = false,
            IsTabStop = false,
            IsVisible = false
        };
        _cycleButton.Click += async (_, _) =>
        {
            if (_options.Count < 2)
                return;
            _optionIndex = (_optionIndex + 1) % _options.Count;
            UpdateCycleButton();
            await ShowCurrentOptionAsync();
        };
        content.Children.Add(_cycleButton);
        Child = content;

        DetachedFromVisualTree += (_, _) =>
        {
            _loadGeneration++;
            ReplaceBitmap(null);
        };
        AttachedToVisualTree += (_, _) =>
        {
            if (_bitmap == null && _options.Count > 0)
                _ = ShowCurrentOptionAsync();
        };
    }

    public Task ShowAsync(string? iconPath)
        => ShowOptionsAsync(string.IsNullOrWhiteSpace(iconPath)
            ? []
            : [(iconPath, null)]);

    public Task ShowOptionsAsync(IEnumerable<(string Path, string? Culture)> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options
            .Where(option => !string.IsNullOrWhiteSpace(option.Path))
            .Select(option => (option.Path.Trim(), string.IsNullOrWhiteSpace(option.Culture) ? null : option.Culture.Trim()))
            .OrderBy(option => option.Item2 == null ? 0 : 1)
            .ToList();
        _optionIndex = 0;
        _cycleButton.IsVisible = _options.Count > 1;
        UpdateCycleButton();
        return ShowCurrentOptionAsync();
    }

    private async Task ShowCurrentOptionAsync()
    {
        var option = _options.Count == 0 ? default : _options[_optionIndex];
        var iconPath = option.Path;
        var generation = ++_loadGeneration;
        ReplaceBitmap(null);
        _placeholder.Text = string.IsNullOrWhiteSpace(iconPath) ? "No icon" : "Loading…";
        ToolTip.SetTip(this, string.IsNullOrWhiteSpace(iconPath) ? "No icon path" : iconPath);
        if (_service == null || string.IsNullOrWhiteSpace(iconPath))
            return;

        IconPreviewData? result;
        try
        {
            result = await _service.LoadAsync(iconPath);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (generation == _loadGeneration)
            {
                _placeholder.Text = "Invalid icon";
                ToolTip.SetTip(this, $"Icon could not be loaded: {iconPath}");
            }
            return;
        }
        if (generation != _loadGeneration)
            return;

        if (result == null)
        {
            _placeholder.Text = "Not found";
            ToolTip.SetTip(this, $"Icon could not be loaded: {iconPath}");
            return;
        }

        try
        {
            using var stream = new MemoryStream(result.PngBytes, writable: false);
            var bitmap = Bitmap.DecodeToWidth(stream, 128, BitmapInterpolationMode.HighQuality);
            if (generation != _loadGeneration)
            {
                bitmap.Dispose();
                return;
            }
            ReplaceBitmap(bitmap);
            _placeholder.Text = "";
            ToolTip.SetTip(this, $"{CultureLabel(option.Culture)}: {result.ResolvedSource}");
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _placeholder.Text = "Invalid icon";
            ToolTip.SetTip(this, $"Icon could not be decoded: {iconPath}");
        }
    }

    private void UpdateCycleButton()
    {
        if (_options.Count == 0)
        {
            ToolTip.SetTip(_cycleButton, null);
            return;
        }

        var current = CultureLabel(_options[_optionIndex].Culture);
        var next = CultureLabel(_options[(_optionIndex + 1) % _options.Count].Culture);
        ToolTip.SetTip(_cycleButton, $"Current icon: {current}. Click to show {next}.");
    }

    private static string CultureLabel(string? culture)
        => string.IsNullOrWhiteSpace(culture) ? "Default" : culture;

    private void ReplaceBitmap(Bitmap? bitmap)
    {
        _image.Source = bitmap;
        var previous = _bitmap;
        _bitmap = bitmap;
        previous?.Dispose();
        _placeholder.IsVisible = bitmap == null;
    }
}
