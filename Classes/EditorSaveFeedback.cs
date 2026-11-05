using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace AoMDivineDataEditor.Classes;

internal enum EditorSaveOutcome { Saved, NewerEdits, Cancelled, Blocked, Failed, NoChanges, ReviewRequired }

/// <summary>Per-surface save feedback. Persistence and error details stay with the caller.</summary>
internal sealed class EditorSaveFeedback
{
    internal static readonly TimeSpan SuccessDuration = TimeSpan.FromSeconds(3);
    private static readonly ConditionalWeakTable<Window, EditorSaveFeedback> Windows = new();
    private readonly TextBlock _target;
    private readonly Action<TimeSpan, Action> _schedule;
    private readonly IBrush? _normalForeground;
    private long _generation;
    private bool _presenting;
    private EditorSaveOutcome? _visibleOutcome;
    private Attempt? _attempt;
    private Action<string>? _hostContext;
    private bool _protectContext;

    internal EditorSaveFeedback(TextBlock target, Action<TimeSpan, Action>? schedule = null)
    {
        _target = target;
        // Controls can be constructed before dynamic theme resources resolve.
        // Never cache that initial (black) fallback as the later status color.
        _normalForeground = Brush.Parse("#E8DECC");
        target.Foreground = _normalForeground;
        _schedule = schedule ?? ((delay, action) => DispatcherTimer.RunOnce(action, delay));
        target.TextWrapping = TextWrapping.NoWrap;
        target.TextTrimming = TextTrimming.CharacterEllipsis;
        target.PropertyChanged += (_, args) =>
        {
            if (!_presenting && args.Property == TextBlock.TextProperty)
            {
                ++_generation;
                _visibleOutcome = null;
                _protectContext = false;
                target.Foreground = _normalForeground;
                if (!string.IsNullOrWhiteSpace(target.Text) && target.Text != "Modified") _hostContext?.Invoke(target.Text);
            }
        };
    }

    internal void Register(Window owner)
    {
        Windows.Remove(owner);
        Windows.Add(owner, this);
    }

    internal void UseHostFooter(Action<string> reportContext)
    {
        _hostContext = reportContext;
        _target.IsVisible = false;
        if (!string.IsNullOrWhiteSpace(_target.Text) && _target.Text != "Modified") reportContext(_target.Text);
    }

    internal void ShowContextMessage(string text)
    {
        if (_attempt != null || _protectContext || string.IsNullOrWhiteSpace(text) || text == "Modified") return;
        var firstLine = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).First().Trim();
        var concise = firstLine.Length > 120 ? firstLine[..117] + "…" : firstLine;
        ToolTip.SetTip(_target, text);
        SetText(concise, _normalForeground, null);
    }

    // Context describes the current browser/category. Navigation must retire it
    // without erasing a Save outcome or interrupting an in-progress transaction.
    internal void ClearContextMessage()
    {
        if (_attempt == null && _visibleOutcome == null && !_protectContext)
            SetText("", _normalForeground, null);
    }

    // The host calls this only after an explicit discard leaves no pending edits.
    internal void ClearAfterDiscard()
    {
        if (_attempt == null) SetText("", _normalForeground, null);
    }

    private static EditorSaveFeedback? FindVisible(Window owner)
    {
        foreach (var child in owner.OwnedWindows.Where(window => window.IsVisible).Reverse())
            if (FindVisible(child) is { } nested) return nested;
        return Windows.TryGetValue(owner, out var feedback) ? feedback : null;
    }

    internal static void Publish(Window owner, EditorSaveOutcome outcome)
        => FindVisible(owner)?.Report(outcome);

    // Called only by an existing error popup when it opens. The popup retains
    // the detailed explanation; an active save attempt gets a concise outcome.
    internal static bool ObserveErrorPopup(Window owner)
    {
        var feedback = FindVisible(owner);
        if (feedback?._attempt is not { } attempt) return false;
        if (attempt.Stopped != EditorSaveOutcome.Failed)
            attempt.Stopped = EditorSaveOutcome.Blocked;
        feedback.PresentStopped(attempt);
        return attempt.Commits > 0;
    }

    internal void Report(EditorSaveOutcome outcome)
    {
        if (_attempt is { } attempt)
        {
            if (outcome is EditorSaveOutcome.Saved or EditorSaveOutcome.NewerEdits)
            {
                attempt.Commits++;
                attempt.NewerEdits |= outcome == EditorSaveOutcome.NewerEdits;
                return; // Never announce success halfway through global Save.
            }
            if (outcome != EditorSaveOutcome.Blocked || attempt.Stopped == null)
                attempt.Stopped = outcome;
            PresentStopped(attempt);
            return;
        }
        Present(outcome);
    }

    internal void ClearSuccessOnEdit()
    {
        if (_visibleOutcome is EditorSaveOutcome.Saved or EditorSaveOutcome.NoChanges)
            SetText("", _normalForeground, null);
    }

    internal Attempt BeginSave()
    {
        if (_attempt != null) return new Attempt(this, ownsAttempt: false);
        _attempt = new Attempt(this, ownsAttempt: true);
        SetText("Saving…", _normalForeground, null);
        return _attempt;
    }

    private void PresentStopped(Attempt attempt)
    {
        if (attempt.Commits > 0)
            SetText("Some changes saved; save not completed.", Brush.Parse("#C59A52"), null, protectContext: true);
        else
            Present(attempt.Stopped ?? EditorSaveOutcome.Blocked);
    }

    private void Present(EditorSaveOutcome outcome)
    {
        var text = outcome switch
        {
            EditorSaveOutcome.Saved => "Changes saved.",
            EditorSaveOutcome.NewerEdits => "Saved; newer edits remain unsaved.",
            EditorSaveOutcome.Cancelled => "Save cancelled.",
            EditorSaveOutcome.Blocked => "Save blocked.",
            EditorSaveOutcome.Failed => "Save failed.",
            EditorSaveOutcome.ReviewRequired => "Reloaded; review before saving.",
            _ => "No changes to save."
        };
        var color = outcome switch
        {
            EditorSaveOutcome.Saved => Brush.Parse("#78B88A"),
            EditorSaveOutcome.NewerEdits or EditorSaveOutcome.Blocked or EditorSaveOutcome.ReviewRequired => Brush.Parse("#C59A52"),
            EditorSaveOutcome.Failed => Brush.Parse("#E58B7D"),
            _ => _normalForeground
        };
        SetText(text, color, outcome);
        if (outcome is EditorSaveOutcome.Saved or EditorSaveOutcome.Cancelled or EditorSaveOutcome.NoChanges)
        {
            var generation = _generation;
            _schedule(SuccessDuration, () =>
            {
                if (_generation == generation) SetText("", _normalForeground, null);
            });
        }
    }

    private void SetText(string text, IBrush? foreground, EditorSaveOutcome? outcome, bool protectContext = false)
    {
        ++_generation;
        _visibleOutcome = outcome;
        _protectContext = protectContext || outcome is EditorSaveOutcome.Blocked or EditorSaveOutcome.Failed or EditorSaveOutcome.NewerEdits or EditorSaveOutcome.ReviewRequired;
        if (outcome != null || text.Length == 0) ToolTip.SetTip(_target, null);
        _presenting = true;
        try { _target.Foreground = foreground; _target.Text = text; }
        finally { _presenting = false; }
    }

    internal sealed class Attempt(EditorSaveFeedback feedback, bool ownsAttempt) : IDisposable
    {
        internal int Commits;
        internal bool NewerEdits;
        internal EditorSaveOutcome? Stopped;
        private bool _completed;
        private bool _disposed;

        internal void Complete(bool newerEdits = false)
        {
            if (!ownsAttempt || _completed || _disposed) return;
            _completed = true;
            feedback._attempt = null;
            feedback.Present(newerEdits || NewerEdits ? EditorSaveOutcome.NewerEdits
                : Commits > 0 ? EditorSaveOutcome.Saved : EditorSaveOutcome.NoChanges);
        }

        public void Dispose()
        {
            if (_disposed || !ownsAttempt) return;
            _disposed = true;
            if (!_completed)
            {
                feedback._attempt = null;
                feedback.PresentStopped(this);
            }
        }
    }
}
