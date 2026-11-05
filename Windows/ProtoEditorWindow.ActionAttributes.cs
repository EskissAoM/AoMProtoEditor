using AoMDivineDataEditor.Classes;

namespace AoMDivineDataEditor.Windows;

public partial class ProtoEditorWindow
{
    // These scalar fields also have a dedicated editor in ranged mode. The full
    // attribute list may expose them in another mode without changing that mode.
    private static readonly string[] AdvancedAttackScalarTags = ["accuracy", "trackrating", "maxspread", "spreadfactor"];

    private bool UsesAdvancedAttackScalarEditor(ProtoActionWidgetState state, string actionType, string tag)
        => IsAttackActionType(actionType) && AdvancedAttackScalarTags.Contains(tag, StringComparer.OrdinalIgnoreCase) &&
           !GetAttackMode(state).Equals("Ranged Attack", StringComparison.OrdinalIgnoreCase);

    // Reuse the same ownership predicates as the generic scalar/list renderers.
    // The picker must not offer a second copy of a specialized field or Add UI.
    private static bool IsDedicatedProtoActionAttribute(string actionType, string tag)
        => IsManagedCoreMinRangeFieldTag(actionType, tag) || IsManagedModelAttachmentFieldTag(actionType, tag) ||
           IsManagedAbductDamageAreaFieldTag(actionType, tag) || IsManagedAutoBoostFieldTag(actionType, tag) ||
           IsManagedAutoBoostStructuredFieldTag(actionType, tag) || IsManagedAutoRangedModifyFieldTag(actionType, tag) ||
           IsManagedAutoRangedModifyStructuredFieldTag(actionType, tag) || IsManagedAssistAttackFieldTag(actionType, tag) ||
           IsManagedAttachingFieldTag(actionType, tag) || IsManagedJumpAttackFieldTag(actionType, tag) ||
           IsManagedChainAttackFieldTag(actionType, tag) || IsManagedAoeAttackFieldTag(actionType, tag) ||
           IsManagedLinearAreaAttackFieldTag(actionType, tag) || IsManagedAbductFieldTag(actionType, tag) ||
           IsManagedMaulFieldTag(actionType, tag) || IsManagedTeleportAttackFieldTag(actionType, tag) ||
           IsManagedThrowFieldTag(actionType, tag) || IsManagedStackControlFieldTag(actionType, tag) ||
           IsManagedChargedFieldTag(actionType, tag) || IsManagedBolsterStructuredFieldTag(actionType, tag) ||
           IsActionCardManagedOptionalFieldTag(actionType, tag) || IsManagedSelfModifyFieldTag(actionType, tag) ||
           IsManagedLikeBonusFieldTag(actionType, tag) || IsManagedReleaseSkyLanternFieldTag(actionType, tag) ||
           IsManagedHealFieldTag(actionType, tag) || IsManagedHealStructuredFieldTag(actionType, tag) ||
           IsManagedConditionalTransformFieldTag(actionType, tag) || IsManagedConditionalTransformStructuredFieldTag(actionType, tag) ||
           IsManagedDelayedTransformFieldTag(actionType, tag) || IsManagedDelayedTransformStructuredFieldTag(actionType, tag) ||
           IsManagedMaintainFieldTag(actionType, tag) || IsManagedMaintainStructuredFieldTag(actionType, tag) ||
           IsManagedAutoGatherFieldTag(actionType, tag) || IsManagedAutoGatherStructuredFieldTag(actionType, tag) ||
           IsManagedConvertFieldTag(actionType, tag) || IsManagedConvertStructuredFieldTag(actionType, tag) ||
           IsManagedSpawnFieldTag(actionType, tag) || IsManagedSpawnStructuredFieldTag(actionType, tag) ||
           IsManagedTradeFieldTag(actionType, tag) || IsManagedTradeStructuredFieldTag(actionType, tag) ||
           IsManagedStealthFieldTag(actionType, tag) || IsManagedAreaRestrictFieldTag(actionType, tag) ||
           IsManagedAreaRestrictStructuredFieldTag(actionType, tag) || IsManagedLureFieldTag(actionType, tag) ||
           IsManagedLureStructuredFieldTag(actionType, tag) || IsManagedIdleStatBonusFieldTag(actionType, tag) ||
           IsManagedDevoteMinorFieldTag(actionType, tag) || IsManagedDevoteMinorStructuredFieldTag(actionType, tag) ||
           IsManagedDistanceModifyFieldTag(actionType, tag) || IsManagedDistanceModifyStructuredFieldTag(actionType, tag) ||
           IsManagedDrainResurrectionStructuredFieldTag(actionType, tag) || IsAutoConvertManagedStructuredFieldTag(actionType, tag);
}
