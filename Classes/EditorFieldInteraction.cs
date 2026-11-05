using System.Globalization;
using System.Runtime.CompilerServices;
using AoMDivineDataEditor.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AoMDivineDataEditor.Classes;

/// <summary>Opt-in field presentation. Catalogs, validation and commits remain with the owner.</summary>
public static class EditorFieldInteraction
{
    public const double SuggestionMinimumWidth = 320;
    private static readonly ConditionalWeakTable<Control, State> States = new();
    private sealed class InputRevision { public int Value; }
    private static readonly ConditionalWeakTable<TopLevel, InputRevision> RootInputs = new();
    private static int Revision(TopLevel? top)
        => top == null ? -1 : RootInputs.GetValue(top, root =>
        {
            var revision = new InputRevision();
            root.AddHandler(InputElement.PointerPressedEvent, (_, _) => revision.Value++, RoutingStrategies.Tunnel, handledEventsToo: true);
            root.AddHandler(InputElement.KeyDownEvent, (_, _) => revision.Value++, RoutingStrategies.Tunnel, handledEventsToo: true);
            root.AddHandler(InputElement.TextInputEvent, (_, _) => revision.Value++, RoutingStrategies.Tunnel, handledEventsToo: true);
            return revision;
        }).Value;

    public static void Configure(Control owner, EditorFieldWidthRole role, Func<bool>? isBusy = null)
    {
        if (owner is not (TextBox or AutoCompleteBox or ComboBox or AssetPathEditor)) return;
        var state = States.GetValue(owner, control => new State(control));
        state.Role = role;
        state.FullBehavior = true;
        if (isBusy != null) state.IsBusy = isBusy;
        state.EnableTip();
        state.RefreshTemplate();
        state.QueueStart();
        if (owner is AutoCompleteBox box)
            EditorAutoCompleteService.EnableDropdown(box, state.IsBusy, selectAllOnFirstClick: false);
        if (owner is AssetPathEditor asset)
            Configure(asset.Editor, EditorFieldWidthRole.AssetPath, state.IsBusy);
    }

    public static void ConfigureStartVisibility(Control owner)
    {
        var state = States.GetValue(owner, control => new State(control));
        if (owner is AutoCompleteBox box) state.IsBusy = EditorAutoCompleteService.GetBusyPredicate(box);
        state.RefreshTemplate();
        state.QueueStart();
    }

    internal static bool IsConfigured(Control owner) => States.TryGetValue(owner, out var state) && state.FullBehavior;
    internal static void UpdateBusy(Control owner, Func<bool>? isBusy)
    {
        if (isBusy != null && States.TryGetValue(owner, out var state)) state.IsBusy = isBusy;
    }
    internal static bool IsInputSource(Control owner, object? source)
        => States.TryGetValue(owner, out var state) && state.IsInputSource(source);
    internal static void ShowStart(Control owner, bool allowFocused = false)
    {
        if (States.TryGetValue(owner, out var state)) state.QueueStart(allowFocused);
    }
    internal static void Invalidate(Control owner)
    {
        if (States.TryGetValue(owner, out var state)) state.Invalidate();
    }
    internal static void SelectAllOnEntry(Control owner)
    {
        if (States.TryGetValue(owner, out var state)) state.QueueSelectAll(entering: true);
    }

    private sealed class State
    {
        private readonly Control _owner;
        private readonly TextBlock _valueTip = new() { TextWrapping = TextWrapping.Wrap };
        private readonly ContentPresenter _helpTip = new();
        private readonly ScrollViewer _tip;
        private object? _help;
        private Popup? _popup;
        private int _version;
        private int _interaction;
        private int _startTicket;
        private bool _pendingSelect;
        private Point _pressedAt;
        public EditorFieldWidthRole Role;
        public bool FullBehavior;
        public Func<bool>? IsBusy;

        public State(Control owner)
        {
            _owner = owner;
            _tip = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new StackPanel { Spacing = 8, Children = { _valueTip, _helpTip } }
            };
            ToolTip.AddToolTipOpeningHandler(owner, OnTipOpening);
            owner.PointerEntered += (_, _) => { if (FullBehavior) EnsureTip(); };
            owner.AttachedToVisualTree += (_, _) => { RefreshTemplate(); QueueStart(); };
            owner.DetachedFromVisualTree += (_, _) => Invalidate();
            owner.SizeChanged += (_, _) => SizePopup();
            owner.LostFocus += (_, _) => QueueStart();
            owner.PropertyChanged += (_, args) =>
            {
                if (args.Property == TextBox.TextProperty || args.Property == AutoCompleteBox.TextProperty ||
                    args.Property == ComboBox.TextProperty)
                {
                    _version++;
                    if (!_owner.IsKeyboardFocusWithin || Busy()) QueueStart(allowFocused: Busy());
                }
            };
            if (owner is TemplatedControl templated)
                templated.TemplateApplied += (_, _) => { RefreshTemplate(); QueueStart(); };
            if (owner is AutoCompleteBox box)
                box.DropDownOpening += (_, _) => { RefreshTemplate(); SizePopup(); };
            if (owner is ComboBox combo)
            {
                combo.DropDownOpened += (_, _) => { RefreshTemplate(); SizePopup(); };
                combo.SelectionChanged += (_, _) => QueueStart(allowFocused: true);
            }
            if (owner is AssetPathEditor asset)
                asset.FullValueChanged += (_, _) => { _version++; QueueStart(); };
            owner.AddHandler(InputElement.PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
            owner.AddHandler(InputElement.PointerMovedEvent, OnMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
            owner.AddHandler(InputElement.PointerReleasedEvent, OnReleased, RoutingStrategies.Bubble, handledEventsToo: true);
            owner.AddHandler(InputElement.KeyDownEvent, (_, _) => { _interaction++; _pendingSelect = false; }, RoutingStrategies.Tunnel);
            owner.AddHandler(InputElement.TextInputEvent, (_, _) => { _interaction++; _startTicket++; _pendingSelect = false; }, RoutingStrategies.Tunnel);
        }

        private bool Busy() => IsBusy?.Invoke() == true;
        public void EnableTip()
        {
            EnsureTip();
            ToolTip.SetShowOnDisabled(_owner, true);
        }
        private TextBox? Input() => _owner switch
        {
            TextBox text => text,
            AssetPathEditor asset => asset.CompactPresenter.IsVisible ? asset.CompactPresenter : null,
            ComboBox { IsEditable: false } => null,
            _ => _owner.GetVisualDescendants().OfType<TextBox>().FirstOrDefault()
        };
        private string Value() => _owner switch
        {
            AssetPathEditor asset => asset.FullValue,
            TextBox text => text.Text ?? "",
            AutoCompleteBox box => box.Text ?? "",
            ComboBox combo => combo.IsEditable ? combo.Text ?? "" : combo.SelectedItem?.ToString() ?? "",
            _ => ""
        };
        private bool OrdinaryInput(TextBox input)
            => Role != EditorFieldWidthRole.ShortRollover && !input.AcceptsReturn &&
               input.TextWrapping == TextWrapping.NoWrap && input.PasswordChar == default;

        public bool IsInputSource(object? source)
        {
            var input = Input();
            return input != null && source is Visual visual &&
                   (ReferenceEquals(input, visual) || visual.GetVisualAncestors().Any(parent => ReferenceEquals(parent, input)));
        }

        public void Invalidate()
        {
            _version++; _interaction++; _startTicket++; _pendingSelect = false;
        }

        public void RefreshTemplate()
        {
            if (_owner is not TemplatedControl control) return;
            var popup = control.GetTemplateDescendants().OfType<Popup>().FirstOrDefault();
            if (ReferenceEquals(popup, _popup)) return;
            if (_popup != null) _popup.Opened -= PopupOpened;
            _popup = popup;
            if (_popup != null) _popup.Opened += PopupOpened;
            SizePopup();
        }

        private void PopupOpened(object? sender, EventArgs args) => SizePopup();
        private Size AvailableSize()
        {
            var top = TopLevel.GetTopLevel(_owner);
            if (top == null) return new Size(640, 480);
            var screen = top.Screens?.ScreenFromTopLevel(top);
            var width = top.ClientSize.Width;
            var height = top.ClientSize.Height;
            if (screen != null)
            {
                width = Math.Min(width, screen.WorkingArea.Width / screen.Scaling);
                height = Math.Min(height, screen.WorkingArea.Height / screen.Scaling);
            }
            return new Size(Math.Max(1, width - 16), Math.Max(1, height - 16));
        }

        private void SizePopup()
        {
            if (!FullBehavior || _popup?.Child is not Control child || TopLevel.GetTopLevel(_owner) == null) return;
            var available = AvailableSize().Width;
            // AutoCompleteBox owns Popup.MinWidth; constrain its content too.
            _popup.MinWidth = Math.Min(_owner.Bounds.Width, available);
            _popup.MaxWidth = available;
            if (_owner is ComboBox or AutoCompleteBox && Role is EditorFieldWidthRole.ShortEnum or EditorFieldWidthRole.LongEnum or EditorFieldWidthRole.ComparisonOperator)
            {
                // Closed choices need only their content width. Reference/search
                // catalogs retain the wider suggestion policy below.
                var choices = _owner is ComboBox combo ? combo.Items.Cast<object>() :
                    ((AutoCompleteBox)_owner).ItemsSource?.Cast<object>() ?? [];
                var contentWidth = choices.Select(item => new FormattedText(
                    item?.ToString() ?? "", CultureInfo.InvariantCulture, _owner.FlowDirection,
                    new Typeface(_owner.GetValue(TextBlock.FontFamilyProperty), _owner.GetValue(TextBlock.FontStyleProperty),
                        _owner.GetValue(TextBlock.FontWeightProperty), _owner.GetValue(TextBlock.FontStretchProperty)),
                    _owner.GetValue(TextBlock.FontSizeProperty), Brushes.White).WidthIncludingTrailingWhitespace + 48).DefaultIfEmpty(0).Max();
                var width = Math.Min(available, Math.Max(_owner.Bounds.Width, Math.Min(SuggestionMinimumWidth, contentWidth)));
                _popup.MaxWidth = width;
                child.MinWidth = width;
                child.MaxWidth = width;
                return;
            }
            child.MinWidth = Math.Min(SuggestionMinimumWidth, available);
            child.MaxWidth = available;
        }

        private void OnPressed(object? sender, PointerPressedEventArgs args)
        {
            _interaction++;
            _pendingSelect = false;
            if (!FullBehavior) return;
            var input = Input();
            if (input == null || !OrdinaryInput(input) || input.IsReadOnly || !input.IsEnabled ||
                Busy() || !IsInputSource(args.Source) || args.ClickCount != 1 ||
                !args.GetCurrentPoint(input).Properties.IsLeftButtonPressed) return;
            _pendingSelect = !input.IsKeyboardFocusWithin && !string.IsNullOrEmpty(input.Text);
            _pressedAt = args.GetPosition(_owner);
            if (_owner is ComboBox { IsEditable: true } combo && string.IsNullOrWhiteSpace(input.Text))
            {
                var version = _version; var interaction = _interaction;
                var top = TopLevel.GetTopLevel(_owner); var revision = Revision(top); var value = Value();
                Dispatcher.UIThread.Post(() =>
                {
                    if (!Valid(top, version, interaction, value) || revision != Revision(top) || Busy() ||
                        !_owner.IsEnabled || !_owner.IsEffectivelyVisible || !_owner.IsKeyboardFocusWithin) return;
                    combo.IsDropDownOpen = true;
                }, DispatcherPriority.Background);
            }
        }

        private void OnMoved(object? sender, PointerEventArgs args)
        {
            var point = args.GetPosition(_owner);
            if (_pendingSelect && (Math.Abs(point.X - _pressedAt.X) > 4 || Math.Abs(point.Y - _pressedAt.Y) > 4))
            {
                _pendingSelect = false;
                _interaction++;
            }
        }

        private void OnReleased(object? sender, PointerReleasedEventArgs args)
        {
            if (!_pendingSelect) return;
            _pendingSelect = false;
            if (args.InitialPressMouseButton == MouseButton.Left && IsInputSource(args.Source)) QueueSelectAll();
        }

        public void QueueSelectAll(bool entering = false)
        {
            var version = _version; var interaction = _interaction;
            var top = TopLevel.GetTopLevel(_owner);
            var rootInput = Revision(top);
            var value = Value();
            Dispatcher.UIThread.Post(() =>
            {
                var input = Input();
                if (input == null || !Valid(top, version, interaction, value) || Busy() || !input.IsEnabled ||
                    rootInput != Revision(top) || !_owner.IsEffectivelyVisible ||
                    !OrdinaryInput(input) || input.IsReadOnly ||
                    (!entering && !input.IsKeyboardFocusWithin)) return;
                if (entering) input.Focus();
                input.CaretIndex = 0;
                input.SelectionStart = 0;
                input.SelectionEnd = input.Text?.Length ?? 0;
                ResetOffset(input);
            }, DispatcherPriority.Background);
        }

        public void QueueStart(bool allowFocused = false)
        {
            var ticket = ++_startTicket;
            var version = _version; var interaction = _interaction;
            var top = TopLevel.GetTopLevel(_owner);
            var rootInput = Revision(top);
            var value = Value();
            Dispatcher.UIThread.Post(() =>
            {
                if (ticket != _startTicket || !Valid(top, version, interaction, value) || Busy() ||
                    !_owner.IsEnabled || !_owner.IsEffectivelyVisible ||
                    rootInput != Revision(top) || (!allowFocused && _owner.IsKeyboardFocusWithin)) return;
                var input = Input();
                if (input == null || !OrdinaryInput(input)) return;
                // A catalog can select an exact match while TextInput is replacing
                // Select All. Presentation must never move the editing caret.
                if (!_owner.IsKeyboardFocusWithin && input.SelectionStart == input.SelectionEnd) input.CaretIndex = 0;
                ResetOffset(input);
            }, DispatcherPriority.Background);
        }

        private bool Valid(TopLevel? top, int version, int interaction, string value)
            => top != null && ReferenceEquals(top, TopLevel.GetTopLevel(_owner)) &&
               version == _version && interaction == _interaction && value == Value();
        private static void ResetOffset(TextBox input)
        {
            var scroll = input.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            if (scroll != null) scroll.Offset = new Vector(0, scroll.Offset.Y);
        }

        private void EnsureTip()
        {
            var tip = ToolTip.GetTip(_owner);
            if (ReferenceEquals(tip, _tip)) return;
            _help = _owner is AssetPathEditor asset && Equals(tip, asset.FullValue) ? null : tip;
            // Replace the old presenter before parenting any existing visual help.
            ToolTip.SetTip(_owner, _tip);
            _helpTip.Content = _help;
        }

        private void OnTipOpening(object? sender, CancelRoutedEventArgs args)
        {
            if (!FullBehavior || !ReferenceEquals(args.Source, _owner)) return;
            EnsureTip();
            var value = Value();
            var input = Input();
            var shown = input?.Text ?? value;
            var clipped = _owner is AssetPathEditor && shown != value || IsClipped(input, shown);
            _valueTip.Text = value;
            _valueTip.IsVisible = clipped && value.Length > 0;
            _helpTip.IsVisible = _help != null;
            var available = AvailableSize();
            _tip.MaxWidth = Math.Min(400, available.Width);
            _tip.MaxHeight = Math.Min(300, available.Height);
            if (!_valueTip.IsVisible && _help == null) { args.Cancel = true; args.Handled = true; }
        }

        private bool IsClipped(TextBox? input, string shown)
        {
            if (shown.Length == 0 || input is { PasswordChar: not '\0' } ||
                input is { AcceptsReturn: true } || input is { TextWrapping: not TextWrapping.NoWrap }) return false;
            if (input != null)
            {
                var presenter = input.GetVisualDescendants().OfType<TextPresenter>().FirstOrDefault();
                var scroll = input.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
                return presenter != null && scroll != null && scroll.Viewport.Width > 0 &&
                       presenter.TextLayout.WidthIncludingTrailingWhitespace > scroll.Viewport.Width + .5;
            }
            var block = _owner.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(text => text.Text == shown);
            if (block == null || block.Bounds.Width <= 0) return false;
            var measured = new FormattedText(shown, CultureInfo.InvariantCulture, block.FlowDirection,
                new Typeface(block.FontFamily, block.FontStyle, block.FontWeight, block.FontStretch), block.FontSize, Brushes.White);
            return measured.WidthIncludingTrailingWhitespace > block.Bounds.Width + .5;
        }
    }
}
