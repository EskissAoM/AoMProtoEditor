namespace AoMDivineDataEditor.Classes;

internal enum ProtoActionEditorHostKind
{
    ProtoUnit,
    TacticsDocument
}

/// <summary>
/// Declares which host owns the shared ProtoAction editor. This keeps host-only
/// behavior out of field renderers and makes the ProtoUnit/tactics boundary explicit.
/// </summary>
internal sealed record ProtoActionEditorHostContext(ProtoActionEditorHostKind Kind)
{
    public static ProtoActionEditorHostContext ProtoUnit { get; }
        = new(ProtoActionEditorHostKind.ProtoUnit);

    public static ProtoActionEditorHostContext TacticsDocument { get; }
        = new(ProtoActionEditorHostKind.TacticsDocument);

    public bool IsTacticsDocument => Kind == ProtoActionEditorHostKind.TacticsDocument;
    public bool UsesTacticsInheritance => Kind == ProtoActionEditorHostKind.ProtoUnit;
    public bool EnforcesProtoUnitOnlyConstraints => Kind == ProtoActionEditorHostKind.ProtoUnit;
    public bool ShowsProtoUnitChrome => Kind == ProtoActionEditorHostKind.ProtoUnit;
    public bool TracksProtoUnitDraft => Kind == ProtoActionEditorHostKind.ProtoUnit;
    public bool UsesStandaloneDocumentLifecycle => Kind == ProtoActionEditorHostKind.TacticsDocument;
    public bool ShowsTacticsDefinitionEditor => Kind == ProtoActionEditorHostKind.TacticsDocument;
    public bool AllowsStandaloneActionCreation => Kind == ProtoActionEditorHostKind.TacticsDocument;
    public bool OwnsCompleteActionSequence => Kind == ProtoActionEditorHostKind.TacticsDocument;
}
