# Tasks: Reject a malformed sequence step on create

**Input**: Design documents from `specs/113-reject-malformed-sequence-step/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/sequences-create.md, quickstart.md

**Tests**: The spec and the constitution require tests. The issue asks for contract and integration tests. Test tasks come before the implementation tasks in each story.

**Organization**: Tasks are in groups by user story. Each story can be tested alone.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: The user story of the task (US1, US2, US3, US4)

---

## Phase 1: Setup

**Purpose**: Get a known baseline.

- [X] T001 Build the solution with `dotnet build GameBot.sln` from the repo root, and run `tests/unit`, `tests/contract` and `tests/integration` on the base commit (with `DOTNET_ROLL_FORWARD=Major` on a host without the .NET 9 runtime). Record the tests that fail before any change, so that later runs can separate environment failures from real failures.

---

## Phase 2: Foundational (blocking prerequisites)

**Purpose**: The error prefix helper exists. US1 needs it.

- [X] T002 Add the private helper `DescribeStepPosition(int index, JsonElement step)` to `src/GameBot.Service/Endpoints/SequencesEndpoints.cs`. It returns `steps[<i>] (stepId '<id>')` when the step is an object with a string `stepId`, otherwise `steps[<i>]` (research R-005). Add an STE XML comment.

**Checkpoint**: The solution builds. No behavior changes.

---

## Phase 3: User Story 1 - A malformed step gets a 400 that names the step (Priority: P1) MVP

**Goal**: The reproduction body gets 400 with `steps[0] (stepId 'a')`, and the service stores nothing, on `POST`, `PUT` and `PATCH`.

**Independent Test**: `SequenceCreateMalformedStepIntegrationTests` (US1 tests) pass.

### Tests for User Story 1

- [X] T003 [US1] Create `tests/integration/Sequences/SequenceCreateMalformedStepIntegrationTests.cs` with a factory and an authorized client as in `SequenceCreateDryRunIntegrationTests.cs`, and a helper that creates a command through `POST /api/commands` and returns its id. Add these tests: (a) the reproduction body gets 400, one error contains `steps[0] (stepId 'a')`, and `GET /api/sequences` has no item with the name; (b) the same step with `stepType: "Action"` gets 400 with the same error text; (c) a body whose second step has only `commandReference` gets 400 with `steps[1] (stepId 'b')`; (d) `PUT /api/sequences/{id}` with the reproduction steps gets 400, and `GET /api/sequences/{id}` shows the stored steps and version without a change; (e) a body with `steps: ["c1", { stepId: "x", stepType: "Action", primitiveAction: ... }]` gets 400 with `steps[0]: each step must be an object.`; (f) a step object without `stepId` gets 400 with `steps[<i>]: each step must include string stepId.` Before the change, (a), (c), (d), (e) and (f) fail.

### Implementation for User Story 1

- [X] T004 [US1] Change `IsPerStepRequestCandidate` in `src/GameBot.Service/Endpoints/SequencesEndpoints.cs`: return `true` when an item of `steps` is an object that has `primitiveAction` or `stepType`, or when the body has no `blocks` and an item of `steps` is an object (research R-001, R-002). Keep the legacy branching check. Add an STE comment that refers to issue #242.
- [X] T005 [US1] In `TryReadPerStepRequest` in the same file, loop with an index, and start each shape error with `DescribeStepPosition(index, stepElement)` and `: ` (data-model.md, "Shape errors").

**Checkpoint**: The US1 tests pass. The current sequence tests pass.

---

## Phase 4: User Story 2 - A dry run never stores a sequence (Priority: P1)

**Goal**: A create with `dryRun: true` never stores a sequence, for each body shape.

**Independent Test**: The US2 tests in `SequenceCreateMalformedStepIntegrationTests` pass.

### Tests for User Story 2

- [X] T006 [US2] In `tests/integration/Sequences/SequenceCreateMalformedStepIntegrationTests.cs`, add these tests: (a) the reproduction body with `dryRun: true` gets 400 with the same errors as without `dryRun`, and the count of stored sequences does not change; (b) an old body `{ name, steps: ["<command id>"], dryRun: true }` gets 200 `{ valid: true, dryRun: true, errors: [] }`, and the count does not change; (c) an old body with `steps: ["c1", 5]` gets 400 with `steps[1]`; (d) an old body with `parameters: []` gets 400, and an old body with `parameters: null` gets 201.

### Implementation for User Story 2

- [X] T007 [US2] In the old branch of `CreateSequenceAsync` in `src/GameBot.Service/Endpoints/SequencesEndpoints.cs`: reject an item of `steps` that is not a string with `steps[<i>]: each step must be a string command id or a step object.`, reject a `parameters` value that is not `null` with `parameters requires the per-step body shape (steps as step objects).`, and return the dry-run 200 body before `repo.CreateAsync` when `IsDryRunRequested(root)` is true (research R-003, R-004).
- [X] T008 [US2] In the domain branch of `CreateSequenceAsync`, return the dry-run 200 body after `ValidateSequence` and before `repo.CreateAsync` when `IsDryRunRequested(root)` is true.

**Checkpoint**: The US1 and US2 tests pass.

---

## Phase 5: User Story 3 - Valid bodies keep their behavior (Priority: P1)

**Goal**: A valid per-step body with a command step and parameters keeps its result.

**Independent Test**: The US3 test passes, and the current sequence tests pass.

- [X] T009 [US3] In `tests/integration/Sequences/SequenceCreateMalformedStepIntegrationTests.cs`, add a test: a per-step body with a `command` step (`primitiveAction.payload.commandId` of the created command), a `reschedule-self` step and one declared parameter gets 201, and `GET /api/sequences/{id}` shows 2 steps and 1 parameter. Add a test: an old body with one string command id gets 201.
- [X] T010 [US3] Run the sequence tests in `tests/integration` (`FullyQualifiedName~Sequences`) and in `tests/contract` (`FullyQualifiedName~Sequences`). Fix each regression that the change causes. `tests/integration/Sequences/FixedDelayTests.cs` pinned the silent drop of object steps `{ order, commandId, delayMs }` (201 with zero steps). Change it to expect 400 with `steps[0]` and no stored sequence, because issue #242 forbids that drop.

---

## Phase 6: User Story 4 - The API documentation tells the rule (Priority: P3)

**Goal**: The OpenAPI description of the create route tells the new rule.

- [X] T011 [US4] Create `tests/contract/Sequences/SequenceCreateMalformedStepContractTests.cs`. Read `/swagger/v1/swagger.json` as in `tests/contract/Sequences/SequenceWritesOpenApiTests.cs`, and make sure that the description of `POST /api/sequences` contains `any body shape` and `steps[<index>]`. The test fails before T012.
- [X] T012 [US4] Change `SequenceCreateDescription` in `src/GameBot.Service/Swagger/SwaggerConfig.cs`: `dryRun: true` (any body shape) never stores a sequence, and a step that breaks the step shape rules (for example, an Action step without `primitiveAction`) gets 400 with an error that starts with `steps[<index>] (stepId '<id>')`. Update each OpenAPI snapshot or test that compares the old text.

---

## Phase 7: Polish and cross-cutting concerns

- [X] T013 [P] Update the section "Dry-run / validate-only sequence mode" in `docs/architecture.md`: `dryRun` on create applies to each body shape, the per-step shape applies when a step is an object, and a malformed step gets 400 that names it. Change the "Last reviewed" date if the file has one.
- [X] T014 [P] Add a "Fixed" entry for 113-reject-malformed-sequence-step (#242) at the top of `CHANGELOG.md`, with a **Compatibility** line.
- [X] T015 [P] Add row 113 to `specs/STATUS.md` with the status `Implemented`, and set the spec `Status` line to `Implemented`.
- [X] T016 Build the solution and run `tests/unit`, `tests/contract` and `tests/integration`. Compare the failures with the baseline of T001.

---

## Dependencies & Execution Order

- T001 → T002 → US1 (T003 → T004, T005) → US2 (T006 → T007, T008) → US3 (T009, T010) → US4 (T011 → T012) → Polish (T013, T014, T015 in parallel) → T016.
- US2 and US4 do not need US1 in code, but the tests of US2 use the helper from T003.

## Implementation Strategy

MVP: Phase 1 to Phase 3 fix the data loss of the issue. Phase 4 closes the dry-run hole. Phase 5 to Phase 7 prove that no regression occurs and update the docs.
