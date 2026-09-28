# Tasks: Press and hold at a detected point (anchored long press)

**Input**: Design documents from `specs/111-anchored-long-press/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/primitive-tap-hold.md, quickstart.md

**Tests**: The constitution requires tests for each change and a regression test for the defect. Test tasks come before the implementation tasks in each story.

**Organization**: Tasks are in groups by user story. Each story can be tested alone.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: The user story of the task (US1, US2, US3, US4)

---

## Phase 1: Setup

**Purpose**: Get a known baseline.

- [ ] T001 Build the solution with `export DOTNET_ROLL_FORWARD=Major; dotnet build GameBot.sln -c Release -warnaserror` from the repo root, and record the baseline result. Record also that `tests/unit/Commands/CommandExecutorPrimitiveTapTests.cs` fails on Linux before any change (`System.Drawing` needs Windows), so that later runs can separate environment failures from real failures.

---

## Phase 2: Foundational (blocking prerequisites)

**Purpose**: The new field and the new outcome field exist in the domain and in the DTOs. All stories need them.

- [ ] T002 Add `public int? HoldMs { get; init; }` with `[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]`, and the constants `MinHoldMs = 0`, `MaxHoldMs = 5000`, `DefaultTapDurationMs = 200`, to `PrimitiveTapConfig` in `src/GameBot.Domain/Commands/CommandStep.cs`. Write an STE XML comment that tells: absent or 0 gives a single tap; more than 0 gives a press and hold at the detected point for that time.
- [ ] T003 [P] Add `public int? HoldMs { get; init; }` with `[System.ComponentModel.DataAnnotations.Range(PrimitiveTapConfig.MinHoldMs, PrimitiveTapConfig.MaxHoldMs)]` and `[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]` to `PrimitiveTapConfigDto`, and `public int? HoldMs { get; init; }` with `[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]` to `StepExecutionOutcomeDto`, in `src/GameBot.Service/Models/Commands.cs`.
- [ ] T004 [P] Add the optional parameter `int? HoldMs = null` at the end of the `PrimitiveTapStepOutcome` record in `src/GameBot.Service/Services/ICommandExecutor.cs`.

**Checkpoint**: The solution builds. No behavior changes.

---

## Phase 3: User Story 1 - A command step presses and holds at a detected point (Priority: P1) MVP

**Goal**: A `PrimitiveTap` step with `holdMs > 0` sends one swipe with the same start point and end point and a duration of `holdMs`. Absent or 0 sends the tap of today (duration 200 ms).

**Independent Test**: Run a command with a `PrimitiveTap` step with `HoldMs = 700` against a session test double. The test double gets one `swipe` input at the point with `DurationMs = 700`.

### Tests for User Story 1

- [ ] T005 [P] [US1] Create `tests/unit/Commands/PrimitiveTapInputTests.cs` (Linux-safe). Check that `PrimitiveTapInput.Create(x, y, holdMs)` gives type `swipe`, args `x1 = x2 = x`, `y1 = y2 = y`, and `DurationMs` equal to 200 for `holdMs` null and 0, and equal to `holdMs` for 1, 700 and 5000. Check that `PrimitiveTapInput.EffectiveHoldMs(holdMs)` gives null for null and 0, and the value for more than 0.
- [ ] T006 [P] [US1] Add tests to `tests/unit/Commands/CommandExecutorPrimitiveTapTests.cs` (Windows only, CI): a new stub `RecordingSessionManagerStub` that records the dispatched `InputAction` list and does not change the args (no jitter); (a) a step with `HoldMs = 700` dispatches one `swipe` with `DurationMs = 700` and the same start and end point, and the outcome has `HoldMs = 700`; (b) a step without `HoldMs` dispatches `DurationMs = 200`, and the outcome has `HoldMs = null`; (c) a step with `HoldMs = 700` whose image is not found dispatches no input, and the outcome is `skipped_detection_failed` as for a tap.
- [ ] T007 [P] [US1] Add a test to `tests/unit/Parameters/CommandStepResolverTests.cs`: a `PrimitiveTap` step with `HoldMs = 700` and a parametrized `referenceImageId` keeps `HoldMs = 700` after resolution.

### Implementation for User Story 1

- [ ] T008 [P] [US1] Create `src/GameBot.Service/Services/PrimitiveTapInput.cs`: an `internal static class PrimitiveTapInput` with `Create(int x, int y, int? holdMs)` that returns `new GameBot.Emulator.Session.InputAction("swipe", { x1, y1, x2, y2 }, null, duration)` (duration = `holdMs > 0 ? holdMs : PrimitiveTapConfig.DefaultTapDurationMs`), and `EffectiveHoldMs(int? holdMs)` that returns `holdMs > 0 ? holdMs : null`. Write STE comments.
- [ ] T009 [US1] In `src/GameBot.Service/Services/CommandExecutor.cs` `TryDetectAndTap`, build the input with `PrimitiveTapInput.Create(x, y, step.PrimitiveTap?.HoldMs)`, read the executed point back from its args, and set `HoldMs: PrimitiveTapInput.EffectiveHoldMs(step.PrimitiveTap?.HoldMs)` on the outcome. Keep the tap of today unchanged when `HoldMs` is null or 0.
- [ ] T010 [P] [US1] Add a test to `tests/unit/BackupServiceTests.cs`: a backup and a restore of a command with a `PrimitiveTap` step with `HoldMs = 700` keep `HoldMs = 700`, and a step without `HoldMs` stays without it.
- [ ] T011 [US1] In `src/GameBot.Domain/Parameters/CommandStepResolver.cs`, copy `HoldMs` into the new `PrimitiveTapConfig` that the resolver makes. Do not add `primitiveTap.holdMs` to `CommandStepFieldPaths.SupportedNumericPaths`.

**Checkpoint**: T005, T007 and T010 pass on Linux. T006 compiles (it runs on Windows CI).

---

## Phase 4: User Story 2 - The hold duration is kept on save and read-back (Priority: P1)

**Goal**: `holdMs` survives create, update, storage and read-back. A value outside 0 to 5000 gets 400.

**Independent Test**: `POST /api/commands` with `holdMs: 700`, then `GET`. The step has `holdMs: 700`. `holdMs: 5001` and `-1` get 400.

### Tests for User Story 2

- [ ] T012 [P] [US2] Create `tests/integration/Commands/PrimitiveTapHoldIntegrationTests.cs` (pattern of `PrimitiveTapValidationIntegrationTests.cs`: `[Collection("ConfigIsolation")]`, env vars, `TestEnvironment.PrepareCleanDataDir()`, bearer token). Tests: (a) create with `holdMs: 700`, then `GET /api/commands/{id}` returns `holdMs: 700`; (b) `PATCH /api/commands/{id}` with `holdMs: 1000`, then `GET` returns 1000; (c) create with `holdMs: 0` returns `holdMs: 0` on read-back; (d) create without `holdMs` has no `holdMs` property on read-back; (e) create with `holdMs: 5000` gives 201; (f) create with `holdMs: -1` and with `holdMs: 5001` gives 400, and the body contains `primitiveTap.holdMs must be between 0 and 5000`; (g) `PATCH` with `holdMs: 5001` gives 400; (h) `POST /api/steps/execute` with a `PrimitiveTap` step with `holdMs: 5001` gives 400 with the same text; (i) create with `fieldTemplates: { "primitiveTap.holdMs": "{{hold}}" }` gives 400.

### Implementation for User Story 2

- [ ] T013 [US2] In `src/GameBot.Service/Endpoints/CommandsEndpoints.cs`: in `ValidateStep` for `PrimitiveTap`, return `"primitiveTap.holdMs must be between 0 and 5000"` (built from `PrimitiveTapConfig.MinHoldMs` and `MaxHoldMs`) when `HoldMs` is outside the range; map `HoldMs` in `ToDomainPrimitiveTap` and `ToResponsePrimitiveTap`.
- [ ] T014 [US2] In `src/GameBot.Service/Endpoints/StepsEndpoints.cs`: add the same range rule to `ValidateStep`, and map `HoldMs` in `ToDomainStep`.
- [ ] T015 [US2] Check the `PATCH` path in `src/GameBot.Service/Endpoints/CommandsEndpoints.cs` uses `ValidateStep` and `ToDomainStep` for its steps, so that the rule and the mapping apply to update too. Fix the path if it does not.

**Checkpoint**: T012 passes on Linux.

---

## Phase 5: User Story 3 - The execution log shows the point and the hold duration (Priority: P2)

**Goal**: The `tap` detail and the step outcome show `holdMs` for a press and hold. A tap without a hold does not change.

**Independent Test**: Log a command outcome with `HoldMs = 700` and read the entry through `/api/execution-logs/{id}`.

### Tests for User Story 3

- [ ] T016 [P] [US3] Add tests to `tests/integration/ExecutionLogs/CommandExecutionLoggingIntegrationTests.cs`. They log a `PrimitiveTapStepOutcome` directly, because image detection runs only on Windows; T006 covers the path from the executor to the outcome on Windows CI. Tests: (a) an outcome with `ResolvedPoint (11,22)`, `ExecutedPoint (13,20)` and `HoldMs: 700` gives a `tap` detail with the message "Press and hold targeted (11,22), executed at (13,20) for 700 ms." and the attribute `holdMs` = 700; (b) the same point for target and executed gives "Press and hold at (11,22) for 700 ms."; (c) an outcome without `HoldMs` gives "Tap executed at (11,22)." and no `holdMs` attribute.

### Implementation for User Story 3

- [ ] T017 [US3] In `src/GameBot.Service/Services/ExecutionLog/ExecutionLogService.cs` `LogCommandExecutionAsync`, when `outcome.HoldMs > 0`, write the press-and-hold text of the contract and add `["holdMs"] = outcome.HoldMs` to the attributes. Keep the text and the attributes of today when `HoldMs` is null.
- [ ] T018 [US3] Map `HoldMs` to the step outcome of the execute responses: `ToResponseOutcome` in `src/GameBot.Service/Endpoints/CommandsEndpoints.cs` (`StepExecutionOutcomeDto.HoldMs`), and `ToResponseOutcome` in `src/GameBot.Service/Endpoints/StepsEndpoints.cs` (add `holdMs` only when it is set, so that the response of a tap does not change).

**Checkpoint**: T016 passes on Linux.

---

## Phase 6: User Story 4 - The API document and the authoring UI show the hold duration (Priority: P3)

**Goal**: The OpenAPI document has `holdMs` with range and description. The web UI tap editor sets it.

**Independent Test**: Read `/swagger/v1/swagger.json`. Open the tap editor in the web UI tests.

### Tests for User Story 4

- [ ] T019 [P] [US4] Create `tests/contract/PrimitiveTapHoldOpenApiTests.cs` (pattern of `tests/contract/Sequences/SequenceTimeLimitOpenApiTests.cs`): `PrimitiveTapConfigDto.properties.holdMs` has `minimum` 0, `maximum` 5000, type `integer`, and a description that contains "press and hold"; `detectionTarget` is still required; `StepExecutionOutcomeDto.properties.holdMs` exists with a description.
- [ ] T020 [P] [US4] Add tests to `src/web-ui/src/components/commands/__tests__/TapPanel.test.tsx`: the panel renders a "Hold duration (ms)" input; a value of 700 comes out in `onConfirm` as `holdMs: '700'`; an empty value comes out as `holdMs: undefined`; a value of 5001, -1 or 1.5 shows an error and does not call `onConfirm`; an `initialValue` with `holdMs` fills the input.

### Implementation for User Story 4

- [ ] T021 [P] [US4] Create `src/GameBot.Service/Swagger/PrimitiveTapHoldSchemaFilter.cs` (an `ISchemaFilter`): set the description of `holdMs` on `PrimitiveTapConfigDto` ("Optional. Hold duration in milliseconds, 0 to 5000. More than 0: press and hold at the detected point (plus offsets) for this duration. Absent or 0: a single tap.") and on `StepExecutionOutcomeDto` ("Hold duration in milliseconds of a PrimitiveTap step that pressed and held. Absent for a single tap and for other steps."). Register it in `src/GameBot.Service/GameBotServiceSetup.cs` with `options.SchemaFilter<PrimitiveTapHoldSchemaFilter>();`.
- [ ] T022 [P] [US4] Add `holdMs` to `PrimitiveTapConfigDto` in `specs/openapi.json`: `{ "maximum": 5000, "minimum": 0, "type": "integer", "format": "int32", "nullable": true, "description": "..." }` with the description of T021.
- [ ] T023 [P] [US4] Add `holdMs?: number` to the `PrimitiveTapConfigDto` type in `src/web-ui/src/services/commands.ts`.
- [ ] T024 [US4] In `src/web-ui/src/components/commands/TapPanel.tsx`, add `holdMs?: string` to `TapPanelValue`, add a "Hold duration (ms)" number input (`id="tap-panel-hold-ms"`, min 0, max 5000, step 1) with an STE hint, and a check: empty is permitted; else an integer from 0 to 5000, or the error "Hold duration must be an integer from 0 to 5000.".
- [ ] T025 [US4] In `src/web-ui/src/components/commands/CommandForm.tsx`, add `holdMs?: string` to `StepEntry.primitiveTap`; pass `holdMs` in the `TapPanel` `initialValue`; in `onConfirm`, put `holdMs` on `primitiveTap` (not on `detectionTarget`); in `toStepItems`, add ", hold N ms" to the description when `holdMs` is set and more than 0.
- [ ] T026 [US4] In `src/web-ui/src/pages/CommandsPage.tsx`, map `holdMs` from the DTO to the form (`String(holdMs)`) and from the form to the DTO (`Number(holdMs)` when not empty, else absent).

**Checkpoint**: T019 passes. `cd src/web-ui && npm test -- --silent` passes.

---

## Phase 7: Polish and cross-cutting concerns

- [ ] T027 [P] Update `docs/architecture.md`: tell that a `PrimitiveTap` command step has an optional `holdMs` (0 to 5000) for a press and hold at the detected point, and that the log `tap` detail shows it. Set the "Last reviewed" line to 2026-09-28, feature 111, #235.
- [ ] T028 [P] Add an entry under `## [Unreleased]` / `### Added` in `CHANGELOG.md`: "Press and hold at a detected point (111-anchored-long-press, #235)", with the field, the range and the 400, the runtime input, the step outcome and log output, the OpenAPI and web UI changes, and the note that a step without `holdMs` does not change.
- [ ] T029 [P] Add row 111 to `specs/STATUS.md` ("Press and hold at a detected point (anchored long press)", Implemented) and set the `**Status**:` line of `specs/111-anchored-long-press/spec.md` to Implemented.
- [ ] T030 Run `export DOTNET_ROLL_FORWARD=Major; dotnet build GameBot.sln -c Release -warnaserror`, then `dotnet test` for `tests/unit`, `tests/integration` and `tests/contract`. Compare the failures with the T001 baseline. Only environment failures (Windows-only, emulator) may remain.
- [ ] T031 Run `cd src/web-ui && npm ci && npm test -- --silent`.
- [ ] T032 Check all new and changed text (comments, messages, docs, changelog) for STE (constitution Principle VI).

---

## Dependencies and execution order

- Phase 1 → Phase 2 → Phases 3 to 6 → Phase 7.
- Phase 2 (T002 to T004) blocks all stories.
- US1 (Phase 3) and US2 (Phase 4) do not depend on each other.
- US3 (Phase 5) T017 uses `PrimitiveTapStepOutcome.HoldMs` (T004). T018 changes the same endpoint files as T013 and T014: do it after them.
- US4 (Phase 6): T021 needs T003. T024 → T025 → T026 (the form types flow from panel to form to page).

## Parallel examples

- Phase 2: T003 and T004 at the same time (different files).
- US1: T005, T006, T007, T008 and T010 at the same time.
- US4: T019, T020, T021, T022 and T023 at the same time.
- Phase 7: T027, T028 and T029 at the same time.

## Implementation strategy

1. MVP: Phases 1, 2, 3 and 4 (US1 and US2). An author can save a hold and the device gets it.
2. Then US3 (log and outcome), then US4 (OpenAPI and web UI).
3. Finish with Phase 7.
