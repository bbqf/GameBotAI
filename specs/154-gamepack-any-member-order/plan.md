# Implementation Plan: Accept any member order in gamepack files

**Branch**: `154-gamepack-any-member-order` | **Date**: 2026-10-17 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/154-gamepack-any-member-order/spec.md` (issue #322, B-035)

## Summary

A gamepack file in canonical (ordinal) member order puts the `type` member of a condition after other members. `System.Text.Json` needs the discriminator first by default. It throws `NotSupportedException`, which the install routes do not catch, so the answer is 500.

The fix has two parts:

1. Set `AllowOutOfOrderMetadataProperties = true` on the three `JsonSerializerOptions` objects that the gamepack parse path uses. The discriminator can then be at any position (FR-001, FR-002, FR-006).
2. Add a structural check of the discriminator (`GamepackTypeDiscriminatorCheck`) before the typed read. A missing, unknown, duplicate, or not-string discriminator gives status 400 with the `InvalidJson` error code and the JSON path, for dry run and for real install (FR-004, FR-005, FR-009).

The hash and signature code reads raw JSON and sorts keys, so it does not change (FR-003).

## Technical Context

**Language/Version**: C# 13 on .NET 9 (`net9.0`), `System.Text.Json`
**Primary Dependencies**: `System.Text.Json` (built-in); no new package
**Storage**: N/A (no stored data change, no file format change)
**Testing**: xUnit in `tests/unit`, `tests/contract`, `tests/integration`
**Target Platform**: Windows service (GameBot.Service), ASP.NET Core minimal API
**Project Type**: web-service (domain library + service + test projects)
**Performance Goals**: No second parse pass. The reader buffers an object only when the discriminator is late. The structural check walks the entry `JsonElement` once. One smoke test with hundreds of objects (no assertion on pass count).
**Constraints**: FR-008 (only install routes change in visible behavior); FR-009 (no classification by exception message text); no change to canonical text rules or the hash algorithm.
**Scale/Scope**: 3 options objects, 1 new internal class, 1 new flag, 1 mapping line.

## Polymorphic types in the gamepack parse path (FR-006)

The parse path reads a file with `GamepackFileSerializer.Parse`. It reads these roots: `manifest`, `sequences` (`CommandSequence`), `commands` (`Command`), `images` (`GamepackImage`), `seedTemplates` (`QueueTemplateDoc`). The walk below follows the declared property types from each root.

| # | Polymorphic base type | Discriminator member | Derived values | Source file | Reached from | In the parse path? |
|---|-----------------------|----------------------|----------------|-------------|--------------|--------------------|
| 1 | `SequenceStepCondition` | `type` | `imageVisible`, `commandOutcome`, `all`, `any`, `none`, `lastRun` | `src/GameBot.Domain/Commands/SequenceStepCondition.cs` | `CommandSequence.Steps[].Condition`, `.BreakCondition`; nested in `all`, `any`, `none` | Yes |
| 2 | `LoopConfig` | `loopType` | `count`, `while`, `repeatUntil` | `src/GameBot.Domain/Commands/LoopConfig.cs` | `CommandSequence.Steps[].Loop` | Yes |
| 3 | `Trigger` | `type` | `delay`, `schedule`, `image-match`, `text-match` | `src/GameBot.Domain/Triggers/Trigger.cs` | `Command` holds only `TriggerId` (a string), not a `Trigger` object | No (not read by the pack parse) |
| 4 | `SequenceStepCondition` contract | `type` | same six values | `src/GameBot.Service/Models/SequenceStepContracts.cs` | API request model only | No (API DTO; `GamepackStepReader` works on `JsonObject`) |
| 5 | `LoopConfig` contract | `loopType` | `count`, `while`, `repeatUntil` | `src/GameBot.Service/Models/SequenceStepContracts.cs` | API request model only | No (API DTO) |

Result: two types are in the parse path (rows 1 and 2). Rows 3 to 5 are listed so a reviewer can see why they are out. `GamepackImage`, `GamepackManifest`, `QueueTemplateDoc`, and `SettingsDocument` hold no polymorphic type; the option change on `SettingsDocumentJson.Options` is a protection for a future type only. Each of rows 1 and 2 has at least one unit test and one endpoint test (FR-006, FR-007). `GamepackTypeDiscriminatorCheck` reads the list from `JsonTypeInfo.PolymorphismOptions`, so a new polymorphic type in the parse path is covered with no code change. A unit test walks the type graph of `CommandSequence` and `Command` by reflection and fails if a polymorphic type exists that the test list does not name.

### Options objects (which ones get `AllowOutOfOrderMetadataProperties`)

| Options object | Defined in | Used for roots | Polymorphic types reached |
|----------------|-----------|----------------|---------------------------|
| `SequenceOptions` (`SequenceJsonOptions`) | `src/GameBot.Domain/Gamepacks/GamepackFileSerializer.cs` | `sequences` | rows 1 and 2 |
| `CommandOptions` (`CommandJsonOptions`) | `src/GameBot.Domain/Gamepacks/GamepackFileSerializer.cs` | `commands` | none today (Command has no polymorphic member); set for parity |
| `SettingsDocumentJson.Options` | `src/GameBot.Domain/QueueSettings/SettingsDocument.cs` (class `SettingsDocumentJson`, line 156; `Options` returns the private `ReadOptions`) | `seedTemplates` | none today; set for parity |
| `ManifestOptions` | `GamepackFileSerializer.cs` | `manifest`, `images` | none; not changed |

## SettingsDocumentJson: exact path and users of `Options`

Exact path: `C:\src\GameBot\src\GameBot.Domain\QueueSettings\SettingsDocument.cs` (the class `SettingsDocumentJson` is at the end of this file, with `SettingsDocument` and `QueueTemplateDoc`). There is no separate file `SettingsDocumentJson.cs`.

`SettingsDocumentJson.Options` is the same object as `ReadOptions`. Code that uses it:

| User | File | Install path? | Effect of the change |
|------|------|---------------|----------------------|
| `GamepackFileSerializer.Parse` (reads `seedTemplates`) | `src/GameBot.Domain/Gamepacks/GamepackFileSerializer.cs` line 62 | Yes | The change is needed only for parity today |
| `GamepackFileSerializer.Serialize` (writes `seedTemplates`) | same file, line 82 | No (write) | None: the option affects reads only |
| `SettingsDocumentValidator` (reads a queue settings document, `Deserialize<SettingsDocument>`) | `src/GameBot.Domain/QueueSettings/SettingsDocumentValidator.cs` line 51 | No (queue settings import) | Non-install reader. It accepts more files, never fewer. `SettingsDocument` has no polymorphic type, so there is no visible effect |
| `SettingsNormalizer` (`SerializeToNode` for sort and hash) | `src/GameBot.Domain/QueueSettings/SettingsNormalizer.cs` lines 104, 112 | No (write) | None |
| `QueueSettingsEndpoints` (`Results.Json(..., Options)` for the import report) | `src/GameBot.Service/Endpoints/QueueSettingsEndpoints.cs` line 70 | No (write) | None |
| `ManagedBackupStore` (uses `SettingsDocumentJson.Serialize`, which uses the private `WriteOptions`) | `src/GameBot.Domain/Gamepacks/Managed/ManagedBackupStore.cs` line 47 | No | None: `WriteOptions` is a separate object and is not changed |

Decision (Loop 2, F1): `Options` serves non-install readers, so the change is accepted only because it has no visible effect there. The rule is: more files are accepted, none fewer, and no write output changes. A unit test shows that the queue settings import gives the same result for a valid document before and after the change. If a review finds a visible effect, split `Options` into `InstallReadOptions` and keep `Options` unchanged (fallback, not planned).

The same check applies to `SequenceJsonOptions` and `CommandJsonOptions`. Other users (all in `src/GameBot.Domain`): `GamepackApplier.TakeOver.cs` (lines 52, 56, 193, 197: take-over backup read and write), `GamepackContentComparer.cs` (lines 68, 73: write for compare), and `QueueSettings/SettingsNormalizer.cs` (lines 65, 84, 153, 161, 167, 168, 172, 173). They only gain tolerance. Their writes do not change. No API endpoint options change (FR-008).

## Error classes and status (Clarifications, Loop 2)

All type errors become `GamepackParseResult.InvalidType`. `GamepackService.InstallAsync` maps it to `GamepackInstallStatus.InvalidJson`. `GamepacksEndpoints.ToInstallResult` answers 400 for `InvalidJson` in both modes (no `isDryRun` branch).

| Error class | Example | Dry run | Real install | Error code | Message holds |
|-------------|---------|---------|--------------|-----------|---------------|
| Type missing | condition with no `type` | 400 | 400 | `InvalidJson` | JSON path, reason "the type member is missing" |
| Type unknown | `"type":"nope"` | 400 | 400 | `InvalidJson` | JSON path, reason "the type value is unknown" |
| Type not a string | `"type":5` or `null` | 400 | 400 | `InvalidJson` | JSON path, reason "the type value must be a string" |
| Type duplicate | two `type` members | 400 | 400 | `InvalidJson` | JSON path, reason "the type member is duplicated" |
| Any of the above, nested | bad `type` in a condition inside `all` | 400 | 400 | `InvalidJson` | nested JSON path (for example `$.sequences[0].steps[0].breakCondition.conditions[1]`) |
| Type valid, any member order | `type` first, middle, or last | 200 `valid:true` | 200 | none | none |
| Other wrong shape (not a type error) | wrong member kind, bad enum | 200 `valid:false` (unchanged) | 422 (unchanged) | existing | existing |
| Text is not valid JSON, or root is not an object | broken text | 400 (unchanged) | 400 (unchanged) | `InvalidJson` | existing |
| Safety net: `NotSupportedException` escapes the check | metadata case that the check misses | 400 | 400 | `InvalidJson` | entry path, reason by exception type only |

Test rule: the check runs before the typed read, so a type error always shows the JSON path from the walk. The safety net exists only so that 500 cannot occur; it has one unit test that forces it.

## Constitution Check

*GATE: passed before Phase 0 and re-checked after Phase 1.*

- Build and test gate (NON-NEGOTIABLE): the existing gamepack tests must stay green (SC-004). Pass.
- STE (Principle VI): all text in the spec, plan, research, data model, contract, quickstart, tasks, code comments, and messages follows STE. Pass.
- I. Code quality: one new internal class, one flag, one mapping. No duplicate type knowledge (metadata drives the check). Pass.
- II. Testing: tests for type first, last, middle, missing, unknown, not-string, duplicate, nested; unit, contract, and integration levels; dry run and real install. Pass.
- III. UX consistency: no web UI change. Phone check: not applicable. The API error format is the existing one. Pass.
- IV. Performance: no second parse pass; one smoke test with hundreds of objects. Pass.
- V. Living documentation: update the OpenAPI text of the two install routes (400 now also covers a type error) and the gamepack docs. Keep the schema; add a description line only. Pass.
- Version bump: `installer/versioning/version.override.json` before the push (see CLAUDE.md).
- Complexity: no violation.

## Project Structure

### Documentation (this feature)

```text
specs/154-gamepack-any-member-order/
├── plan.md              # This file
├── research.md          # Phase 0
├── data-model.md        # Phase 1
├── quickstart.md        # Phase 1
├── contracts/
│   └── install-endpoint.md
├── checklists/
└── tasks.md             # Phase 2 (/speckit.tasks)
```

### Source Code (repository root)

```text
src/GameBot.Domain/Gamepacks/
├── GamepackFileSerializer.cs          # option flag on SequenceOptions and CommandOptions; InvalidType flag; ReadList check and safety net
├── GamepackTypeDiscriminatorCheck.cs  # NEW: structural check from JsonTypeInfo.PolymorphismOptions
└── GamepackService.cs                 # InstallAsync: InvalidType maps to InvalidJson
src/GameBot.Domain/QueueSettings/
└── SettingsDocument.cs                # SettingsDocumentJson.BuildOptions: option flag on ReadOptions
src/GameBot.Service/Endpoints/
└── GamepacksEndpoints.cs              # no logic change; OpenAPI description line only

tests/
├── unit/Gamepacks/        # parser, check class, canonical hash equality, reflection guard, queue settings import regression
├── contract/              # install routes: dry run and real install, 400 and InvalidJson
└── integration/           # end to end install with type last and nested
```

**Structure Decision**: Single solution with the existing layout (Domain, Service, tests). All logic stays in `GameBot.Domain/Gamepacks`. The Service project changes only the OpenAPI text.

## Complexity Tracking

No constitution violation. This table is empty.
