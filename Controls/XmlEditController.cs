using AoMDivineDataEditor.Classes;
using Avalonia.Controls;
using Avalonia.Interactivity;
using AvaloniaEdit;

namespace AoMDivineDataEditor.Controls;

/// <summary>
/// Binds the selected record's session to an existing XML pane. Does not own
/// navigation, parsing, validation, dirty bookkeeping outside XML, or disk writes.
/// </summary>
internal sealed class XmlEditController : IDisposable
{
    private readonly TextEditor _editor;
    private readonly Button _editButton;
    private readonly Control? _form;
    private readonly Action _draftChanged;
    private XmlEditSession? _session;
    private bool _formEnabled;
    private bool _allowEditing;
    private bool _rendering;
    private bool _disposed;
    private string _renderedText = "";
    internal Func<Task<bool>>? BeforeEditAsync { get; set; }
    private bool _startingEdit;

    internal XmlEditController(TextEditor editor, Button editButton, Control? form, Action draftChanged)
    {
        _editor = editor;
        _editButton = editButton;
        _form = form;
        _draftChanged = draftChanged;
        _editor.TextChanged += TextChanged;
        _editButton.Click += EditClicked;
        Bind(null, formEnabled: false);
    }

    // The module owns stable record identity and retains sessions with open drafts.
    // Never create a new session merely because the user selected another record.
    internal void Bind(XmlEditSession? session, bool formEnabled, bool allowEditing = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_session != null) _session.Changed -= Render;
        _session = session;
        _formEnabled = formEnabled;
        _allowEditing = allowEditing;
        if (_session != null) _session.Changed += Render;
        Render();
    }

    private async void EditClicked(object? sender, RoutedEventArgs e)
    {
        if (_startingEdit || _session == null || !_allowEditing) return;
        var selected = _session;
        _startingEdit = true;
        try
        {
            if (BeforeEditAsync != null && !await BeforeEditAsync()) return;
            if (!_disposed && _allowEditing && ReferenceEquals(selected, _session) && selected.BeginEditing()) _editor.Focus();
        }
        finally { _startingEdit = false; }
    }

    private void TextChanged(object? sender, EventArgs e)
    {
        // Avalonia may deliver a programmatic TextChanged after Render returns.
        if (_disposed || _rendering || _editor.IsReadOnly ||
            string.Equals(_editor.Text, _renderedText, StringComparison.Ordinal)) return;
        if (_session?.Edit(_editor.Text ?? "") == true) _draftChanged();
    }

    private void Render()
    {
        _rendering = true;
        try
        {
            _renderedText = _session?.Text ?? "";
            if (!string.Equals(_editor.Text, _renderedText, StringComparison.Ordinal)) _editor.Text = _renderedText;
            _editor.IsEnabled = _editor.Focusable = _session != null;
            XmlSyntaxEditorService.SetReadOnly(_editor, !_allowEditing || _session?.IsEditing != true);
            _editButton.Content = "Edit XML";
            _editButton.IsVisible = _allowEditing && _session?.CanEdit == true;
            _editButton.IsEnabled = _allowEditing && _session?.CanEdit == true && !_session.IsEditing;
            ToolTip.SetTip(_editButton, _session?.IsEditing == true
                ? "Save to validate and finish XML editing."
                : "Edit this item's XML. Save validates and saves your changes.");
            // Pass only the form content, never its navigation/tabs/Save controls.
            // Preserve the module's existing read-only/unavailable form policy.
            if (_form != null) _form.IsEnabled = _formEnabled && _session?.IsEditing != true;
        }
        finally { _rendering = false; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_session != null) _session.Changed -= Render;
        _editor.TextChanged -= TextChanged;
        _editButton.Click -= EditClicked;
    }
}
