using System.Xml;
using Avalonia.Platform;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;

namespace AoMDivineDataEditor.Classes;

public static class XmlSyntaxEditorService
{
    private static readonly Lazy<IHighlightingDefinition> Highlighting = new(LoadHighlighting);

    public static void Configure(TextEditor editor)
    {
        ArgumentNullException.ThrowIfNull(editor);

        editor.SyntaxHighlighting = Highlighting.Value;
        editor.Options.ConvertTabsToSpaces = true;
        editor.Options.IndentationSize = 2;
        editor.Options.EnableHyperlinks = false;
        editor.Options.EnableEmailHyperlinks = false;
        editor.Options.AllowScrollBelowDocument = false;
        SetReadOnly(editor, editor.IsReadOnly);
    }

    public static void SetReadOnly(TextEditor editor, bool isReadOnly)
    {
        ArgumentNullException.ThrowIfNull(editor);

        editor.IsReadOnly = isReadOnly;
        editor.IsTabStop = !isReadOnly;
        editor.TextArea.Caret.CaretBrush = isReadOnly
            ? Brushes.Transparent
            : Brush.Parse("#E8DECC");
    }

    private static IHighlightingDefinition LoadHighlighting()
    {
        var uri = new Uri("avares://AoMDivineDataEditor/Assets/AoMXml.xshd");
        using var stream = AssetLoader.Open(uri);
        using var reader = XmlReader.Create(stream);
        return HighlightingLoader.Load(reader, HighlightingManager.Instance);
    }
}
