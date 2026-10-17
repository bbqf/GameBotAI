# Implementation Plan: Remove an installed gamepack record

**Branch**: `156-remove-gamepack-record` | **Date**: 2026-10-17 | **Spec**: `specs/156-remove-gamepack-record/spec.md`
**Input**: Feature specification from `/specs/156-remove-gamepack-record/spec.md` (FR-001 to FR-013, issue #325)

## Summary

Add one route, `DELETE /api/gamepacks/{name}`, that removes the installed record of a gamepack. A new method `GamepackService.RemoveAsync` takes the same gate as install and rollback. In one atomic registry `Update` call it removes the record and every owner entry of the pack. After that write works, it deletes the kept previous-version file. It never reads or changes a sequence, command, image, or take-over backup. An unknown or invalid name gives `404 not_found`. A failed write gives `500` and the registry stays as before. No UI change. Design details are in `research.md` (D-001 to D-009).

## Technical Context

**Language/Version**: C# on .NET (same as the rest of the repo)
**Primary Dependencies**: ASP.NET Core minimal API, Swashbuckle (OpenAPI), xUnit
**Storage**: Files. The gamepack registry file (atomic replace through `IGamepackRegistryStore.Update`) and the previous-version folder
**Testing**: xUnit. Unit tests in `tests\unit\Gamepacks`, contract tests in `tests\contract\Gamepacks`
**Target Platform**: Windows service (GameBot.Service)
**Project Type**: Web service (backend only)
**Performance Goals**: None. One small file write per request
**Constraints**: All-or-nothing registry change (FR-007). Same lock as install and rollback (FR-008). Keep the handler in a named method, not a large lambda (taint analyzers)
**Scale/Scope**: One new route, one new service method, two small types

## Constitution Check

*NON-NEGOTIABLE*: `build` and required `test` runs must pass before implementation progresses. Plan: T016 runs both.
*NON-NEGOTIABLE*: All text in this plan and its artifacts uses STE (Principle VI). Applied.

- I. Code Quality: Small, single-purpose method that follows `RollbackAsync`. Pass.
- II. Testing: Contract tests (FR-011), unit tests with a failing store (FR-007), a gate-wait test (FR-008), one OpenAPI test (FR-010). Pass.
- III. UX Consistency / Phone check: No web UI change (FR-012). A final diff review MUST confirm that no UI file changed. Phone support level: not applicable. Pass.
- IV. Performance: No effect. Pass.
- V. Living Documentation: OpenAPI summary, description, and examples (D-007). The class comment of `GamepacksEndpoints` and any route list in `docs\` are updated (D-009). Pass.
- VI. STE: Pass.

Post-design re-check: no violation. Complexity Tracking is empty.

## Project Structure

### Documentation (this feature)

```text
specs/156-remove-gamepack-record/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── remove-gamepack.md
├── checklists/
│   └── requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
src/
├── GameBot.Domain/Gamepacks/
│   └── GamepackService.cs                 # add GamepackRemoveStatus, GamepackRemoveOutcome, RemoveAsync
└── GameBot.Service/
    ├── Endpoints/GamepacksEndpoints.cs    # map DELETE {name}, handler RemoveAsync, class comment
    └── Swagger/SwaggerConfig.cs           # ApplyGamepackExamples: 404 and 500 examples

tests/
├── unit/Gamepacks/
│   ├── GamepackServiceRemoveTests.cs      # NEW: unit tests of RemoveAsync
│   ├── GamepackValidatorConflictTests.cs  # EXISTING: add the removed-owner user-object conflict test
│   └── GamepackRestoreTakeOverTests.cs    # EXISTING: add the owner_changed after removal test
└── contract/Gamepacks/
    ├── GamepackRemoveContractTests.cs     # NEW: route tests
    └── GamepackOpenApiTests.cs            # EXISTING: add the FR-010 test
```

**Structure Decision**: Backend only. Logic in the Domain service because it owns the gate. The endpoint handler maps the outcome to a status code.

### Exact test paths

| Test file (absolute path) | Status | Covers |
|---------------------------|--------|--------|
| `C:\src\GameBot\tests\unit\Gamepacks\GamepackServiceRemoveTests.cs` | New | Removal of record and all owner entries (also an entry whose item is gone); other packs kept; kept previous file deleted; content stores not touched; `NotFound` for unknown or invalid name with no write; failing `Update` gives `Failed` and nothing changes (FR-007); failing `DeletePrevious` after a good write still gives `Removed`; gate wait while install or rollback holds the lock (FR-008); take-over backup files stay |
| `C:\src\GameBot\tests\unit\Gamepacks\GamepackValidatorConflictTests.cs` | Existing, add test | An item with no owner entry after a removal follows the user-object conflict rule (spec assumption: the conflict test stays in the validator class, not in the removal test class) |
| `C:\src\GameBot\tests\unit\Gamepacks\GamepackRestoreTakeOverTests.cs` | Existing, add test | A restore after a removal gives `owner_changed` (existing rule, no code change) |
| `C:\src\GameBot\tests\contract\Gamepacks\GamepackRemoveContractTests.cs` | New | Success `204` and list check; remove after content items deleted by hand; other pack unchanged; new install after removal is a first install; user-object conflict and `takeOver=true`; rollback after removal `404`; rollback with no previous still `409 no_previous_version`; kept previous version removed; unknown name `404`; repeat `404`; letter case; invalid name; `DELETE keys/{fingerprint}` keeps its own route |
| `C:\src\GameBot\tests\contract\Gamepacks\GamepackOpenApiTests.cs` | Existing, add test | `DELETE /api/gamepacks/{name}` with operation id `RemoveGamepack`, summary, description, codes 204, 404, 500 |

Each contract test uses its own `GamepackContractHost` (contract tests share a data folder). Unit tests use `GamepackTestKit` and a store whose `Update` throws.

The unit test project is `tests\unit`. The final test run (T016) covers the unit and contract gamepack tests only, with the two commands below.

Run commands (see `quickstart.md`):

```text
dotnet test C:\src\GameBot\tests\unit --filter "FullyQualifiedName~Gamepack"
dotnet test C:\src\GameBot\tests\contract --filter "FullyQualifiedName~Gamepack"
```

## Complexity Tracking

No violation. Table not needed.
