using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using AoMDivineDataEditor.Windows;
using AoMDivineDataEditor.Controls;

namespace AoMDivineDataEditor.Classes;

/// <summary>Shared close/keyboard policy; the owner retains validation and persistence.</summary>
internal sealed class CompactEditorLifecycle
{
    private readonly Window _owner;
    private readonly Func<bool> _hasChanges;
    private readonly Func<Task> _save;
    private bool _allowClose;
    private bool _committing;
    private bool _closed;
    internal bool IsClosePending { get; private set; }
    internal long CloseVersion { get; private set; }

    internal CompactEditorLifecycle(Window owner, Func<bool> hasChanges, Func<Task> save)
    {
        _owner = owner; _hasChanges = hasChanges; _save = save;
        owner.Closing += Closing;
        owner.Closed += (_, _) => _closed = true;
        owner.AddHandler(InputElement.KeyDownEvent, KeyDown, RoutingStrategies.Tunnel);
    }

    internal bool CanCommit(long version) => !_closed && !IsClosePending && version == CloseVersion;

    internal IDisposable BeginCommit()
    {
        _committing = true;
        var form = _owner.Content as Control;
        var enabled = form?.IsEnabled ?? false;
        if (form != null) form.IsEnabled = false;
        return new CommitScope(() =>
        {
            _committing = false;
            if (form != null) form.IsEnabled = enabled;
        });
    }

    private sealed class CommitScope(Action finish) : IDisposable
    {
        private Action? _finish = finish;
        public void Dispose() => Interlocked.Exchange(ref _finish, null)?.Invoke();
    }

    internal void CloseAfterSave(Action close)
    {
        _allowClose = true;
        close();
    }

    private async void Closing(object? sender, WindowClosingEventArgs args)
    {
        if (_allowClose) return;
        if (_committing || IsClosePending || _owner.OwnedWindows.Any(window => window.IsVisible))
        { args.Cancel = true; return; }
        CloseVersion++;
        if (!_hasChanges()) return;
        args.Cancel = true;
        IsClosePending = true;
        var focus = _owner.FocusManager?.GetFocusedElement() as Control;
        var prompt = new Prompt(PromptType.Confirm, "Unsaved changes",
            "Discard your unsaved changes and close this editor?", confirmButtonText: "Discard",
            actionRole: PromptActionRole.Destructive);
        prompt.FindControl<Button>("_cancelButton")!.Content = "Keep editing";
        try
        {
            await prompt.ShowDialog(_owner);
            if (_closed) return;
            if (prompt.Confirmed) CloseAfterSave(_owner.Close);
            else if (focus != null)
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var target = focus.IsEffectivelyVisible ? focus
                        : focus.GetLogicalAncestors().OfType<AssetPathEditor>().FirstOrDefault()?.CompactPresenter;
                    if (!_closed && target?.IsEffectivelyVisible == true && target.IsEffectivelyEnabled &&
                        ReferenceEquals(TopLevel.GetTopLevel(target), _owner)) target.Focus();
                }, DispatcherPriority.Loaded);
        }
        finally { IsClosePending = false; }
    }

    private async void KeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Key == Key.S && args.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            args.Handled = true;
            if (!_closed && !_committing && !IsClosePending && !_owner.OwnedWindows.Any(window => window.IsVisible))
                await _save();
        }
        else if (args.Key == Key.Escape)
        {
            args.Handled = true;
            if (_closed || IsClosePending || _owner.OwnedWindows.Any(window => window.IsVisible)) return;
            foreach (var control in _owner.GetLogicalDescendants().OfType<Control>())
            {
                if (control is AutoCompleteBox { IsDropDownOpen: true } auto) { auto.IsDropDownOpen = false; return; }
                if (control is ComboBox { IsDropDownOpen: true } combo) { combo.IsDropDownOpen = false; return; }
            }
            _owner.Close();
        }
    }
}
