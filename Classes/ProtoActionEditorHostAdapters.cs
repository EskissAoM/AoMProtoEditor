using System.Xml.Linq;

namespace AoMDivineDataEditor.Classes;

internal interface IProtoActionEditorHostAdapter
{
    ProtoActionEditorHostContext Context { get; }
    List<ProtoAction> LoadActions(XElement backingUnit);
    string ResolveActionType(
        string actionName,
        string? editorType,
        Func<string, string?> resolveUnitActionType,
        Func<string?, string> resolveExactType);
    ProtoAction? FindInheritedAction(string actionName, Func<string, ProtoAction?> findTacticsAction);
    bool ShouldCollapseTacticsOnlyOverlay(bool isTacticsOnlyAction);
    void WriteActions(XElement target, IEnumerable<ProtoAction> actions);
}

internal sealed class ProtoUnitActionEditorHostAdapter : IProtoActionEditorHostAdapter
{
    public ProtoActionEditorHostContext Context => ProtoActionEditorHostContext.ProtoUnit;

    public List<ProtoAction> LoadActions(XElement backingUnit)
        => ProtoXmlHandler.GetProtoActions(backingUnit);

    public string ResolveActionType(
        string actionName,
        string? editorType,
        Func<string, string?> resolveUnitActionType,
        Func<string?, string> resolveExactType)
        => resolveUnitActionType(actionName) ?? resolveExactType(editorType);

    public ProtoAction? FindInheritedAction(string actionName, Func<string, ProtoAction?> findTacticsAction)
        => findTacticsAction(actionName);

    public bool ShouldCollapseTacticsOnlyOverlay(bool isTacticsOnlyAction)
        => isTacticsOnlyAction;

    public void WriteActions(XElement target, IEnumerable<ProtoAction> actions)
        => ProtoXmlHandler.SetProtoActions(target, actions);
}

internal sealed class TacticsActionEditorHostAdapter(
    Func<List<ProtoAction>> loadActions) : IProtoActionEditorHostAdapter
{
    public ProtoActionEditorHostContext Context => ProtoActionEditorHostContext.TacticsDocument;

    public List<ProtoAction> LoadActions(XElement backingUnit)
        => loadActions();

    public string ResolveActionType(
        string actionName,
        string? editorType,
        Func<string, string?> resolveUnitActionType,
        Func<string?, string> resolveExactType)
        => resolveExactType(editorType);

    public ProtoAction? FindInheritedAction(string actionName, Func<string, ProtoAction?> findTacticsAction)
        => null;

    public bool ShouldCollapseTacticsOnlyOverlay(bool isTacticsOnlyAction)
        => false;

    public void WriteActions(XElement target, IEnumerable<ProtoAction> actions)
        => ProtoXmlHandler.SetProtoActions(target, actions);
}
