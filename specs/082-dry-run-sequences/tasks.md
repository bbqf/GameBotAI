---

description: "Task list for Dry-Run / Validate-Only Sequence Mode"
---

# Tasks: Dry-Run / Validate-Only Sequence Mode

**Input**: Design documents from `/specs/082-dry-run-sequences/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md,
contracts/dry-run-sequences.md, quickstart.md

**Tests**: Included — Constitution Principle II requires tests for all executable
logic and a failing-test-first approach, mirroring the precedent set by features
080/081.

**Organization**: Tasks are grouped by user story (US1 = dry-run create/validate,
US2 = dry-run execute). The two stories touch entirely disjoint files (US1:
`SequencesEndpoints.CreateSequenceAsync` + `SequenceUpsertContract`; US2:
`SequenceRunner`, `CommandDispatchOutcome`, `SequenceExecutionService`,
`ISequenceExecutionService`, `SequenceExecuteContract`,
`SequencesEndpoints.ExecuteSequenceAsync`) and can be implemented fully
independently, in either order or in parallel.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1/US2)

## Phase 1: Setup

No new project, dependency, or tooling setup is required — this feature adds no
new projects and no new third-party dependencies (plan.md Technical Context).

## Phase 2: Foundational

None strictly blocking. US1 and US2 touch disjoint files and can proceed
independently or in parallel — there is no shared plumbing to build first.

---

## Phase 3: User Story 1 - Validate a candidate sequence body without creating it (Priority: P1) 🎯 MVP

**Goal**: `POST /api/sequences` (per-step request shape) accepts `dryRun: true`,
runs the same enrichment/validation a real create runs, and never persists a
sequence — success or failure.

**Independent Test**: Submit a structurally valid per-step body with
`dryRun: true`; confirm `200 OK` with `{ valid: true, dryRun: true, errors: [] }`
and that no matching sequence appears in a subsequent list. Submit an invalid
body (nested `Loop`, or a dangling `commandId`) with `dryRun: true`; confirm the
same error a non-dry-run create would give, and still nothing persisted.

### Tests for User Story 1 ⚠️

> Write these first; confirm they FAIL against current code before implementing.

- [X] T001 [P] [US1] Add a failing integration test in `tests/integration/Sequences/SequenceCreateDryRunIntegrationTests.cs` (new file) asserting `POST /api/sequences` with a structurally valid per-step body and `dryRun: true` returns `200 OK` with body `{ "valid": true, "dryRun": true, "errors": [] }`, and that `GET /api/sequences` afterward does not include a sequence with the submitted name (spec US1 Acceptance Scenario 1)
- [X] T002 [P] [US1] Add a failing integration test in the same file asserting a per-step body containing a `Loop` nested inside another `Loop`'s body, submitted with `dryRun: true`, returns the identical `400 Bad Request` error a non-dry-run create returns for the same body (compare against `SequenceStepValidationService`'s existing nested-loop rejection message), and nothing is persisted (spec US1 Acceptance Scenario 2)
- [X] T003 [P] [US1] Add a failing integration test in the same file asserting a per-step body whose `command` step's `commandId` does not resolve to an existing command, submitted with `dryRun: true`, returns the identical `400 Bad Request` reference-resolution error a non-dry-run create returns, and nothing is persisted (spec US1 Acceptance Scenario 3)
- [X] T004 [P] [US1] Add a regression test in the same file confirming the existing per-step create suite (`tests/contract/Sequences/*ContractTests.cs`, `tests/integration/Sequences/SequenceCommandIdValidationIntegrationTests.cs`) is unaffected: a body that omits `dryRun` (or sets it `false`) still returns `201 Created` and persists a sequence exactly as today (spec FR-011)

### Implementation for User Story 1

- [X] T005 [US1] In `src/GameBot.Service/Models/SequenceStepContracts.cs`, add `public bool? DryRun { get; init; }` (JSON `dryRun`) to `SequenceUpsertContract`
- [X] T006 [US1] In `src/GameBot.Service/Endpoints/SequencesEndpoints.cs`'s `CreateSequenceAsync`, in the per-step branch (after `ValidatePerStepForPersistenceAsync` and `ValidateSequenceParametersAsync` both pass, immediately before `repo.CreateAsync` at line ~83), add: when `perStepRequest.DryRun == true`, return `Results.Ok(new { valid = true, dryRun = true, errors = Array.Empty<string>() })` instead of calling `repo.CreateAsync`/`Results.Created` — validation failures earlier in the method are already returned unchanged before this point, so no other branch needs to change

**Checkpoint**: Run T001-T004 — all must now pass. User Story 1 is independently
complete and testable; a sequence author can validate any candidate per-step body
without ever persisting one.

---

## Phase 4: User Story 2 - Exercise an existing sequence's structure without touching the emulator (Priority: P2)

**Goal**: `POST /api/sequences/{id}/execute` accepts `dryRun: true`, walks the
sequence's real step tree (control flow genuinely evaluated), but skips every
step that would dispatch to the emulator, start/use a session, or read live
capture state, reporting `skipped_dry_run` for each — while a `commandId` that
does not resolve to a real command still fails loudly, exactly as today.

**Independent Test**: Execute an existing sequence containing a primitive tap
step and a `Loop` with a parameter-gated `Break`, with `dryRun: true` and no
session running; confirm the call succeeds, the tap step's outcome is
`skipped_dry_run`, and the `Loop`'s `exitReason` correctly reflects the `Break`.

### Tests for User Story 2 ⚠️

> Write these first; confirm they FAIL against current code before implementing.

- [X] T007 [P] [US2] Add a failing unit test in `tests/unit/Sequences/SequenceRunnerDryRunTests.cs` (new file) asserting that with `dryRun: true`, a primitive `tap` step's `StepResult` has `Status: "Succeeded"` and `ActionOutcome: "skipped_dry_run"`, and that the test's `actionDispatcher` spy delegate is never invoked
- [X] T008 [P] [US2] Add a failing unit test in the same file asserting that with `dryRun: true`, a `command`-referencing step whose `commandId` **resolves** to a real command — including one authored with `RequireDispatch: true` — reports `ActionOutcome: "skipped_dry_run"`, does **not** fail the step or the sequence, and the test's `commandDispatcher` spy never reports a real dispatch (spec contract invariant 6 — dry-run never trips the `requireDispatch` miss-check)
- [X] T009 [P] [US2] Add a failing unit test in the same file asserting that with `dryRun: true`, a `reschedule-self` action step reports `ActionOutcome: "skipped_dry_run"` and the `actionDispatcher` spy is never invoked for it (distinct from its normal `noop`/`scheduled` outcomes)
- [X] T010 [P] [US2] Add a failing unit test in the same file asserting that with `dryRun: true`, a dedicated `waitForImage` action step reports `ActionOutcome: "skipped_dry_run"` and the test's `conditionEvaluator` spy is never invoked for it
- [X] T011 [P] [US2] Add a failing unit test in the same file asserting that with `dryRun: true`, a `Loop` containing a `Break` gated by a `commandOutcome` condition (referencing a prior step's recorded outcome, not live device state) still reports the correct `ExitReason` (`BrokeVia`/`ExhaustedMaxIterations`) for both a break-firing run and an iterations-exhausted run — matching what a real (non-dry-run) run with equivalent inputs would report (spec US2 Acceptance Scenarios 2-3)
- [X] T012 [P] [US2] Add a failing unit test in the same file asserting that a `commandOutcome` condition referencing a step whose own outcome was `skipped_dry_run` (because dry-run skipped it) evaluates to `false` regardless of `expectedState` (`success`/`failed`/`skipped`/`break`/`no_break`) — spec Edge Cases
- [X] T013 [P] [US2] Add a failing integration test in `tests/integration/Sequences/SequenceExecuteDryRunIntegrationTests.cs` (new file, shared with T015-T016) asserting that a sequence with a step gated by a per-step `imageVisible` condition, executed with `dryRun: true` and **no session running**, returns `200 OK`/`status: "Succeeded"` with that step reported `skipped`/not failed — proving the `conditionEvaluator` wrapper (only reachable through the real `SequenceExecutionService`, not a hand-built `SequenceRunner` test) never attempts a live capture read (spec FR-015)
- [X] T014 [P] [US2] Merged into T016 — `SequenceExecutionService.DispatchCommandAsync`'s constructor pulls in ten collaborators with no existing hand-built-fake precedent in this repo (only `WebApplicationFactory`-based integration tests exercise it); the real command-repository-backed existence check and the "never reaches `CommandExecutor`" guarantee are both provable at the integration level with far less setup, so T016 covers this directly instead of a redundant unit-level double
- [X] T015 [P] [US2] Add a failing integration test in `tests/integration/Sequences/SequenceExecuteDryRunIntegrationTests.cs` (new file) that creates a sequence containing a primitive tap step via the real HTTP path, then `POST`s `{sequenceId}/execute` with `{ "dryRun": true }` and **no session running**, asserting `200 OK`, `status: "Succeeded"`, and the tap step's `actionOutcome: "skipped_dry_run"` (spec US2 Acceptance Scenario 1; proves FR-009 end-to-end)
- [X] T016 [P] [US2] Add a failing integration test in the same file that creates a sequence whose `command` step references a `commandId`, deletes that command, then executes with `dryRun: true`, asserting the same real reference-resolution error a non-dry-run execution reports (spec US2 Acceptance Scenario 4 / FR-010, end-to-end)
- [X] T017 [P] [US2] Add a regression test confirming the existing `tests/integration/Sequences/EmptyStateExecuteSequenceIntegrationTests.cs` suite is unaffected: a tap-step sequence executed with `dryRun` omitted (or `false`) and no session still fails with the existing "no session available" error, unchanged (spec US2 Acceptance Scenario 5 / FR-014)

### Implementation for User Story 2

- [X] T018 [US2] In `src/GameBot.Domain/Services/SequenceRunner.cs`, add a `DryRunOutcomes` static class alongside the existing `BreakOutcomes`, with `public const string SkippedDryRun = "skipped_dry_run";`
- [X] T019 [US2] In `src/GameBot.Domain/Services/CommandDispatchOutcome.cs`, add `public bool SkippedDryRun { get; init; }` (default `false`) to the `CommandDispatchOutcome` record, alongside the existing `Dispatched`/`Reason`
- [X] T020 [US2] In `src/GameBot.Domain/Services/SequenceRunner.cs`, add `bool dryRun = false` as a new trailing optional parameter (before `ct`) to the public `ExecuteAsync` overloads and thread it through the private recursive helpers that need to observe it (`ExecuteLoopStepAsync`, `ExecuteIfStepAsync`, `ExecuteCountLoopAsync`, `ExecuteWhileLoopAsync`, `ExecuteRepeatUntilLoopAsync`, `ExecuteLoopBodyAsync`, `ExecuteSingleStepAsync`) — purely mechanical parameter passthrough, no behavior change yet
- [X] T021 [US2] In `ExecuteSingleStepAsync`, insert one dry-run gate immediately after the existing `Gate`/delay handling and immediately before the `IsWaitForImageStep` check (~line 605), covering **only** wait-for-image, `reschedule-self`, and `IsDispatchedPrimitiveAction` steps: when `dryRun` is `true`, skip those blocks entirely, call `result.AddStep(stepKey, appliedDelay, "Succeeded", conditionType: ..., conditionResult: ..., actionOutcome: DryRunOutcomes.SkippedDryRun, message: "dry run: step was not dispatched")`, set `stepOutcomes[stepKey] = DryRunOutcomes.SkippedDryRun` when `stepKey` is non-blank, and `return false`. **Do not** intercept the command-referencing fallback path (~line 678) here — leave it flowing into `commandDispatcher` unchanged
- [X] T022 [US2] In the same file's command-fallback handling (~line 715, the `if (!cmdDispatch.Dispatched)` block), add a case checked first: when `cmdDispatch.SkippedDryRun` is `true`, call `result.AddStep(step.CommandId, appliedDelay, "Succeeded", ..., actionOutcome: DryRunOutcomes.SkippedDryRun, message: "dry run: command was not dispatched")`, set `stepOutcomes[stepKey] = DryRunOutcomes.SkippedDryRun`, and `return false` — **before** the existing `RequireDispatch` miss-check, so a dry-run-skipped command step never trips it
- [X] T023 [US2] In `src/GameBot.Service/Services/SequenceExecution/ISequenceExecutionService.cs`, add `bool dryRun = false` as a new trailing optional parameter (before `ct`) to the scope-taking `ExecuteAsync` overload
- [X] T024 [US2] In `src/GameBot.Service/Services/SequenceExecution/SequenceExecutionService.cs`, accept `dryRun` on `ExecuteAsync`/`ExecuteCoreAsync`, pass it through to `_runner.ExecuteAsync(..., dryRun: dryRun, ct)`, and wrap the `conditionEvaluator` lambda (built in `ExecuteCoreAsync`, ~lines 257-281) so that when `dryRun` is `true`, an `image`- or `text`-sourced condition returns `Task.FromResult(false)` immediately without calling `EvaluateImageConditionAsync` or `_evalSvc.Evaluate`
- [X] T025 [US2] In the same file's `DispatchCommandAsync` local function (~line 208, which already closes over `dryRun` once T024 makes it a parameter of the enclosing method), add at the top: when `dryRun` is `true`, look up `commandId` via `_commandRepository.GetAsync(commandId, ct)`; if `null`, throw `new InvalidOperationException($"Command '{commandId}' was not found; the sequence step references a missing command.")` (the exact existing message text); otherwise return `new CommandDispatchOutcome(false, null, SkippedDryRun: true)` **without** calling `_commandExecutor.ForceExecuteDetailedAsync`
- [X] T026 [US2] In `src/GameBot.Service/Models/SequenceStepContracts.cs`, add `public bool? DryRun { get; init; }` (JSON `dryRun`) to `SequenceExecuteContract`
- [X] T027 [US2] In `src/GameBot.Service/Endpoints/SequencesEndpoints.cs`'s `ExecuteSequenceAsync`, read `body?.DryRun ?? false` alongside the existing `sessionId`/`suppliedParameters` reads, and pass it to `sequenceExecution.ExecuteAsync(sequenceId, sessionId, parentContext: null, scope, dryRun, ct)`

**Checkpoint**: Run T007-T017 — all must now pass. User Stories 1 AND 2 both
work independently; a sequence author can dry-run both create and execute
without any emulator/session activity, and a genuinely stale `commandId` still
fails loudly either way.

---

## Phase 5: Polish & Cross-Cutting Concerns

- [X] T028 Update `docs/architecture.md` — add a new subsection (alongside "Break
  & loop execution and the execution-log status vocabulary") documenting the
  `dryRun` option on create/execute, its exact skip scope (including the
  command-existence-check carve-out and the image-reference limitation),
  and the `skipped_dry_run` outcome, refreshing the "Last reviewed" date
  (Constitution Principle V)
- [X] T029 Run `dotnet build` and the full `dotnet test` suite (unit +
  integration + contract projects) and confirm zero failures/regressions
- [X] T030 Attempted against a locally launched `GameBot.Service`; it picked up
  the real production `GAMEBOT_DATA_DIR` (no isolation available through the
  launch tool used) and was stopped immediately after a read-only `GET
  /api/sequences` confirmed it was live production data, before any
  create/execute request. No further manual run was attempted. Equivalent
  coverage — same real ASP.NET Core pipeline, isolated per-test temp data
  directories — is provided instead by the integration tests added in T001-T003
  and T013/T015-T017, which exercise every quickstart scenario end-to-end
- [X] T031 Update spec.md's Status line to `Implemented` once all tasks above are
  verified complete

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup / Foundational**: None — skipped, no shared prerequisites.
- **User Story 1 (Phase 3)**: Fully independent — touches only
  `SequencesEndpoints.CreateSequenceAsync`'s per-step branch and
  `SequenceUpsertContract`, disjoint from US2's files.
- **User Story 2 (Phase 4)**: Fully independent of US1 — touches
  `SequenceRunner`, `CommandDispatchOutcome`, `SequenceExecutionService`,
  `ISequenceExecutionService`, `SequenceExecuteContract`, and
  `SequencesEndpoints.ExecuteSequenceAsync`.
- **Polish (Phase 5)**: Depends on both user stories being complete.

### Within Each User Story

- Tests (T001-T004 / T007-T017) MUST be written and confirmed FAILING before
  their story's implementation tasks.
- T005 before T006 (US1): the request-contract field must exist before the
  endpoint can read it.
- T018-T020 before T021 (US2): the outcome constant, the `CommandDispatchOutcome`
  field, and the `dryRun` parameter threaded through every signature all need
  to exist before the gate that uses them. T021 before T022 (the leaf-dispatch
  gate lands before the command-fallback's `SkippedDryRun` case — both are in
  the same method but T022 depends on T019's new record field). T023 before
  T024 (interface signature before its implementation). T024 before T025
  (`DispatchCommandAsync` needs `dryRun` in scope, which T024 introduces on the
  enclosing method). T026 before T027 (contract field before the endpoint reads
  it). T020-T025 all land before T015-T016's integration tests can pass, since
  the HTTP path runs through all of them.

### Parallel Opportunities

- T001-T004 and T007-T017 are each independently parallelizable within their
  story (distinct test files or distinct assertions), except where a task
  explicitly reuses a file a preceding task in this list created — author those
  together rather than truly concurrently to avoid merge conflicts in one new
  file.
- User Story 1 (T001-T006) and User Story 2 (T007-T027) can proceed fully in
  parallel — no shared files.

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. T001-T006 (US1). **STOP and VALIDATE**: run the new tests, confirm no
   regression in the existing per-step create suite. This alone already
   delivers FR-002's specifically evidenced cost — no more throwaway
   create-run-inspect-delete cycles for a purely structural question.

### Incremental Delivery

1. US1 (T001-T006) → validate → dry-run create ships, independently useful.
2. US2 (T007-T027) → validate → extends the same courtesy to execute, so
   `Loop`/`Break` branching mechanics can be verified without an emulator.
3. Phase 5 polish once both are in.

## Notes

- US1 and US2 are deliberately disjoint at the file level (per research.md
  Unknown 1/2's choice of choke points) so either can ship alone without the
  other half existing.
- US2's implementation has one deliberate asymmetry: three step families
  (wait-for-image, reschedule-self, dispatched primitive actions) are
  blanket-skipped by one gate (T021), while the command-referencing family
  gets its own narrower existence-check carve-out (T019, T022, T025) one layer
  down — a blanket skip there would have silently swallowed the existing
  stale-`commandId` error path, which FR-010 requires to survive `dryRun`. No
  changes are needed in `CommandExecutor`, `FileSequenceRepository`, or
  `SequenceStepValidationService` — the chosen choke points make
  `CommandExecutor` unreachable for a dry-run command step either way (found
  or not), and create-time dry-run reuses existing validation unchanged.
- Commit after each user story's checkpoint, not after every individual task.
