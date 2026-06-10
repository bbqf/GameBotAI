# Tasks: Tap-Point Jitter

**Input**: Design documents from `/specs/058-tap-point-jitter/`
**Prerequisites**: plan.md (required), spec.md (required), research.md, data-model.md, contracts/

**Tests**: Required per constitution (Principle II: Testing Standards).

**Organization**: Tasks are grouped by user story. US2 (configuration) is foundational — the `TapJitterRadiusPx` config property and the `CoordinateJitter` helper it parameterizes must exist before US1's dispatch-time jitter can be implemented. US3 (UI/docs visibility) only requires the config property to exist and is independent of US1.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup

**Purpose**: No new projects or directories needed. This phase verifies the build is green before changes begin.

- [ ] T001 Verify build and full test suite pass (`dotnet build -c Debug && dotnet test -c Debug`)

---

## Phase 2: Foundational — Jitter Helper & Configuration (US2, Priority: P2) 🎯 Prerequisite

**Purpose**: Introduce the `TapJitterRadiusPx` configuration property and the `CoordinateJitter` helper that US1 depends on. Must be complete before US1 work begins.

**Goal**: `AppConfig.TapJitterRadiusPx` (default 5, ≥0, negative→default) is wired through `Program.cs` and `IConfigApplier`; `CoordinateJitter.Apply` computes clamped per-axis offsets per `data-model.md`.

**Independent Test**: Unit tests for `CoordinateJitter.Apply` (radius=0 passthrough, radius=N stays within bounds, never negative, independent calls vary) and for `AppConfig`/`ConfigApplier` validation (default 5, 0 valid, negative falls back to 5) pass.

### Tests for Foundational Phase

- [ ] T002 [P] [US2] Add `tests/unit/Domain/CoordinateJitterTests.cs` — radius=0 returns input unchanged; radius=N produces results within `[x-N,x+N]`/`[y-N,y+N]` across many samples; near-zero coordinates with radius>0 never go negative; consecutive calls (e.g. 4 in a row) produce varying offsets (seed-uniqueness per research R-002)
- [ ] T003 [P] [US2] Add `DefaultTapJitterRadiusPxIsFive`, `TapJitterRadiusPxCanBeSetToZero`, `TapJitterRadiusPxNegativeFallsBackToDefault` tests to `tests/unit/Config/AppConfigValidationTests.cs`

### Implementation for Foundational Phase

- [ ] T004 [P] [US2] Implement `CoordinateJitter` static helper in `src/GameBot.Domain/Services/CoordinateJitter.cs` — `Apply(int x, int y, int radiusPx)` returns `(x, y)` unchanged when `radiusPx <= 0`; otherwise draws independent per-axis offsets from `[-radiusPx, +radiusPx]` using an LCG seeded from `DateTime.UtcNow.Ticks` XORed with an `Interlocked.Increment`-based counter (mirrors `SequenceRunner.GetAppliedDelay` pattern, per research R-002), then clamps each result with `Math.Max(0, ...)`
- [ ] T005 [P] [US2] Add `TapJitterRadiusPx` property (`int`, default `5`) with XML doc comment to `src/GameBot.Domain/Config/AppConfig.cs`, documenting `GAMEBOT_TAP_JITTER_RADIUS_PX` mapping, `0` disables, negative falls back to 5, per data-model.md
- [ ] T006 [US2] Wire `GAMEBOT_TAP_JITTER_RADIUS_PX` in the `AppConfig` singleton registration in `src/GameBot.Service/Program.cs` (~line 145-172): `int.TryParse(jitterEnv, out var jParsed) && jParsed >= 0 ? jParsed : 5`, include `TapJitterRadiusPx = jitterRadius` in the constructed `AppConfig`
- [ ] T007 [US2] Add `_appConfig.TapJitterRadiusPx = GetInt(snapshot, "GAMEBOT_TAP_JITTER_RADIUS_PX", 5) is var jitter && jitter >= 0 ? jitter : 5;` to `Apply` in `src/GameBot.Service/Services/IConfigApplier.cs` (~line 51-56, alongside `TapRetryProgression`)
- [ ] T008 [US2] Verify build passes and T002-T003 tests are green

**Checkpoint**: `AppConfig.TapJitterRadiusPx` and `CoordinateJitter.Apply` are available and validated. US1 dispatch-time jitter can now be implemented.

---

## Phase 3: User Story 1 — Every Executed Tap and Swipe Lands Near, Not Exactly On, the Target (Priority: P1) 🎯 MVP

**Goal**: `SessionManager.SendInputsAsync` applies `CoordinateJitter` to tap `(x,y)` and swipe `(x1,y1,x2,y2)` args before dispatch, mutating `InputAction.Args` in place; `CommandExecutor` reads back the post-jitter values and reports both pre-jitter target and post-jitter executed coordinates in step outcomes and execution logs.

**Independent Test**: Run a primitive tap step at a fixed target multiple times; observe (via `SessionManagerJitterTests` and `CommandExecutorPrimitiveTapTests`/`CommandExecutorSwipeTests`) that dispatched coordinates vary within ±default radius of the target, swipe endpoints are jittered independently, coordinates near (0,0) never go negative, and step outcomes/execution log details report both target and executed points.

### Tests for User Story 1

- [ ] T009 [P] [US1] Add `tests/unit/Emulator/SessionManagerJitterTests.cs` — construct `SessionManager` in stub mode (set `GAMEBOT_USE_ADB=false` for the test) and verify `SendInputsAsync` mutates `InputAction.Args["x"]/["y"]` for `Type=="tap"` and `["x1"]["y1"]["x2"]["y2"]` for `Type=="swipe"` to values within the default ±5px radius of the original targets; start/end swipe offsets are independent (not identical across many samples); near-zero targets never produce negative results (jitter normalization runs before the ADB-mode branch, so stub mode exercises it)
- [ ] T010 [P] [US1] Add unit test(s) to `tests/unit/Commands/CommandExecutorPrimitiveTapTests.cs` — when a primitive tap is detected and dispatched, the resulting `PrimitiveTapStepOutcome.ExecutedPoint` reflects the (possibly jittered) coordinates read back from the dispatched `InputAction.Args`, while `ResolvedPoint` continues to hold the pre-jitter detected target
- [ ] T011 [P] [US1] Add unit test(s) to `tests/unit/Commands/CommandExecutorSwipeTests.cs` — for an explicit `Swipe` step, `PrimitiveTapStepOutcome.TargetSwipe` reflects `step.Swipe.StartX/Y`/`EndX/Y` and `ExecutedSwipe` reflects the (possibly jittered) coordinates read back from the dispatched `InputAction.Args`

### Implementation for User Story 1

- [ ] T012 [US1] Add `PrimitiveSwipePoints(PrimitiveTapResolvedPoint Start, PrimitiveTapResolvedPoint End)` record and append optional `ExecutedPoint` (`PrimitiveTapResolvedPoint?`), `TargetSwipe` (`PrimitiveSwipePoints?`), `ExecutedSwipe` (`PrimitiveSwipePoints?`) parameters to `PrimitiveTapStepOutcome` in `src/GameBot.Service/Services/ICommandExecutor.cs`
- [ ] T013 [US1] In `src/GameBot.Emulator/Session/SessionManager.cs` `SendInputsAsync`, add a jitter normalization pass at the **top of the method** (after the session lookup, **before** the `_useAdb`/`DeviceSerial` branch at ~line 114): materialize `actions` once, and for each action call `CoordinateJitter.Apply` with `_appConfig.TapJitterRadiusPx` — for `Type=="tap"` on `Args["x"]`/`["y"]`, for `Type=="swipe"` independently on `Args["x1"]/["y1"]` and `["x2"]/["y2"]` — writing the jittered values back into the same `Args` entries. This ensures jitter applies (and the caller-visible `Args` mutation occurs) in **both** real-ADB mode and stub mode (`GAMEBOT_USE_ADB=false` / no bound device, where the per-action ADB loop is skipped entirely), keeping behaviour consistent and the T009/T021/T022 tests runnable in CI
- [ ] T014 [US1] In `TryDetectAndTap` in `src/GameBot.Service/Services/CommandExecutor.cs` (~line 574-585), after `SendInputsAsync` returns, read back `tapArgs["x1"]`/`["y1"]` and pass `new PrimitiveTapResolvedPoint(executedX, executedY)` as `ExecutedPoint` in the constructed `PrimitiveTapStepOutcome` (keeping existing `ResolvedPoint = (x, y)` as the pre-jitter target)
- [ ] T015 [US1] In the `Swipe` step handling in `src/GameBot.Service/Services/CommandExecutor.cs` (~line 233-249), after `SendInputsAsync` returns, build `TargetSwipe` from `step.Swipe.StartX/Y`/`EndX/Y` and `ExecutedSwipe` by reading back `swipeArgs["x1"]/["y1"]/["x2"]/["y2"]`, and include both in the returned `PrimitiveTapStepOutcome`
- [ ] T016 [US1] In `src/GameBot.Service/Services/ExecutionLog/ExecutionLogService.cs` `LogCommandExecutionAsync` (~line 136-146), extend the "Tap executed at (X,Y)." detail item to include `ExecutedPoint` when present (e.g. `"Tap targeted (X,Y), executed at (X',Y')."` when they differ, otherwise unchanged text), and add an analogous detail item for swipe steps using `TargetSwipe`/`ExecutedSwipe`
- [ ] T017 [US1] In `src/GameBot.Service/Models/Commands.cs` (~line 80-96), add a `SwipePointsDto` (`{ Start, End }`, each a `ResolvedPointDto`) and add `ResolvedPointDto? ExecutedPoint`, `SwipePointsDto? TargetSwipe`, `SwipePointsDto? ExecutedSwipe` properties to `StepExecutionOutcomeDto` — reuse the existing `ResolvedPointDto` for all point shapes (do NOT add a duplicate `ExecutedPointDto` type)
- [ ] T018 [P] [US1] Map `ExecutedPoint`/`TargetSwipe`/`ExecutedSwipe` from `PrimitiveTapStepOutcome` to the new DTO fields in `ToResponseOutcome` in `src/GameBot.Service/Endpoints/CommandsEndpoints.cs` (~line 168-184)
- [ ] T019 [P] [US1] Map `executedPoint`/`targetSwipe`/`executedSwipe` from `PrimitiveTapStepOutcome` into the anonymous outcome object in `ToResponseOutcome` in `src/GameBot.Service/Endpoints/StepsEndpoints.cs` (~line 164-171)
- [ ] T020 [US1] Verify build passes and T009-T011 tests (plus existing suite) are green

**Checkpoint**: All taps and swipes dispatched via `SessionManager` are jittered automatically (FR-001/002/003/007/012), and step outcomes/execution logs report both target and executed coordinates (FR-013/SC-006).

---

## Phase 4: User Story 2 — Operator Tunes or Disables Jitter via Configuration (Priority: P2)

**Goal**: Confirm end-to-end that changing `GAMEBOT_TAP_JITTER_RADIUS_PX` (including to `0`) changes dispatch behaviour without any sequence/command/step edits.

**Independent Test**: With `TapJitterRadiusPx=0`, dispatched coordinates exactly equal the targets. With `TapJitterRadiusPx=20`, the maximum per-axis offset observed matches 20 (not the default 5).

> **Note**: The configuration plumbing (`AppConfig.TapJitterRadiusPx`, `Program.cs`, `ConfigApplier`) was completed in Phase 2, and the dispatch-time application (`CoordinateJitter.Apply` called from `SendInputsAsync`) was completed in Phase 3. This phase adds end-to-end tests confirming the configured radius actually controls dispatch behaviour.

### Tests for User Story 2

- [ ] T021 [US2] Add test to `tests/unit/Emulator/SessionManagerJitterTests.cs` (stub mode, as in T009) — construct `SessionManager` with `AppConfig { TapJitterRadiusPx = 0 }`; verify `SendInputsAsync` leaves tap and swipe `Args` coordinates exactly equal to the original target values (FR-005, US2 acceptance scenario 1)
- [ ] T022 [US2] Add test to `tests/unit/Emulator/SessionManagerJitterTests.cs` (stub mode, as in T009) — construct `SessionManager` with `AppConfig { TapJitterRadiusPx = 20 }`; verify dispatched coordinates fall within `[-20,+20]` of the target across many samples and that values outside the default `[-5,+5]` range occur (confirms the configured radius, not the default, governs the offset; US2 acceptance scenario 2)

### Implementation for User Story 2

- [ ] T023 [US2] Verify build passes and T021-T022 tests are green

**Checkpoint**: Jitter radius is fully configurable end-to-end, including disabling it (US2 acceptance scenarios 1-4 satisfied).

---

## Phase 5: User Story 3 — Jitter Setting Visible Alongside Other Configuration Values (Priority: P3)

**Goal**: `GAMEBOT_TAP_JITTER_RADIUS_PX` appears in the general Configuration variables list with its current value, default, and source, and is documented in `ENVIRONMENT.md`. No per-step UI control is introduced.

**Independent Test**: `GET /api/config` (and the web-ui Configuration page it powers) includes `GAMEBOT_TAP_JITTER_RADIUS_PX` with value `5` when unset; `ENVIRONMENT.md` documents the parameter.

### Tests for User Story 3

- [ ] T024 [P] [US3] Add tests to `tests/unit/ConfigMaskingAndMergeTests.cs` (or `ConfigUpdateTests.cs`) asserting (a) `RefreshAsync` snapshot `Parameters` contains `GAMEBOT_TAP_JITTER_RADIUS_PX` with default value `5` and source `Default` when not otherwise set, and (b) a value saved in `config/config.json` for `GAMEBOT_TAP_JITTER_RADIUS_PX` is reflected in the snapshot with source `File` (FR-008 precedence: default → saved file → environment)

### Implementation for User Story 3

- [ ] T025 [US3] Add `["GAMEBOT_TAP_JITTER_RADIUS_PX"] = 5` to `BuildDefaultRelevantKeys()` in `src/GameBot.Service/Services/ConfigSnapshotService.cs` (~line 259-260, alongside `GAMEBOT_TAP_RETRY_COUNT`/`GAMEBOT_TAP_RETRY_PROGRESSION`)
- [ ] T026 [P] [US3] Add a `GAMEBOT_TAP_JITTER_RADIUS_PX` entry to `ENVIRONMENT.md` (alongside the `ADB / Emulator` or tap-retry section) documenting purpose, default `5`, `0` disables jitter, negative values fall back to `5`, and noting that jitter applies to **all** dispatch paths including the raw `POST /sessions/{id}/inputs` API (radius `0` is the escape hatch for pixel-exact input)
- [ ] T027 [US3] Verify build passes and T024 test is green; manually confirm the web-ui Configuration page lists `GAMEBOT_TAP_JITTER_RADIUS_PX` and that no jitter-specific control exists in the command authoring or execution UIs (FR-011)

**Checkpoint**: The jitter radius parameter is discoverable and documented exactly like other `GAMEBOT_*` parameters, with no new UI surface.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Documentation and final validation.

- [ ] T028 [P] Add `CHANGELOG.md` entry documenting the tap-point jitter feature, the `GAMEBOT_TAP_JITTER_RADIUS_PX` configuration parameter, and its default
- [ ] T029 Run full regression: `dotnet build -c Debug && dotnet test -c Debug` — all tests pass, 0 warnings, 0 errors
- [ ] T030 Validate `quickstart.md` scenarios manually: default jitter (±5px) varies dispatched coordinates run-to-run; `GAMEBOT_TAP_JITTER_RADIUS_PX=0` disables jitter; execution log shows target vs. executed coordinates

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately
- **Foundational (Phase 2 / US2 config+helper)**: Depends on Phase 1 — BLOCKS Phase 3
- **Dispatch-Time Jitter (Phase 3 / US1)**: Depends on Phase 2 (needs `AppConfig.TapJitterRadiusPx` and `CoordinateJitter`)
- **Configurability Verification (Phase 4 / US2)**: Depends on Phase 3 (needs jitter applied in `SendInputsAsync` to verify against)
- **UI/Docs Visibility (Phase 5 / US3)**: Depends on Phase 2 only (needs the config property to exist in `AppConfig`/snapshot); independent of Phase 3/4
- **Polish (Phase 6)**: Depends on all prior phases

### User Story Dependencies

- **US2 (Configuration, foundational part)**: No dependencies on other stories — provides `TapJitterRadiusPx` and `CoordinateJitter`
- **US1 (Automatic Jitter)**: Depends on US2's foundational config/helper (Phase 2)
- **US2 (Configurability verification, Phase 4)**: Depends on US1 (Phase 3) for the dispatch-time application to verify
- **US3 (Visibility)**: Depends on US2's foundational config/helper (Phase 2) only

### Within Each Phase

- Tests MUST be written first and MUST fail before implementation
- Config/helper before dispatch logic
- Dispatch logic before outcome/log/DTO propagation
- Verify build + tests green at each checkpoint

### Parallel Opportunities

Within Phase 2:
- T002, T003 (tests) and T004, T005 (implementation) can all proceed in parallel [P] — different files, no interdependency until T006/T007 wire them together

Within Phase 3:
- T009, T010, T011 (all tests) can be written in parallel [P]
- T018, T019 (DTO endpoint mappings) can be done in parallel [P] after T012/T017

Within Phase 4:
- T021 and T022 edit the same test file (`SessionManagerJitterTests.cs`) and are therefore sequential (no [P])

Within Phase 5:
- T024 (test) and T026 (docs) can be done in parallel [P]

Within Phase 6:
- T028 can be done in parallel [P] with final verification

---

## Parallel Example: Phase 2 (Foundational)

```bash
# Launch foundational tests and implementation together:
Task: "Add tests/unit/Domain/CoordinateJitterTests.cs"
Task: "Add AppConfig validation tests to tests/unit/Config/AppConfigValidationTests.cs"
Task: "Implement CoordinateJitter in src/GameBot.Domain/Services/CoordinateJitter.cs"
Task: "Add TapJitterRadiusPx property to src/GameBot.Domain/Config/AppConfig.cs"
```

---

## Implementation Strategy

### MVP Scope

The MVP is **Phase 1 + Phase 2 + Phase 3** (US2 foundational config/helper + US1 automatic dispatch-time jitter). This delivers the core value: every tap and swipe lands within a configurable radius of its target, with full pre/post-jitter reporting.

### Incremental Delivery

1. **Increment 1** (Phases 1-2): Config property and jitter helper exist and are validated — no dispatch behaviour change yet
2. **Increment 2** (Phase 3): Jitter actively applied to every tap/swipe dispatch; outcomes/logs report target vs. executed coordinates
3. **Increment 3** (Phase 4): Configurability (including disable) verified end-to-end
4. **Increment 4** (Phase 5): Parameter documented and visible in the UI configuration list
5. **Increment 5** (Phase 6): Documentation and full regression

### Risk Mitigation

- **Lowest risk**: `AppConfig`/`CoordinateJitter` additions (Phase 2) are additive and backward-compatible
- **Medium risk**: `SessionManager.SendInputsAsync` change (Phase 3, T013) modifies the hot dispatch path — mitigated by `SessionManagerJitterTests` covering tap, swipe, radius=0, and clamping
- **Low risk**: Outcome/DTO/log additions (T012, T014-T019) are additive new fields; existing `ResolvedPoint`-based consumers and `RecordingSessionManager`-based tests are unaffected since jitter only applies in the real `SessionManager`
