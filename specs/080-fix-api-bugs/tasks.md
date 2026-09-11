---

description: "Task list for Fix Sequence & Session-Input API Bugs"
---

# Tasks: Fix Sequence & Session-Input API Bugs

**Input**: Design documents from `/specs/080-fix-api-bugs/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/api-changes.md, quickstart.md

**Tests**: Included — Constitution Principle II requires a failing test reproducing
each bug before its fix.

**Organization**: Tasks are grouped by user story (US1 = B-003, US2 = B-005,
US3 = B-001). The three bugs live in non-overlapping files, so all three stories
are fully independent and can be implemented/tested/delivered in any order.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1/US2/US3)

## Phase 1: Setup

No new project, dependency, or tooling setup is required — this feature adds no
new projects and no new third-party dependencies (plan.md Technical Context).

## Phase 2: Foundational

None. The three bugs are isolated to disjoint files with no shared blocking
prerequisite; each user story below can start immediately.

---

## Phase 3: User Story 1 - Sequence authoring fails fast on a bad command reference (Priority: P1) 🎯 MVP

**Goal**: Reject, at creation/replace time, any `Command`-typed sequence step
(top-level or nested in `Loop`/`If`) whose payload lacks a resolvable `commandId`.

**Independent Test**: POST a sequence with one step whose payload has
`commandName` but no `commandId` and confirm `400` with no sequence persisted;
repeat with the same malformed step nested inside a `Loop` body.

### Tests for User Story 1 ⚠️

> Write these first; confirm they FAIL against current code before implementing.

- [X] T001 [P] [US1] Add failing unit test in `tests/unit/Sequences/` (new file `SequenceStepValidationServiceCommandIdTests.cs`) asserting `SequenceStepValidationService.Validate` returns a validation error for a top-level `Command`-typed step whose `Action.Parameters` has no `commandId`
- [X] T002 [P] [US1] Add failing unit test in the same file asserting the same validation error for a `Command`-typed step nested inside a `Loop` step's body, and separately inside an `If` step's body
- [X] T003 [P] [US1] Add failing integration test in `tests/integration/Sequences/` (new file `SequenceCommandIdValidationIntegrationTests.cs`) that POSTs to `/api/sequences` with a top-level step payload containing `commandName` but no `commandId`, asserting `400 Bad Request` with an error mentioning the step id, and that no sequence was persisted (follow-up `GET` confirms absence)
- [X] T004 [P] [US1] Add failing integration test in the same file for `PUT /api/sequences/{id}` (replace) with the same malformed nested-step payload, asserting `400 Bad Request`
- [X] T005 [P] [US1] Add a regression test in the same file confirming a request where every `Command`-typed step supplies a valid `commandId` still returns `201`/`204` (spec FR-004) — this test should already pass and must keep passing
- [X] T006 [P] [US1] Add a regression test in the same file confirming a step payload carrying **both** `commandName` and `commandId` validates successfully and dispatches using `commandId` (spec Edge Cases) — this test should already pass and must keep passing

### Implementation for User Story 1

- [X] T007 [US1] In `src/GameBot.Domain/Services/SequenceStepValidationService.cs`, extend `Validate`/`ValidateStepCondition` to reject a `Command`-typed action step (`ActionTypes.Command`) whose `Action.Parameters` has no non-empty `commandId`, for both top-level steps and steps inside `Loop`/`If` bodies (recursing through `Body`/`ElseBody` the same way existing checks already traverse nested steps), producing an error string identifying the step id
- [X] T008 [US1] In `src/GameBot.Domain/Commands/FileSequenceRepository.cs`, mirror the identical `commandId`-presence gate inside `ValidateActionPayloads` (per the existing "two allow-lists" duplication noted in research.md), so this path can't be bypassed
- [X] T009 [US1] In `src/GameBot.Service/Endpoints/SequencesEndpoints.cs`, confirm the `POST`/`PUT` handlers surface the new validation errors from T007 through the existing `Results.BadRequest(new { message = "Invalid sequence payload", errors = ... })` shape (no new error envelope) — adjust only if the current wiring doesn't already propagate `SequenceStepValidationService`'s errors to the response

**Checkpoint**: Run T001-T006 — all must now pass. User Story 1 is independently
complete and testable.

---

## Phase 4: User Story 2 - A required-dispatch step reliably fails the sequence when nested (Priority: P1)

**Goal**: `requireDispatch: true` on a step nested inside a `Loop` or `If` body
must fail the step (and the run) when the step's action doesn't dispatch, exactly
as it already does for top-level steps, at any nesting depth.

**Independent Test**: Run a sequence with a `Loop` body containing one
`requireDispatch: true` step that dispatches nothing; confirm the step and the
overall run are reported `Failed`. Repeat for an `If` body.

### Tests for User Story 2 ⚠️

> Write these first; confirm they FAIL against current code before implementing.

- [X] T010 [P] [US2] Add failing integration test in `tests/integration/Sequences/` (new file `NestedRequireDispatchIntegrationTests.cs`) that creates a sequence via the real `POST /api/sequences` HTTP path with a `Loop(maxIterations: 1)` body containing one step with `requireDispatch: true` targeting an action that won't dispatch (e.g. an `imageVisible` wait that never matches), runs it, and asserts the step outcome and overall run status are both `Failed`
- [X] T011 [P] [US2] Add the same failing test in the same file for an `If` body (both `then` and `else` branches) with a `requireDispatch: true` step that doesn't dispatch
- [X] T012 [P] [US2] Add a failing integration test in the same file for a step nested **two levels deep** — a top-level `Loop` body containing an `If` step, whose branch contains the `requireDispatch: true` step that doesn't dispatch — asserting the same `Failed` outcome (spec FR-007, nesting-depth uniformity)
- [X] T013 [P] [US2] Add a regression test in the same file confirming a nested `requireDispatch: true` step whose action *does* dispatch still reports the run as `Succeeded` (spec FR-008) — should already pass and must keep passing
- [X] T014 [P] [US2] Add a regression test confirming a *top-level* `requireDispatch: true` step's behavior (both dispatch-succeeds and dispatch-fails) is unchanged (spec FR-008) — should already pass and must keep passing

### Implementation for User Story 2

- [X] T015 [US2] In `src/GameBot.Service/Endpoints/SequencesEndpoints.cs`, `MapBodySteps`, add `RequireDispatch = child.RequireDispatch ?? false,` to the mapped `SequenceStep` object initializer, mirroring the existing top-level assignment in `MapToLinearSteps` (this single fix covers the `Loop`-body, `If`-body, and any-depth-nested cases, since `MapBodySteps` recurses into itself for nested `If` children and is reused for every `Loop` body — confirmed by code inspection)
- [X] T016 [US2] In the same file, check the read path (`MapStepToDto` / the `requireDispatch` projection near line 565) recurses into `Body`/`ElseBody` when serializing a sequence back out, so a `GET` of a sequence with a nested `requireDispatch: true` step reflects it correctly — fix if it currently doesn't recurse

**Checkpoint**: Run T010-T014 — all must now pass. User Stories 1 AND 2 both work
independently.

---

## Phase 5: User Story 3 - Session-input errors describe the real problem (Priority: P3)

**Goal**: `POST /api/sessions/{id}/inputs` against a confirmed-running session no
longer reports `409 not_running` when the real problem is malformed action
arguments; it reports `400` (nothing dispatched) or an extended `202` (partial
dispatch) instead, per contracts/api-changes.md.

**Independent Test**: Post a swipe action with a mismatched argument shape
(`{x1,y1}` missing `x2,y2`) to a session confirmed `Running`; confirm the response
is not `409`, and that a genuinely absent/stopped session still returns `409`.

### Tests for User Story 3 ⚠️

> Write these first; confirm they FAIL against current code before implementing.

- [X] T017 [P] [US3] Add failing unit test in `tests/unit/Emulator/` (new file `SessionManagerInputResultsTests.cs`) asserting the new per-action-result method on `SessionManager` reports `Dispatched: false` with a non-null `FailureReason` for a swipe action missing `x2`/`y2`, while a well-formed tap action in the same call reports `Dispatched: true`
- [X] T018 [P] [US3] Add failing integration test in `tests/integration/SessionInputTests.cs` (ADB/stub mode, per existing test setup patterns in that file) that posts a single malformed swipe action to a session confirmed `Running`, asserting `400 Bad Request` with `error.code == "invalid_input_actions"` (not `409`)
- [X] T019 [P] [US3] Add failing integration test in the same file posting one well-formed tap action and one malformed swipe action in the same request to a `Running` session, asserting `202 Accepted` with a `results` array showing one dispatched and one not, with a failure reason
- [X] T020 [P] [US3] Add a regression test in the same file confirming a request against a genuinely non-existent/non-running session id still returns `409 not_running` unchanged (spec FR-010) — should already pass and must keep passing
- [X] T021 [P] [US3] Add a regression test confirming an all-well-formed-actions request against a `Running` session still returns `202` with the existing `accepted` count (spec FR-013) — should already pass and must keep passing

### Implementation for User Story 3

- [X] T022 [US3] In `src/GameBot.Emulator/Session/SessionManager.cs`, factor the per-action dispatch loop (currently inside `SendInputsAsync`, lines ~124-244) so it can report a per-action result (dispatched yes/no, and a short stable failure reason instead of swallowing the exception in the `catch` blocks at lines ~219-230) without changing `SendInputsAsync`'s existing `Task<int>` signature or behavior
- [X] T023 [US3] Add the new result-returning method (e.g. `SendInputsWithResultsAsync`) to `src/GameBot.Emulator/Session/ISessionManager.cs` and its implementation in `SessionManager.cs`, reusing the loop from T022
- [X] T024 [US3] In `src/GameBot.Service/Endpoints/SessionsEndpoints.cs`, change the `POST {id}/inputs` handler to call the new method from T023 and the session's real `Status`, selecting: `409` when the session isn't found/running (unchanged), `400` with `error.code = "invalid_input_actions"` when running but zero actions dispatched, `202` with the existing `accepted` count plus a `results` array otherwise — per contracts/api-changes.md

**Checkpoint**: Run T017-T021 — all must now pass. All three user stories are
independently functional.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T025 Check `docs/architecture.md` for any description of the current (buggy) contract of `POST /api/sequences`, sequence execution `requireDispatch` semantics, or `POST /api/sessions/{id}/inputs`, and update it plus its "Last reviewed" date if so (Constitution Principle V)
- [X] T026 Run `dotnet build` and the full `dotnet test` suite (unit + integration + contract projects) and confirm zero failures/regressions
- [X] T027 Manually validate quickstart.md's three curl scenarios against a locally running `GameBot.Service`
- [X] T028 Update spec.md's Status line to `Implemented` once all tasks above are verified complete

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup / Foundational**: None — skipped, no shared prerequisites.
- **User Stories (Phase 3-5)**: Fully independent of each other (disjoint files);
  may be done in any order or in parallel. Priority order for a solo/sequential
  run: US1 → US2 → US3 (P1, P1, P3).
- **Polish (Phase 6)**: Depends on all three user stories being complete.

### Within Each User Story

- Tests (T001-T006 / T010-T014 / T017-T021) MUST be written and confirmed FAILING
  before their story's implementation tasks.
- T007 before T008 before T009 (US1): validator gate first, then the
  duplicate-repository gate, then confirm the endpoint surfaces it.
- T015 before T016 (US2): the mapping fix is the actual bug fix; the read-path
  check follows it.
- T022 before T023 before T024 (US3): shared dispatch-loop refactor, then the new
  method, then the endpoint wiring.

### Parallel Opportunities

- T001-T006, T010-T014, and T017-T021 are each independently parallelizable
  within their story (different assertions in shared new test files — mark `[P]`
  where they land in genuinely separate files; several land in the same new test
  file per story and should be written together rather than truly concurrently).
- US1, US2, and US3 implementation phases (T007-T009, T015-T016, T022-T024) touch
  entirely disjoint files and can be worked in parallel by different people.

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. T001-T009 (US1). **STOP and VALIDATE**: run the new tests, confirm no
   regression in existing sequence-creation tests.

### Incremental Delivery

1. US1 (T001-T009) → validate → this alone fixes the highest-blast-radius bug.
2. US2 (T010-T016) → validate → closes the silent-false-success safety gap.
3. US3 (T017-T024) → validate → cleans up the misleading session-input error.
4. Phase 6 polish once all three are in.

## Notes

- All three stories are bug fixes with pre-existing coverage gaps identified
  during research (each bug went undetected specifically because no test
  exercised the real HTTP/mapping path involved) — the test tasks above target
  those exact gaps rather than re-testing already-covered paths.
- Commit after each user story's checkpoint, not after every individual task.
