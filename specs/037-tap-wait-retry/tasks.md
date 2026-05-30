# Tasks: Tap Wait-and-Retry Before Execution

**Input**: Design documents from `/specs/036-tap-wait-retry/`
**Prerequisites**: plan.md (required), spec.md (required), research.md, data-model.md, contracts/

**Tests**: Required per constitution (Principle II: Testing Standards).

**Organization**: Tasks are grouped by user story. US3 (configuration) is foundational — US1 and US2 depend on it.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup

**Purpose**: No new projects or directories needed. This phase verifies the build is green before changes begin.

- [X] T001 Verify build and all 489 tests pass (`dotnet build -c Debug && dotnet test -c Debug`)

---

## Phase 2: Foundational — Configuration (US3, Priority: P2) 🎯 Prerequisite

**Purpose**: Introduce the three configuration properties that the retry loop depends on. Must be complete before US1/US2 work begins.

**Goal**: AppConfig exposes CaptureIntervalMs, TapRetryCount, TapRetryProgression with env-var wiring and validation.

**Independent Test**: Startup with/without env vars produces correct AppConfig values; invalid values fall back to defaults.

### Tests for Configuration

- [X] T002 [P] [US3] Add unit tests for AppConfig default values and validation in `tests/unit/Config/AppConfigValidationTests.cs` — test default CaptureIntervalMs=500, TapRetryCount=3, TapRetryProgression=1.0; test negative TapRetryCount falls back to 3; test zero/negative TapRetryProgression falls back to 1.0; test CaptureIntervalMs clamped to ≥50

### Implementation for Configuration

- [X] T003 [US3] Add CaptureIntervalMs, TapRetryCount, TapRetryProgression properties to `src/GameBot.Domain/Config/AppConfig.cs` with defaults (500, 3, 1.0) and XML doc comments per data-model.md
- [X] T004 [US3] Wire new AppConfig properties from env vars in `src/GameBot.Service/Program.cs` — parse GAMEBOT_CAPTURE_INTERVAL_MS (reuse existing local var), GAMEBOT_TAP_RETRY_COUNT, GAMEBOT_TAP_RETRY_PROGRESSION with validation and fallback per R-003; also feed captureIntervalMs from the same parsed value to BackgroundScreenCaptureService registration
- [X] T005 [US3] Verify build passes and T002 tests are green

**Checkpoint**: AppConfig now carries all three retry config values. Retry loop implementation can begin.

---

## Phase 3: User Story 1 — Tap Wait-and-Retry Loop (Priority: P1) 🎯 MVP

**Goal**: Primitive tap steps wait for the target image to appear, retrying up to COUNT times with WAIT_TIME delays, before executing the tap or failing.

**Independent Test**: A primitive tap step with a detection target waits, detects on Nth attempt, and taps; or exhausts retries and fails; or is cancelled immediately.

### Tests for User Story 1

- [X] T006 [P] [US1] Add unit test: image found on first attempt (after initial wait) — tap executes with status "executed" and null reason, in `tests/unit/Commands/CommandExecutorPrimitiveTapTests.cs`
- [X] T007 [P] [US1] Add unit test: image found on 3rd attempt — tap executes with status "executed" and reason "detected_after_2_retries", in `tests/unit/Commands/CommandExecutorPrimitiveTapTests.cs`
- [X] T008 [P] [US1] Add unit test: image never found, COUNT=3 — step fails with status "skipped_detection_failed" and reason "detection_failed_after_3_retries", in `tests/unit/Commands/CommandExecutorPrimitiveTapTests.cs`
- [X] T009 [P] [US1] Add unit test: cancellation during wait — step reports status "cancelled" and reason "cancelled_during_retry_N", in `tests/unit/Commands/CommandExecutorPrimitiveTapTests.cs`
- [X] T010 [P] [US1] Add unit test: COUNT=0 — single detection check, no retries; if not found, fails immediately with reason "detection_failed_after_0_retries", in `tests/unit/Commands/CommandExecutorPrimitiveTapTests.cs`
- [X] T011 [P] [US1] Add integration test: end-to-end retry with mock screenshot source returning image on 2nd call, in `tests/integration/Commands/PrimitiveTapExecutionIntegrationTests.cs`
- [X] T023 [P] [FR-012] Add regression test: primitive tap step without a detection target is unaffected by retry logic — still skips with existing behaviour, in `tests/unit/Commands/CommandExecutorPrimitiveTapTests.cs`

### Implementation for User Story 1

- [X] T012 [US1] Add source-generated `[LoggerMessage]` methods to the Log static class in `src/GameBot.Service/Services/CommandExecutor.cs` — TapRetryWaiting (Debug), TapRetryDetected (Info), TapRetryNotDetected (Debug), TapRetryExhausted (Warning), TapRetryCancelled (Info) per R-006
- [X] T013 [US1] Inject AppConfig into CommandExecutor constructor in `src/GameBot.Service/Services/CommandExecutor.cs` — add `_appConfig` field, receive via DI, read CaptureIntervalMs/TapRetryCount/TapRetryProgression
- [X] T014 [US1] Implement retry loop in PrimitiveTap handling block of `src/GameBot.Service/Services/CommandExecutor.cs` — move template lookup before loop (R-005); perform initial wait + detection check, then wrap retry cycles in `for` loop with `Task.Delay(currentWaitMs, ct)` followed by `currentWaitMs *= progression` (progression applied after each retry wait, not after initial check); handle OperationCanceledException for immediate cancellation (FR-013); encode retry metadata in Reason field per R-004
- [X] T015 [US1] Verify build passes and all tests (T006–T011 plus existing 489) are green

**Checkpoint**: Primitive tap steps now wait and retry. US1 acceptance scenarios 1, 2, 3 are satisfied. Cancellation works.

---

## Phase 4: User Story 2 — Progressive Wait Time (Priority: P2)

**Goal**: Wait time between retries increases by the PROGRESSION multiplier each cycle.

**Independent Test**: With PROGRESSION=2, verify via logs/test assertions that wait times double each cycle.

> **Note**: The progression math (`currentWaitMs *= progression`) is implemented as part of the retry loop in T014. This phase adds dedicated tests to verify the progression behaviour and edge cases.

### Tests for User Story 2

- [X] T016 [P] [US2] Add unit test: PROGRESSION=2, WAIT_TIME=500, COUNT=3 — verify retry wait times are 500, 1000, 2000 ms (excluding the initial check wait), in `tests/unit/Commands/CommandExecutorPrimitiveTapTests.cs`
- [X] T017 [P] [US2] Add unit test: PROGRESSION=1 (default) — verify all wait intervals are equal (500ms each), in `tests/unit/Commands/CommandExecutorPrimitiveTapTests.cs`
- [X] T018 [P] [US2] Add unit test: invalid PROGRESSION (0 or negative) falls back to 1.0 — verify constant wait intervals, in `tests/unit/Commands/CommandExecutorPrimitiveTapTests.cs`

**Checkpoint**: Progression logic is verified. US2 acceptance scenarios 1 and 2 are satisfied.

---

## Phase 5: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, changelog, and final validation.

- [X] T019 [P] Add CHANGELOG.md entry documenting tap wait-and-retry feature, new configuration parameters, and their defaults
- [X] T020 [P] Add a class-level XML doc comment on `AppConfig` in `src/GameBot.Domain/Config/AppConfig.cs` describing the retry algorithm interaction (WAIT_TIME × PROGRESSION^N formula, state machine overview) — supplements the individual property doc comments added in T003
- [X] T021 Run full regression: `dotnet build -c Debug && dotnet test -c Debug` — all tests pass, 0 warnings, 0 errors
- [X] T022 Validate quickstart.md scenarios manually: default config works out of the box; overriding env vars changes retry behaviour

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately
- **Configuration (Phase 2 / US3)**: Depends on Phase 1 — BLOCKS Phase 3 and 4
- **Retry Loop (Phase 3 / US1)**: Depends on Phase 2 (needs AppConfig properties)
- **Progression (Phase 4 / US2)**: Depends on Phase 3 (progression is part of retry loop; Phase 4 adds verification tests)
- **Polish (Phase 5)**: Depends on all prior phases

### User Story Dependencies

- **US3 (Configuration)**: Foundational — no dependencies on other stories
- **US1 (Retry Loop)**: Depends on US3 (needs config values to parameterize loop)
- **US2 (Progression)**: Depends on US1 (progression is embedded in retry loop logic)

### Within Each Phase

- Tests MUST be written first and MUST fail before implementation
- Models/config before services
- Services/logic before wiring
- Verify build + tests green at each checkpoint

### Parallel Opportunities

Within Phase 2:
- T002 (tests) can be written in parallel with understanding T003-T004, but must be committed first

Within Phase 3:
- T006–T011, T023 (all tests) can be written in parallel [P]
- T012 (log methods) and T013 (DI injection) can be done in parallel [P] before T014

Within Phase 4:
- T016–T018 (all tests) can be written in parallel [P]

Within Phase 5:
- T019 and T020 can be done in parallel [P]

---

## Implementation Strategy

### MVP Scope

The MVP is **Phase 1 + Phase 2 + Phase 3** (US3 config + US1 retry loop). This delivers the core value: primitive taps wait for images before executing, with configurable retry count. Progression defaults to 1.0 (constant intervals), which is fully functional.

### Incremental Delivery

1. **Increment 1** (Phases 1-2): Configuration properties wired and validated — no behaviour change yet
2. **Increment 2** (Phase 3): Retry loop active — full tap wait-and-retry with cancellation support
3. **Increment 3** (Phase 4): Progression edge cases verified — confidence in backoff behaviour
4. **Increment 4** (Phase 5): Documentation and regression — feature complete

### Risk Mitigation

- **Lowest risk**: AppConfig changes (Phase 2) are additive and backward-compatible
- **Medium risk**: Retry loop (Phase 3) modifies the hot path in CommandExecutor — mitigated by comprehensive unit + integration tests
- **No risk**: Progression (Phase 4) adds only test verification — the math is a single multiplication already in the loop
