# Implementation Tasks: Evaluate-And-Execute Trigger Guard

## Dependencies & Execution Order

1. User Story 1 (Trigger-Gated Command Execution)
2. User Story 2 (Safe Handling for Pending Triggers)
3. User Story 3 (Deterministic Unit Coverage)
4. Polish & Telemetry Hardening

Parallel opportunities:
- Unit vs integration test updates can proceed concurrently once CommandExecutor changes are in place.
- Telemetry/logging polish can run in parallel with integration test enhancements if they touch different files.

MVP scope: Complete User Story 1 end-to-end (trigger evaluation before execution + satisfied path).

## Phase 1 – Setup

- [x] T001 Verify clean workspace and checkout `001-fix-trigger-evaluate`

## Phase 2 – Foundational

- [x] T002 Confirm existing repositories/services wiring for CommandExecutor in src/GameBot.Service/Services/CommandExecutor.cs
- [x] T003 Review integration test harness in tests/integration/CommandEvaluateAndExecuteTests.cs for current coverage gaps

## Phase 3 – User Story 1 (Trigger-Gated Command Execution)

**Goal**: Ensure Evaluate & Execute always evaluates the trigger prior to running command steps and persists satisfied metadata.
**Independent Test**: Call Evaluate & Execute with a trigger that should be satisfied and verify non-zero accepted inputs plus updated trigger timestamps.

- [x] T004 [US1] Update src/GameBot.Service/Services/CommandExecutor.cs to evaluate trigger before ForceExecuteAsync and persist satisfied state
- [x] T005 [US1] Extend logging/telemetry in src/GameBot.Service/Services/CommandExecutor.cs to emit execution outcome (executed vs skipped) with trigger status
- [x] T006 [US1] Add or update unit guard rails for satisfied trigger path in tests/unit/Commands/CommandExecutorTests.cs
- [x] T007 [US1] Enhance integration test positive scenario in tests/integration/CommandEvaluateAndExecuteTests.cs to assert trigger status and accepted count
- [x] T008 [US1] Document behavior in specs/001-fix-trigger-evaluate/quickstart.md (manual verification notes)

## Phase 4 – User Story 2 (Safe Handling for Pending Triggers)

**Goal**: Skip execution when trigger evaluates to pending/failed and leave cooldown metadata unchanged (aside from evaluation timestamp).
**Independent Test**: Configure delay trigger with pending result and confirm Evaluate & Execute returns zero accepted inputs and no session input activity.

- [x] T009 [US2] Adjust src/GameBot.Service/Services/CommandExecutor.cs to exit early when trigger evaluation returns Pending/Failed
- [x] T010 [US2] Add explicit log entry for skipped execution with reason in CommandExecutor
- [x] T011 [US2] Extend integration tests in tests/integration/CommandEvaluateAndExecuteTests.cs for pending path (assert zero accepted + persisted state unchanged)

## Phase 5 – User Story 3 (Deterministic Unit Coverage)

**Goal**: Provide lightweight unit tests exercising satisfied and pending trigger outcomes without needing the full service host.
**Independent Test**: Run new unit fixture; it should fail if Evaluate & Execute bypasses trigger evaluation or misorders persistence.

- [x] T012 [US3] Create tests/unit/Commands/CommandExecutorTests.cs fakes for ICommandRepository, IActionRepository, ISessionManager, ITriggerRepository, TriggerEvaluationService
- [x] T013 [US3] Implement satisfied-path unit test verifying ForceExecuteAsync invoked and return value propagated
- [x] T014 [US3] Implement pending-path unit test verifying ForceExecuteAsync not called and method returns zero
- [x] T015 [US3] Ensure unit tests assert trigger metadata persistence ordering (satisfied path updates before execution, pending leaves untouched)

## Phase 6 – Polish & Cross-Cutting

- [x] T016 Update docs/specs/001-fix-trigger-evaluate/plan.md with any deviations discovered during implementation
- [x] T017 Add telemetry/metrics validation steps to quickstart if new logging fields introduced
- [x] T018 Final test sweep: run `dotnet test -c Debug` and record results in specs/001-fix-trigger-evaluate/quickstart.md
- [x] T019 Prepare changelog/summary entry in CHANGELOG.md noting Evaluate & Execute trigger fix

## Implementation Strategy

1. Implement core trigger evaluation changes (US1) and keep integration tests passing.
2. Layer pending-trigger safeguards (US2) ensuring zero execution on pending state.
3. Add deterministic unit tests (US3) to guard regressions.
4. Finish with telemetry polish and documentation/test sweep.
