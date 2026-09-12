# AoM Divine Data Editor test suite

The suite is organized by product responsibility. Pass or incident numbers may remain
in individual regression filenames when they explain why a narrowly focused test exists,
but they no longer determine the directory structure.

## Layout

| Directory | Responsibility |
| --- | --- |
| `TestInfrastructure` | Isolated temporary workspaces, source contracts, and the shared Avalonia dispatcher |
| `Infrastructure` | BAR/XMB, text encoding, transactions, paths, validation, deletion and reference policies |
| `Units` | ProtoUnit load/save, strings, types, stats, containment and asset references |
| `Units/Actions` | Shared ProtoAction editor, layout, tactics overlays and action-state stability |
| `Technologies` | Technology persistence, strings, references, validation and Tech Types |
| `Technologies/Effects` | Individual technology effect families and effect-catalog coverage |
| `Technologies/Hardening` | Historical safety and preservation regressions |
| `Abilities`, `Commands`, `Tactics` | Auxiliary definition formats and their persistence rules |
| `Assets` | Icon, animation, sound and destination catalogs |
| `UI` | Navigation, shared controls, resources and visual editor contracts |
| `Architecture` | Intentional source-level contracts that cannot yet be tested behaviorally |
| `Integration` | Multi-document persistence boundaries |
| `EndToEnd` | Complete filesystem or headless-editor user workflows |

## Test levels

- **Unit** tests exercise pure parsing, validation and transformation policies.
- **Integration** tests combine real temporary files, document safety and transactions.
- **UI** tests run actual Avalonia controls on the shared headless dispatcher.
- **EndToEnd** tests save, reopen and verify complete workflows across module boundaries.
- **Architecture** tests are the limited set of source checks protecting required shared wiring.

New tests should use `[Trait("Category", TestCategories.…)]`. Older tests can be
categorized gradually when they are next edited; moving them must never remove coverage.

## Alpha coverage matrix

| Alpha risk | Primary coverage |
| --- | --- |
| Comments, unknown XML, encoding and newline preservation | `Units/ProtoXmlPreservationTests`, `Integration/AuxiliaryPersistenceWorkflowTests`, technology preservation suites |
| External edits are never silently overwritten | file transaction tests, named-document merge tests, `EndToEnd/TechnologyEditorWorkflowTests` |
| Multi-file failure restores exact bytes | file transaction tests and auxiliary/technology workflow tests |
| Rename updates supported cross-module references | reference policy/migration tests and `EndToEnd/CrossModuleReferenceWorkflowTests` |
| Delete blocks live or unknown references | deletion safety tests and `Architecture/SafeDeletionArchitectureTests` |
| Malformed owned structures open read-only | mod/technology safety tests and `Units/UnitStructuralLoadingTests` |
| String additions use editor sections without rewriting user text | game-data text tests and `EndToEnd/AlphaModLifecycleWorkflowTests` |
| Managers share filtering, virtualization and detailed deletion UI | `Architecture/PerformanceAndManagerArchitectureTests` plus UI navigation tests |
| Technology editor real load/save pipeline | `EndToEnd/TechnologyEditorWorkflowTests` |
| Complete definition rename commits and reopens across modules | `EndToEnd/AlphaModLifecycleWorkflowTests` |

Gods and Powers remain intentionally limited to foundational safety coverage until those
editors are feature-complete.

## Isolation rules

- Filesystem tests must use `TestWorkspace` or another explicitly isolated temporary fixture.
- Tests must not read or write the user's saved editor settings or real mod directory.
- A configured real game corpus is optional and read-only. Set `AOM_DATA_BAR` to enable the
  existing original-technology corpus compatibility test.
- Tests must not depend on execution order, current culture, machine line endings or a
  writable application installation directory.

## Alpha verification

From the editor project directory:

```powershell
.\scripts\Invoke-AlphaVerification.ps1
```

The gate runs the complete test suite, performs a Release build, and confirms that the
expected Release executable was produced.
