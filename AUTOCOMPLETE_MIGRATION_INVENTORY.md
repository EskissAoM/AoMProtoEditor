# Autocomplete migration inventory

Status: proposal only. No migration should start until this document is reviewed.

## Scope and coverage

- 123 current `AutoCompleteBox` construction sites are in scope.
- 119 are in `Windows/ProtoEditorWindow.axaml.cs`.
- One each is in `Windows/ProtoUnitCommandEditorWindow.cs`, `Controls/ProtoUnitCommandTransformEditor.cs`, and `Classes/ProtoUnitCommandFieldFactory.cs`.
- `Controls/AssetPathEditor.cs` contains one additional target-typed `AutoCompleteBox` (`new()`), which is why a simple `new AutoCompleteBox` search returns 122 rather than 123.
- All 123 currently use `AutoCompleteFilterMode.Contains`.
- Existing true `ComboBox` fields are not part of the construction-site count. Autocomplete fields explicitly assigned `STRICT_COMBOBOX` or `STRICT_SEARCHABLE_COMBOBOX` below are planned conversions and remain in scope.
- A construction site may represent several logical fields. Those dynamic families are explicitly listed rather than pretending they have one uniform validation rule.

## Proposed shared option model

Every migrated field should explicitly choose these options:

| Option | Values | Meaning |
|---|---|---|
| `ValuePolicy` | `Strict`, `Suggested`, `CustomName`, `AssetPath` | Whether arbitrary text is accepted and how it is stored. |
| `AllowEmpty` | `true`, `false` | Whether the committed value may be empty. |
| `PreserveUnknownLoadedValue` | `true`, `false`, `read-only only` | Whether an existing value absent from current suggestions survives. |
| `CommitMode` | `OnSelection`, `OnTextChanged`, `OnLostFocus`, `AddChipOnSelection` | When the model is updated. Multiple modes may apply. |
| `ExcludeSelectedValues` | `true`, `false` | Removes values already represented by chips/rows. |
| `SelectAllOnFirstClick` | `true`, `false` | Whether the current text is selected on initial pointer focus. |
| `HostWheelPolicy` | `FreezeHostKeepPopup`, `Normal` | While open, editor-tree wheel events are consumed; popup-tree wheel events scroll the popup normally. |
| `Width` | pixels or `Standard` | Explicit layout width. |
| `DisplayAdapter` | optional | Converts stored values to display values and back. |
| `OptionSource` | named provider | Defines the authoritative closed set or suggestion source. |

## Reusable profiles

These names are referenced throughout the inventory. Edit these defaults before changing individual rows.

| Profile | Value policy | Empty | Unknown loaded | Commit | Exclude selected | Select all | Host wheel | Default width |
|---|---|---:|---|---|---:|---:|---|---|
| `STRICT_OPTIONAL` | Strict | true | read-only only | Selection + lost focus | false | false | FreezeHostKeepPopup | Standard |
| `STRICT_REQUIRED` | Strict | false | read-only only | Selection + lost focus | false | false | FreezeHostKeepPopup | Standard |
| `STRICT_DISPLAY_VALUE` | Strict | true | read-only only | Selection + lost focus | false | false | FreezeHostKeepPopup | Standard |
| `SUGGESTED_OPTIONAL` | Suggested | true | true | Text changed + selection | false | false | FreezeHostKeepPopup | Standard |
| `CUSTOM_NAME_REQUIRED` | CustomName | false | true | Text changed + lost focus | false | false | FreezeHostKeepPopup | Standard |
| `CUSTOM_NAME_OPTIONAL` | CustomName | true | true | Text changed + lost focus | false | false | FreezeHostKeepPopup | Standard |
| `CHIP_STRICT` | Strict | true | false | Add chip on selection | true | false | FreezeHostKeepPopup | 150 |
| `ASSET_PATH` | AssetPath | true | true | Text changed + selection | false | true | FreezeHostKeepPopup | Standard |
| `STRICT_COMBOBOX` | Strict | true | read-only only | Selection | false | false | Normal | Standard |
| `STRICT_SEARCHABLE_COMBOBOX` | Strict | true | read-only only | Selection + validated lost focus | false | false | FreezeHostKeepPopup | Standard |

`STRICT_SEARCHABLE_COMBOBOX` is a true Avalonia `ComboBox` with an editable search surface. Typing filters the closed option set, but text is never itself a valid committed value: the user must select an exact option, otherwise the edit is cleared or restored to the previous selection. It should be implemented once as shared behavior rather than independently per field. `STRICT_COMBOBOX` uses the same closed-set semantics without a filter field and is suitable for very short lists.

Global expectations for every profile except where explicitly overridden:

- An empty field opens with every available suggestion.
- Typing filters with case-insensitive `Contains` (`camel` can show `CamelRider`).
- Scrolling inside the popup scrolls suggestions and keeps the popup open.
- Scrolling over the host editor while the popup is open does not move the editor and does not close the popup.
- Event handlers are detached when the field leaves the visual tree.

Additional expectations for `STRICT_SEARCHABLE_COMBOBOX`:

- Opening an empty control shows the complete closed option set.
- The editable text is a filter only; it cannot introduce a custom XML value.
- Clicking the arrow continues to expose the complete or currently filtered list.
- The selected item, rather than unvalidated text, is the value written to XML.

## 1. Tactics and command-row fields

| Source site | Logical field(s) | Proposed profile | Overrides / review notes |
|---|---|---|---|
| `ProtoEditorWindow.axaml.cs:1174` `CreateBasicTacticAutoComplete` | Basic tactics action names and other basic-tactic suggestion fields | `SUGGESTED_OPTIONAL` | Some call sites may need `STRICT_OPTIONAL`; split by caller during migration. |
| `ProtoEditorWindow.axaml.cs:4904` `AddCommandRow/valueAcb` | Tactics command-row value | `STRICT_OPTIONAL` | Width remains row-specific; suggestion source depends on command item. |
| `ProtoEditorWindow.axaml.cs:36346` `commandNameAutoComplete` | ProtoUnit command name | `CUSTOM_NAME_REQUIRED` | No whitespace; existing command names are suggestions. |
| `ProtoEditorWindow.axaml.cs:36840` `ShowTransformCommandPicker/selector` | Transform command selector | `STRICT_REQUIRED` | Stored value must be an existing command name. |
| `ProtoEditorWindow.axaml.cs:37099` `tacticsControl` | Transform command tactics file | `STRICT_OPTIONAL` | Preserve unknown only in read-only/original view. |

## 2. ProtoAction core fields

| Source site | Logical field(s) | Proposed profile | Overrides / review notes |
|---|---|---|---|
| `:8573` `animationEditor` | Action animation | `SUGGESTED_OPTIONAL` | Custom animation identifiers may be valid. |
| `:8593` `impactEditor` | Impact effect | `SUGGESTED_OPTIONAL` | Custom effect paths/identifiers may be valid. |
| `:8786` `RenderFieldRow` | Dynamic ProtoAction simple field | `PER_TAG` | Must use the tag matrix below. Do not migrate as one policy. |
| `:8967` `AddAnimationEditor/editor` | Action animation | `SUGGESTED_OPTIONAL` | Same policy as `animationEditor`. |
| `:9221`, `:9533`, `:10443` `projectileEditor` | Projectile ProtoUnit | `STRICT_OPTIONAL` | Exact ProtoUnit name; no custom value. |
| `:9965` `castPowerTargetEditor` | Cast-power target type | `STRICT_OPTIONAL` | ComboBox candidate if the source is always finite. |
| `:10100` `CreateSimpleEditor` | Dynamic simple action field | `PER_TAG` | Must use the tag matrix below. |
| `:10459` `selector` | Chain-attack animation/selector | `SUGGESTED_OPTIONAL` | Confirm exact tag at migration site. |
| `:10796` `rateTypeAcb` | Resource rate type | `STRICT_OPTIONAL` | ComboBox candidate; source is known resource types. |

### Dynamic ProtoAction tag matrix (`RenderFieldRow`, `CreateSimpleEditor`, repeatable row factories)

| Field/tag family | Proposed profile | Notes |
|---|---|---|
| ProtoUnit references (`projectile`, `trailprotounit`, `modifyprotoid`, `conversionproto`, `charged`, attach proto) | `STRICT_OPTIONAL` | Must match a valid ProtoUnit. |
| Tech references | `STRICT_OPTIONAL` | Must match a valid technology. |
| ProtoAction references | `STRICT_OPTIONAL` | Must match an action available in the relevant unit/context. |
| Unit type / target type / build-limit target | `STRICT_OPTIONAL` | Dynamic suggestion source may combine unit types and ProtoUnits. |
| Damage type | `STRICT_OPTIONAL` | ComboBox candidate. |
| Modify type | `STRICT_DISPLAY_VALUE` | Uses display-to-XML mapping from `ProtoConstants`. |
| Resource type | `STRICT_OPTIONAL` | ComboBox candidate. |
| Animation | `SUGGESTED_OPTIONAL` | Custom animation identifiers remain allowed. |
| Impact effect / VFX path | `SUGGESTED_OPTIONAL` | Custom identifiers remain allowed unless a specific schema says otherwise. |
| Model attachment bone / attach bone | `SUGGESTED_OPTIONAL` | Known bones suggested; custom bone names allowed. |
| Flags added as chips | `CHIP_STRICT` | Exclude already selected flags. |
| XML enum with a closed known set | `STRICT_OPTIONAL` | Mark as ComboBox candidate during review. |

## 3. AutoRanged, modifier, and rate editors

| Source site(s) | Logical field(s) | Proposed profile | Overrides / review notes |
|---|---|---|---|
| `:11227`, `:11424`, `:11646`, `:17262`, `:17926` | Modify Type | `STRICT_DISPLAY_VALUE` | Display name maps to canonical XML value. ComboBox candidate. |
| `:11230`, `:11447`, `:17292`, `:17946` | Modify Damage Type | `STRICT_OPTIONAL` | ComboBox candidate. |
| `:11681` | Modify Damage Target Type | `STRICT_OPTIONAL` | Unit-type/build-target source. |
| `:11952` | AutoRanged bonus type | `STRICT_OPTIONAL` | Review whether custom types are ever valid. |
| `:12021` | AutoRanged rate type | `STRICT_OPTIONAL` | ComboBox candidate when source is finite. |
| `:12112` | AutoRanged include/exclude list picker | `CHIP_STRICT` | Exclude selected values. |
| `:12217` | AutoRanged mutate-into unit | `STRICT_OPTIONAL` | Exact ProtoUnit. |
| `:12850`, `:12922`, `:13290`, `:13639`, `:14413`, `:14741`, `:15209`, `:15517`, `:15549`, `:15818`, `:16664`, `:16950`, `:17602`, `:17833`, `:18246` | Typed rate target/unit type | `STRICT_OPTIONAL` | Same policy; sources vary between build-limit targets, unit types, and ProtoUnits. |
| `:13179` | Conditional rule action type | `STRICT_OPTIONAL` | ComboBox candidate. |
| `:13238`, `:13417`, `:14786`, `:18336` | Modify/conversion ProtoUnit | `STRICT_OPTIONAL` | Exact ProtoUnit. |
| `:14039` | Maintain-rate target | `STRICT_OPTIONAL` | Current source is ProtoUnit names; verify whether unit types should also be accepted. |
| `:14246`, `:15962` | Resource type | `STRICT_OPTIONAL` | ComboBox candidate. |
| `:17088` | Charged ProtoUnit | `STRICT_OPTIONAL` | Exact ProtoUnit. |

## 4. Attachments, charged effects, target cards, and nested effect rows

| Source site(s) | Logical field(s) | Proposed profile | Overrides / review notes |
|---|---|---|---|
| `:18586` `CreateAttachmentEditor` | Model attachment / attachment bone dynamic field | `PER_TAG` | Attachment asset is suggested/custom; bone is suggested/custom. |
| `:19346` `CreateStackControlEditor` | Stack action/control reference | `STRICT_OPTIONAL` | Must match an action/control from the current context. |
| `:19561` | Target card element/type | `STRICT_OPTIONAL` | ComboBox candidate if schema set is closed. |
| `:19620` | Target animation | `SUGGESTED_OPTIONAL` | Custom animation allowed. |
| `:19642`, `:20639`, `:21391` | Model/attach bone | `SUGGESTED_OPTIONAL` | Custom bone names allowed. |
| `:19690` | Forbid target/type row | `STRICT_OPTIONAL` | Verify exact source during migration. |
| `:19917`, `:20121`, `:20387` | Modify Type | `STRICT_DISPLAY_VALUE` | Canonical XML mapping. |
| `:19955`, `:20163`, `:20440` | Damage Type / modify parameter | `STRICT_OPTIONAL` | ComboBox candidate. |
| `:20558` | Charged/source VFX ProtoUnit | `STRICT_OPTIONAL` | Existing save code validates against ProtoUnits. |
| `:21235` | Effect Type | `STRICT_REQUIRED` | ComboBox candidate; currently selectable effect types. |
| `:21335`, `:21537`, `:22520` | Target Unit Type | `STRICT_OPTIONAL` | Unit type/build-limit target. |
| `:21349` | Effect animation | `SUGGESTED_OPTIONAL` | Custom animation allowed. |
| `:21363` | ProtoAction reference | `STRICT_OPTIONAL` | Current unit action only. |
| `:21377` | Attach ProtoUnit | `STRICT_OPTIONAL` | Exact ProtoUnit. |
| `:21482` | Player Relation | `STRICT_OPTIONAL` | ComboBox candidate. |
| `:21496` | Reward Resource Type | `STRICT_OPTIONAL` | ComboBox candidate. |
| `:21523` | Relativity | `STRICT_OPTIONAL` | ComboBox candidate. |
| `:21553` | Terrain/Movement Type | `STRICT_OPTIONAL` | ComboBox candidate. |
| `:21625` | Shading Type | `STRICT_OPTIONAL` | Same policy as Initial/Damage Shading; width 150. ComboBox candidate. |

## 5. Damage, modify, ramp, boost, gather, and repeated attribute rows

| Source site(s) | Logical field(s) | Proposed profile | Overrides / review notes |
|---|---|---|---|
| `:22656`, `:22830`, `:23015`, `:23229`, `:23383` | Damage Type | `STRICT_OPTIONAL` | ComboBox candidate. |
| `:22788`, `:22994`, `:23187`, `:23362` | Modify Type | `STRICT_DISPLAY_VALUE` | Canonical XML mapping. |
| `:23806` | Repeated entry value | `STRICT_OPTIONAL` | Suggestions depend on enclosing effect. |
| `:23852` | Repeated-entry add picker | `CHIP_STRICT` | Exclude values already represented by rows. |
| `:23968`, `:24865` | Dynamic repeated action row | `PER_TAG` | Resolve with the ProtoAction tag matrix. |
| `:24082`, `:24944` | Attribute value editor | `STRICT_OPTIONAL` | Suggestions depend on attribute name. |
| `:24250` | Add gather-rate attribute | `CHIP_STRICT` | Exclude attributes already present. |
| `:24482` | Add action flag | `CHIP_STRICT` | Exclude selected flags. |
| `:24616` | Add optional field/flag picker | `CHIP_STRICT` | Exclude controls already visible. |
| `:24699` | Dynamic add picker | `CHIP_STRICT` | Confirm selection commits a chip/row, not a text value. |

## 6. ProtoUnit Stats and Other Specific Attributes

| Source site(s) | Logical field(s) | Proposed profile | Overrides / review notes |
|---|---|---|---|
| `:30411`, `:30442` | Culture-aware Icon/Anim File rows | `SUGGESTED_OPTIONAL` | Asset identifiers can be custom; culture itself remains a true dropdown. |
| `:30644` | Build Limit target | `STRICT_REQUIRED` | Unit type or ProtoUnit depending on build-limit mode. |
| `:31094` | Contain / Not Contain chip picker | `CHIP_STRICT` | Exclude selected values independently per list. |
| `:31411` `CreateOtherSuggestionBox` | Shared Other Specific suggestion constructor | `PER_FIELD` | Must not be migrated with one validation policy; see matrix below. |
| `:35644` | “Other specific attribute” picker | `CHIP_STRICT` | Adds a UI section rather than XML value; excludes visible attributes. |
| `:35946` | Add Unit Type | `CHIP_STRICT` | Exclude selected unit types. |
| `:36034` | Add Unit Flag | `CHIP_STRICT` | Exclude selected flags. |

### Other Specific Attribute field matrix

| Field | Proposed profile | Options / notes |
|---|---|---|
| Culture | Not autocomplete | Keep true dropdown. |
| Resource Subtype | Not autocomplete | Keep true dropdown. |
| Pathability Flag | Not autocomplete | Keep true dropdown. |
| Placement File | `SUGGESTED_OPTIONAL` | Custom placement file allowed. |
| Partisan Type | `STRICT_OPTIONAL` | Exact ProtoUnit. |
| Disguise Proto ID | `STRICT_OPTIONAL` | Exact ProtoUnit; no custom. |
| Dead Replacement | `STRICT_OPTIONAL` | Existing values displayed; field is not addable for new attributes. |
| Dead Transform | `STRICT_OPTIONAL` | Exact ProtoUnit; no custom. |
| Eidolon Proto ID | `STRICT_OPTIONAL` | Exact ProtoUnit; no custom. |
| Initial Unit AI Stance | `STRICT_SEARCHABLE_COMBOBOX` | True closed-set ComboBox; search text only filters the known stance values. |
| Hotkey Context | `STRICT_OPTIONAL` | Strict union of `<context>` values loaded from base `game\data\gameplay\hotkeys\unit_contexts.xml` in `Data.bar` and the active mod's optional `game\data\gameplay\hotkeys\unit_contexts_mods.xml`. Custom text is forbidden. |
| Ally Hotkey Context | `STRICT_OPTIONAL` | Uses the same authoritative base-plus-mod context union as Hotkey Context; custom text is forbidden. |
| Blood Group | `STRICT_OPTIONAL` | Width 300; exact known group. |
| Dependent Unit | `STRICT_REQUIRED` | Exact ProtoUnit. |
| Dependent Attach Bone | `SUGGESTED_OPTIONAL` | Width 150; custom bone allowed. |
| Spawn ProtoUnit | `STRICT_REQUIRED` | Width 150; exact ProtoUnit. |
| Spawn Shading | `STRICT_SEARCHABLE_COMBOBOX` | Width 150; true closed-set ComboBox. Search text only filters known shading values. |
| Spawn Water ProtoUnit | `STRICT_OPTIONAL` | Width 150; exact ProtoUnit. |
| Respawn Self Target Type | `STRICT_REQUIRED` | Width 150; build-limit target source. |
| Respawn Self Train Proto | `STRICT_REQUIRED` | Width 150; exact ProtoUnit. |
| Respawn VFX | `STRICT_OPTIONAL` | Width 150; exact valid ProtoUnit name, no custom value. |
| Respawn Types | `CHIP_STRICT` | Width 150; 320px chip lane; exclude selected values. |
| Respawn Exclude Types | `CHIP_STRICT` | Width 150; 320px chip lane; exclude selected values. |
| On Damage Modify Type | `STRICT_DISPLAY_VALUE` | Canonical modify-type mapping. |
| On Damage Damage Type | `STRICT_SEARCHABLE_COMBOBOX` | True closed-set ComboBox over known damage types. |
| Initial Shading Type | `STRICT_SEARCHABLE_COMBOBOX` | Width 150; true closed-set ComboBox over known shading types. |
| Damage Shading Type | `STRICT_SEARCHABLE_COMBOBOX` | Width 150; true closed-set ComboBox over known shading types. |
| Minimap Icon | `ASSET_PATH` | Known minimap icons are suggestions, but a custom asset path is valid and must be preserved. |
| Replacement Type | `STRICT_SEARCHABLE_COMBOBOX` | Width 150; true closed-set ComboBox; empty by default. |
| Replacement Value | `STRICT_REQUIRED` | Width 300; exact ProtoUnit. |
| Socket Unit Type | `STRICT_OPTIONAL` | Unit type/build-target source. |
| Non-Socket Place Proto ID | `STRICT_OPTIONAL` | Exact valid ProtoUnit name, no custom value. |
| Shared Selection Unit Types | `CHIP_STRICT` | Inline 150px picker; no Add button; exclude selected. |
| Recharge/Aux Recharge include and exclude types | `CHIP_STRICT` | Exclude selected values. |
| Veterancy include and exclude types | `CHIP_STRICT` | Exclude selected values. |
| Selection-only enum fields converted to `ComboBox` | Not autocomplete | Keep outside shared autocomplete migration. |

## 7. ProtoAction header and bonus rows

| Source site | Logical field(s) | Proposed profile | Overrides / review notes |
|---|---|---|---|
| `:37591` `nameAcb` | ProtoAction name | `CUSTOM_NAME_REQUIRED` | No whitespace; existing action names suggested. |
| `:37612` `typeAcb` | ProtoAction type | `STRICT_REQUIRED` | Often derived/locked by action name. ComboBox candidate where editable. |
| `:38223` `AddBonusRow/typeAcb` | Action bonus target/type | `STRICT_OPTIONAL` | Source combines unit types and ProtoUnits. |

## 8. Ability editor

| Source site | Logical field(s) | Proposed profile | Overrides / review notes |
|---|---|---|---|
| `:41598` `nameBox` | Ability name | `CUSTOM_NAME_REQUIRED` | No whitespace; existing ability names suggested outside definition mode. |
| `:41758` `abilityTechEditor` | Ability technology | `STRICT_OPTIONAL` | Exact technology. |
| `:41879` `abilityFlagPicker` | Add ability flag | `CHIP_STRICT` | Exclude visible/selected flags. |
| `:41987` `boundActionPicker` | Bound action | `STRICT_OPTIONAL` | Current ProtoUnit action only. |
| `:42048` repeatable type/proto picker | Unit type or ProtoUnit chip | `CHIP_STRICT` | Exclude selected rows/chips. |
| `:42180` `abilityUnitActionEditor` | Ability unit action | `STRICT_OPTIONAL` | Current ProtoUnit action only; preserve missing original in read-only view. |
| `:42274` `placementFlagPicker` | Placement flag | `CHIP_STRICT` | Exclude selected flags. |
| `:42350` dynamic ability optional field | `PER_TAG` | Resolve exact field source; do not default all to Suggested. |

## 9. Standalone command editors

| Source site | Logical field(s) | Proposed profile | Overrides / review notes |
|---|---|---|---|
| `Windows/ProtoUnitCommandEditorWindow.cs:1124` | Validated command transform fields | `STRICT_OPTIONAL` | Existing shared strict service; preserve unknown only in read-only mode. |
| `Controls/ProtoUnitCommandTransformEditor.cs:363` `_picker` | Transform field/flag picker | `CHIP_STRICT` | Exclude values already selected by the transform editor. |
| `Classes/ProtoUnitCommandFieldFactory.cs:37` | Shared strict command selector | `STRICT_OPTIONAL` | Keep caller-provided width and busy-state callback. |

## 10. Asset path editor

| Source site | Logical field(s) | Proposed profile | Overrides / review notes |
|---|---|---|---|
| `Controls/AssetPathEditor.cs:23` `Editor` | Asset paths | `ASSET_PATH` | Uses `PathSuggestion`; display value and stored full path differ. Compact/read-only presentation must remain. |

### Hotkey context option source

- Base options come from every `<context>` element in `game\data\gameplay\hotkeys\unit_contexts.xml` loaded from `Data.bar`; the elements may be nested below grouping elements such as `<traincontexts>`.
- Mod options come from every `<context>` element in the active mod's optional `game\data\gameplay\hotkeys\unit_contexts_mods.xml`, whose document parent/root is `<unitcontextsmods>`.
- Hotkey Context and Ally Hotkey Context receive the case-insensitive, de-duplicated union of both sources.
- Missing mod files contribute no options and are not an error.
- Neither field accepts a value outside this union in editable mode.

## Migration decisions still required

Please edit these before migration:

1. For finite XML enums that are still merely marked “ComboBox candidate”, should they use `STRICT_COMBOBOX` or `STRICT_SEARCHABLE_COMBOBOX`?
2. Should unknown values loaded from modified XML be preserved in editable mode, or only in original/read-only mode?
3. Should animation and effect fields always permit custom identifiers?
4. For dynamic unit-type/build-target sources, should ProtoUnit names and abstract unit types always be combined, or selected per field?
5. Should all name fields enforce the same no-whitespace rule, including ability/action/command names?

## Required shared-system tests before migration

- Empty field click shows all suggestions.
- `Contains` filtering works (`camel` shows `CamelRider`).
- Popup wheel scrolls the suggestion list.
- Host-editor wheel is frozen while the popup stays open.
- Clicking/typing after scrolling continues to filter normally.
- Strict fields reject invalid custom text.
- Suggested fields preserve valid custom text.
- Unknown loaded values follow the selected preservation policy.
- Chip selectors exclude selected values and restore them after chip removal.
- Selection commits exactly once.
- Text-change callbacks do not recursively reopen/close the popup.
- Detaching a control removes host-wheel and focus handlers.
- Display adapters round-trip canonical XML values.
- Searchable ComboBoxes filter with case-insensitive `Contains` while committing only exact list selections.
- Searchable ComboBoxes reject unmatched editable text and never serialize it.
- Hotkey context options combine base and mod files, de-duplicate case-insensitively, and reject values outside the union.
