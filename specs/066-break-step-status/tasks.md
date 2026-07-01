---
description: "Task list for Break Step Success/Failure Execution Statuses"
---

# Tasks: Break Step Success/Failure Execution Statuses

**Input**: Design documents from `/specs/066-break-step-status/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/break-step-outcome.md, quickstart.md

**Tests**: INCLUDED. The Constitution (Testing Standards) and the two behavior reversals in this
feature (error → no-break; false → `no_break` instead of `Skipped`) require failing-first tests
that reproduce/pin the new behavior before the code changes.

**Organization**: Tasks are grouped by the two P1 user stories from spec.md. Both stories touch
the same file (`SequenceRunner.cs`) in adjacent branches, so US1 and US2 are sequenced (not run in
parallel by different people on that file); tasks in different files are marked [P].

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1 or US2 (Setup / Foundational / Polish carry no story label)

## Path Conventions

Web application, existing four-project layout: `src/GameBot.Domain/`, `src/GameBot.Service/`,
`src/web-ui/src/`, and `tests/` at repo root.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Establish a known-green baseline before changing behavior (release-blocker gate).

- [ ] T001 Establish baseline: run `dotnet test "C:\src\GameBot\GameBot.sln" --filter "FullyQualifiedName~SequenceRunnerLoopTests|FullyQualifiedName~ExecutionLog"` and `npm --prefix "C:\src\GameBot\src\web-ui" run build` + `npm --prefix "C:\src\GameBot\src\web-ui" test`; record that they pass (or note the pre-existing lint/`tsc` failures are excluded from the gate per project memory).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The single canonical break outcome vocabulary shared by BOTH break mechanisms and by the display mapping.

**⚠️ CRITICAL**: Both user stories depend on these tokens existing.

- [ ] T002 Add canonical break outcome tokens `break` and `no_break` as a small internal/public static holder in `src/GameBot.Domain/Services/SequenceRunner.cs` (e.g. a `BreakOutcomes` static class alongside the runner) so the loop-body break step, the loop-level `breakOn` end-state, and `GameBot.Service` mapping all reference one source of truth instead of magic strings.

**Checkpoint**: Outcome tokens available — user story work can begin.

---

## Phase 3: User Story 1 - Break reflects whether it broke (Priority: P1) 🎯 MVP

**Goal**: Each break's own outcome reads as a **success** when it fired and as a distinct neutral **"No break"** when a conditional break did not fire — never `Skipped`, never red `Failed`.

**Independent Test**: Author a loop with one conditional break; run once with the condition satisfied and once not. The break step shows `break`/success in the first run and a distinct "No break" badge in the second (verified via unit + web-ui tests, and the manual walkthrough in quickstart.md).

### Tests for User Story 1 (write first, ensure they FAIL) ⚠️

- [ ] T003 [P] [US1] Unit test: conditional break with condition TRUE records `StepResult.Status="Succeeded"`, `ActionOutcome="break"`, `ConditionResult="true"`, and ends the iteration — in `tests/unit/Sequences/SequenceRunnerLoopTests.cs`
- [ ] T004 [P] [US1] Unit test: conditional break with condition FALSE records `ActionOutcome="no_break"` (asserts it is NOT `Skipped`/`continue`) and the loop continues to the next iteration — in `tests/unit/Sequences/SequenceRunnerLoopTests.cs`
- [ ] T005 [P] [US1] Unit test: unconditional ("Always break") step records `ActionOutcome="break"` success and ends the iteration — in `tests/unit/Sequences/SequenceRunnerLoopTests.cs`
- [ ] T006 [P] [US1] Unit test: `ExecutionLogService.MapStepStatus` maps `"break" → "success"` and `"no_break" → "no_break"` — in `tests/unit/ExecutionLogs/ExecutionLogServiceMapStepStatusTests.cs` (new file, alongside the existing `tests/unit/ExecutionLogs/` suite)
- [ ] T007 [P] [US1] web-ui Jest test: the execution-log grid renders a distinct neutral "No break" badge for a node whose status is `no_break` (asserts it is not the red `failure` styling and not `skipped`) — colocated in `src/web-ui/src/pages/__tests__/ExecutionLogs.noBreak.test.tsx` (new file)
- [ ] T026 [P] [US1] Unit test for FR-009 (loop-construct consistency): a break step hosted in a **while / do-while** step-loop (not a count loop) yields the same `break`/`no_break` outcomes as the count-loop cases, confirming the shared `ExecuteLoopBodyAsync` path applies uniformly — in `tests/unit/Sequences/SequenceRunnerLoopTests.cs`

### Implementation for User Story 1

- [ ] T008 [US1] In `ExecuteLoopBodyAsync` condition-FALSE branch, record the outcome as `no_break` (`Status="Succeeded"`, `ActionOutcome=no_break`, `ConditionResult="false"`, message retained per FR-007) instead of `Status="Skipped"`/`ActionOutcome="continue"`, keeping the fall-through to the next body step / iteration unchanged — in `src/GameBot.Domain/Services/SequenceRunner.cs` (~lines 1013-1017)
- [ ] T009 [US1] In the fired branches of `ExecuteLoopBodyAsync` (unconditional and condition-true), confirm/normalize `ActionOutcome=break` and `Status="Succeeded"` using the `BreakOutcomes` tokens — in `src/GameBot.Domain/Services/SequenceRunner.cs` (~lines 978-1010)
- [ ] T010 [US1] Extend `ExecutionLogService.MapStepStatus`: add cases `"break" => "success"` and `"no_break" => "no_break"` (fixes the current fall-through of `"break"` to `"failure"`) — in `src/GameBot.Service/Services/ExecutionLog/ExecutionLogService.cs` (~lines 475-482)
- [ ] T011 [US1] Verify the flat detail-item mapping in `SequenceExecutionService` passes `break`/`no_break` through unchanged (the `Skipped→"skipped"` fallback at ~lines 223-225 does not apply because break steps set `ActionOutcome` explicitly); adjust only if a break step would otherwise be mislabeled — in `src/GameBot.Service/Services/SequenceExecution/SequenceExecutionService.cs`
- [ ] T012 [P] [US1] Extend `ExecutionTreeNodeStatus` with `'no_break'` — in `src/web-ui/src/services/executionLogsApi.ts` (line ~36)
- [ ] T013 [US1] Render the `no_break` status in the log grid: surface the value in `src/web-ui/src/pages/executionLogGrid.ts`, give it a readable label/aria in `src/web-ui/src/pages/ExecutionLogs.tsx` (status cell, ~line 56), and add a distinct **neutral** badge style for the `execution-logs-row[data-status="no_break"]` / `execution-logs-cell-status` selectors in `src/web-ui/src/styles.css` (do not reuse the `failure`/red styling)

**Checkpoint**: A fired break shows success; a non-firing conditional break shows a distinct "No break" badge. MVP is demoable.

---

## Phase 4: User Story 2 - A non-firing break never taints the run (Priority: P1)

**Goal**: A break that does not fire (condition false, or condition/`breakOn` evaluation error) never marks the loop, sequence/run, or any ancestor as failed, never alters flow, and is excluded from failure counts — for both the discrete break step and the loop-level `breakOn`.

**Independent Test**: Run a loop that iterates several times with the break condition false (and one with a condition that errors) until a separate exit ends it; the loop and run report Succeeded and every iteration's work still ran (verified via unit + web-ui tests and quickstart.md).

### Tests for User Story 2 (write first, ensure they FAIL) ⚠️

- [ ] T014 [P] [US2] Rewrite `CountLoopBreakConditionThrowsLoopFails` → `CountLoopBreakConditionErrorRecordedAsNoBreakLoopContinues`: a break condition that throws is recorded as `no_break`, the loop continues, and the run/loop `Status` is `Succeeded` (asserts `result.Fail()` is NOT called) — in `tests/unit/Sequences/SequenceRunnerLoopTests.cs` (~line 434)
- [ ] T015 [P] [US2] Update `CountLoopConditionalBreakNeverTriggeredLoopRunsToCompletion` to also assert the run `Status="Succeeded"` and that each non-firing break is `no_break` (not `Skipped`) — in `tests/unit/Sequences/SequenceRunnerLoopTests.cs` (~line 374)
- [ ] T016 [P] [US2] Unit test: nested loops — a `no_break` on an inner break does not mark the inner loop, outer loop, or sequence as failed (run `Status="Succeeded"`) — in `tests/unit/Sequences/SequenceRunnerLoopTests.cs`
- [ ] T017 [P] [US2] Unit test for the loop-level `breakOn`: a `breakOn` whose evaluation throws does not propagate/fail the run and the loop continues; a `breakOn` that evaluates true ends the block with `Status="true"` — in `tests/unit/Sequences/SequenceRunnerWhileBreakOnTests.cs` (new file)
- [ ] T018 [P] [US2] web-ui Jest test: a run whose only non-success break outcomes are `no_break` renders the run/loop row as Succeeded (not failed) — colocated in `src/web-ui/src/pages/__tests__/ExecutionLogs.noBreak.test.tsx`

### Implementation for User Story 2

- [ ] T019 [US2] In the `ExecuteLoopBodyAsync` break-condition `catch`, record `no_break` (`Status="Succeeded"`, `ConditionResult="error"`, message includes the condition detail + error per FR-007) and return `(false, false, stepsExecuted)` to continue the loop — remove the `result.Fail(...)` call and the `earlyStop=true` return — in `src/GameBot.Domain/Services/SequenceRunner.cs` (~lines 994-1002)
- [ ] T020 [US2] Guard the loop-level `breakOn` evaluations: wrap the `breakOn-start` (~line 1254) and `breakOn-mid` (~line 1294) `conditionEvaluator` calls in `ExecuteWhileBlockAsync` so an exception is treated as `false` (no break) rather than propagating out and failing the run; a true result still ends the block with `Status="true"` — in `src/GameBot.Domain/Services/SequenceRunner.cs`
- [ ] T021 [US2] Confirm FR-008: `no_break` outcomes are excluded from failure counts/health/alerts (they key on `Failed` status / `failure` node status, which `no_break` never sets). Add/extend an assertion covering this and change code only if a counter would otherwise include `no_break` — in `tests/unit/ExecutionLogs/ExecutionLogServiceMapStepStatusTests.cs` (and `src/GameBot.Service/Endpoints/MetricsEndpoints.cs` only if a change is actually needed)

**Checkpoint**: Non-firing breaks (false + error) and unguarded `breakOn` errors leave the run green and the flow intact, across both break mechanisms.

---

## Phase 5: Polish & Cross-Cutting Concerns

- [ ] T022 [P] Update `docs/architecture.md`: break/loop execution behavior and the execution-log status vocabulary (add `break`/`no_break`), and refresh the "Last reviewed" date (Constitution Principle V)
- [ ] T023 [P] Add a `CHANGELOG.md` entry for the user-visible change (non-firing breaks now show a neutral "No break" state instead of `Skipped`, and a break-condition error no longer fails the run)
- [ ] T024 Update `specs/066-break-step-status/spec.md` `Status` line to Implemented and reconcile `specs/STATUS.md`; add an "iterated by 066" note to the `Status` of the earlier break/loop specs (014, 034, 042) if their described behavior is now changed (Constitution Principle V)
- [ ] T025 Run `specs/066-break-step-status/quickstart.md` validation end-to-end (backend `dotnet test` filters + `vite build` + `jest`, then the manual UI walkthrough)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: none — start immediately.
- **Foundational (Phase 2 / T002)**: depends on Setup; BLOCKS both stories.
- **User Story 1 (Phase 3)**: depends on T002. MVP.
- **User Story 2 (Phase 4)**: depends on T002; also depends on US1's T008/T009/T010 because it edits adjacent branches of the same `SequenceRunner.cs`/mapping and reuses the `no_break` rendering — do US1 first, then US2.
- **Polish (Phase 5)**: depends on US1 + US2 complete.

### Within Each User Story

- Write the story's tests (they FAIL) before its implementation.
- Domain outcome recording (T008/T009, T019/T020) before Service mapping consumption (T010) before web-ui rendering (T012/T013) — but tests come first.

### Parallel Opportunities

- **US1 tests**: T003, T004, T005, T006, T007, T026 are all [P] (independent files / independent assertions).
- **US2 tests**: T014, T015, T016, T017, T018 are all [P].
- **Cross-file impl within US1**: T012 (web-ui type) is [P] against the Domain/Service tasks.
- **Polish**: T022 and T023 are [P].
- US1 and US2 implementation are **not** parallel on `SequenceRunner.cs` (same file, adjacent branches).

---

## Parallel Example: User Story 1 tests

```bash
# Launch the US1 failing-first tests together:
Task: "Unit test conditional break TRUE → break/success in tests/unit/Sequences/SequenceRunnerLoopTests.cs"
Task: "Unit test conditional break FALSE → no_break (not Skipped) in tests/unit/Sequences/SequenceRunnerLoopTests.cs"
Task: "Unit test unconditional break → break/success in tests/unit/Sequences/SequenceRunnerLoopTests.cs"
Task: "Unit test MapStepStatus break→success, no_break→no_break in tests/unit/ExecutionLog/ExecutionLogServiceMapStepStatusTests.cs"
Task: "web-ui Jest: grid renders distinct No break badge in src/web-ui/src/pages/__tests__/ExecutionLogs.noBreak.test.tsx"
```

---

## Implementation Strategy

### MVP First (User Story 1)

1. Phase 1 Setup → 2. Phase 2 Foundational (T002) → 3. Phase 3 US1 → **STOP & VALIDATE**: a fired break reads as success and a non-firing conditional break reads as a distinct "No break" (no `Skipped`). Demoable.

### Incremental Delivery

1. Setup + Foundational → tokens ready.
2. US1 → representation correct (MVP).
3. US2 → non-influence + error/`breakOn` guarantees layered on the same paths.
4. Polish → docs, changelog, status lines, quickstart validation.

---

## Notes

- [P] = different files, no dependency on an incomplete task.
- Both user stories are P1 and tightly coupled through `SequenceRunner.cs`; sequence US1 → US2 to avoid same-file conflicts.
- The two behavior reversals (error → `no_break`; false → `no_break` not `Skipped`) MUST have failing-first tests (T004, T014) per the Constitution.
- Commit after each task or logical group; do not mark a phase complete on a red build/test (release-blocker gate).
