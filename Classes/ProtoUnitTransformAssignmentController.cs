using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace AoMDivineDataEditor.Classes;

public sealed class TransformCommandAssignmentState
{
    public string CommandName { get; set; } = "";
    public string SourceCommandName { get; set; } = "";
    public bool SourceIsBuiltIn { get; set; }
    public bool IsMultiple { get; set; }
    public string Row { get; set; } = "0";
    public string Column { get; set; } = "0";
    public string MergeMode { get; set; } = "";
    public ProtoUnitCommandDefinition? Definition { get; set; }
    public ProtoUnitTransformDefinition? TransformDefinition { get; set; }
    public Dictionary<string, string> StringTexts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool DefinitionDirty { get; set; }
    public string SnapshotCommandName { get; set; } = "";
    public XElement? LoadedCommandSnapshot { get; set; }
    public XElement? LoadedTransformSnapshot { get; set; }
}

/// <summary>
/// Owns the ProtoUnit-level Transform assignment collection and the pure assignment
/// serialization rules. UI rendering, dialogs and persistence stay in ProtoEditorWindow.
/// </summary>
public sealed class ProtoUnitTransformAssignmentController : IList<TransformCommandAssignmentState>
{
    private readonly List<TransformCommandAssignmentState> _items = [];

    public TransformCommandAssignmentState? UniqueAssignment =>
        _items.FirstOrDefault(state => !state.IsMultiple && !string.IsNullOrWhiteSpace(state.CommandName));

    public IReadOnlyList<ProtoCommandEntry> BuildCommandEntries(
        Func<string, bool> isUniqueCommand,
        Func<string, bool> isMultipleCommand)
        => _items
            .Where(state =>
                !string.IsNullOrWhiteSpace(state.CommandName) &&
                ((state.IsMultiple && isMultipleCommand(state.CommandName)) ||
                 (!state.IsMultiple && isUniqueCommand(state.CommandName))))
            .Select(state => new ProtoCommandEntry
            {
                Value = state.CommandName.Trim(),
                Row = state.Row,
                Column = state.Column,
                MergeMode = state.MergeMode
            })
            .ToList();

    public bool ContainsCommand(string commandName, TransformCommandAssignmentState? except = null)
        => _items.Any(state =>
            !ReferenceEquals(state, except) &&
            state.CommandName.Equals(commandName, StringComparison.OrdinalIgnoreCase));

    public TransformCommandAssignmentState this[int index] { get => _items[index]; set => _items[index] = value; }
    public int Count => _items.Count;
    public bool IsReadOnly => false;
    public void Add(TransformCommandAssignmentState item) => _items.Add(item);
    public void Clear() => _items.Clear();
    public bool Contains(TransformCommandAssignmentState item) => _items.Contains(item);
    public void CopyTo(TransformCommandAssignmentState[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
    public IEnumerator<TransformCommandAssignmentState> GetEnumerator() => _items.GetEnumerator();
    public int IndexOf(TransformCommandAssignmentState item) => _items.IndexOf(item);
    public void Insert(int index, TransformCommandAssignmentState item) => _items.Insert(index, item);
    public bool Remove(TransformCommandAssignmentState item) => _items.Remove(item);
    public void RemoveAt(int index) => _items.RemoveAt(index);
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
