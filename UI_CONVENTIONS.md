# Proto Editor UI Conventions

## Remove buttons

- Every row or optional-field remove control must use the `remove-button` class.
- Do not set its content, color, size, padding, font, or content alignment locally.
- Keep only placement-specific properties locally when required, such as an unusual margin or overlay alignment.
- Every remove button must have a working `Click` handler.
- The handler must update both the visual container and the backing editor state, mark the editor dirty, and restore the related Add button when applicable.
- Paired values must be removed together.

Example:

```csharp
var removeButton = new Button
{
    Classes = { "remove-button" }
};
removeButton.Click += async (_, _) =>
{
    if (!await CheckStartLocalMod())
        return;

    rowsPanel.Children.Remove(row);
    state.Rows.Remove(rowState);
    RefreshOptionalSectionVisibility();
    MarkDirty();
};
```

## Responsive label/editor rows

- A label and its editor must be one indivisible child of a `WrapPanel`.
- Use `CreateLabeledFieldGroup` instead of adding a bare `TextBlock` followed by a control.
- Use a `WrapPanel` for rows that may become narrower when the XML preview is visible.
- Do not use a fixed multi-column `Grid` for unrelated label/editor pairs that should be allowed to wrap.
- Keep logically inseparable controls in the same group or card.
- Do not change field widths, serialization, suggestions, or event handlers merely to fix wrapping.

Example:

```csharp
var row = new WrapPanel
{
    Orientation = Orientation.Horizontal
};
row.Children.Add(CreateLabeledFieldGroup("Cooldown:", cooldownTb));
row.Children.Add(CreateLabeledFieldGroup("Duration:", durationTb));
```

## Optional fields

- Absent editable field: show the Add button only.
- Present editable field: show the field and a functional remove button; hide the Add button.
- Read-only field absent from XML: show nothing.
- Removing a field must clear its backing value/state, hide or remove its UI, restore the Add button, and mark the editor dirty.
- Never create an empty optional value merely to make the editor row visible.

## Verification

For each changed optional or repeatable field, verify:

1. Add.
2. Edit.
3. Remove.
4. Add again.
5. XML preview after each operation.
6. Save and reopen.
7. Narrow-window wrapping with the XML preview visible.
8. Read-only original-unit display.

### Chip remove controls

- Remove controls embedded inside chips must use `chip-remove-button`, not `remove-button`.
- Chip remove controls use the original compact visual: transparent background, light-gray `X`, no border, and minimal padding.
- The red 28×28 `remove-button` style is reserved for full-row or full-card removal.
- Do not override chip remove content, padding, colors, or alignment locally.

## Mirrored ProtoAction flag controls

- A specialized checkbox, selector, or chip that represents a ProtoAction flag must stay synchronized with the generic Flags editor in both directions.
- Register specialized checkboxes in `CustomFlagControls` and register every mirrored control with `RegisterProtoActionFlagSourceControl`.
- Source markers use the underlying XML flag value: cyan when inherited from tactics, orange when proto overrides tactics, and no marker for proto-only flags.
- Removing an inherited flag through either representation must preserve the existing explicit `0` proto override behavior; re-adding it restores inheritance and removes the redundant override.
