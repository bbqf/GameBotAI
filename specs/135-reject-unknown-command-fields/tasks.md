# Tasks: Reject unknown command step fields

**Input**: Design documents in `C:\src\GameBot\specs\135-reject-unknown-command-fields\`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/unknown-field-error.md, quickstart.md
**Tests**: Required. FR-009 demands failing tests before the fix.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: The task can run in parallel (different file, no open dependency).
- Tasks that edit the same file are NOT [P]. All contract tests are in one file, so they run in order.
- Use PowerShell with absolute paths. Do not use `cd dir;`.

## Phase 1: Setup

- [ ] T001 Build the solution and run the existing command tests. Record that they pass: `dotnet test C:\src\GameBot\GameBot.sln --filter "FullyQualifiedName~Commands"`. Stop if a test fails (constitution gate).
- [ ] T002 Record the current PATCH /api/commands/{id} answers (status and body) in `C:\src\GameBot\specs\135-reject-unknown-command-fields\research.md` under D-7. Read `C:\src\GameBot\src\GameBot.Service\Endpoints\CommandsEndpoints.cs` and call the running service or a temporary test. Record four answers: (a) valid PATCH status, (b) empty body, (c) malformed JSON on an existing command, (d) malformed JSON on a missing command id. Do not guess the values.
- [ ] T003 [P] Search for real callers of the root alias `detectionTarget` on POST /api/commands (`TryReadDetection` in `C:\src\GameBot\src\GameBot.Service\Endpoints\CommandsEndpoints.cs`, web-ui, tests, docs). Record the result in `C:\src\GameBot\specs\135-reject-unknown-command-fields\research.md` under D-3. The alias stays accepted.

**Checkpoint**: Baseline is green and the PATCH answers are recorded.

## Phase 2: Foundational

No shared prerequisite task. The scanner is part of User Story 1.

## Phase 3: User Story 1 - Error for an unknown step field (Priority: P1) MVP

**Goal**: POST /api/commands answers 400 with every unknown path and a hint. Nothing is stored.

**Independent Test**: POST a PrimitiveTap step with `primitiveTap.bogusField`. Expect 400, the path `steps[0].primitiveTap.bogusField` in the error, and no stored command.

### Tests for User Story 1 (write first, they MUST fail before the fix)

- [ ] T004 [US1] Create `C:\src\GameBot\tests\contract\Commands\UnknownStepFieldContractTests.cs`. Add POST tests: (a) unknown `primitiveTap.bogusField` gives 400, the message contains `steps[0].primitiveTap.bogusField`, and the hint is not null; (b) after the 400, GET /api/commands shows no new command; (c) unknown fields in two places give 400 and both full paths; (d) unknown step top-level field gives `steps[0].bogus`; (e) unknown nested field gives `steps[0].primitiveTap.detectionTarget.bogus`; (f) unknown root field gives 400 with the field name; (g) a `null` value for an unknown field gives 400.
- [ ] T005 [US1] Run the new POST tests and confirm they FAIL with 201 instead of 400 (FR-009): `dotnet test C:\src\GameBot\GameBot.sln --filter "FullyQualifiedName~UnknownStepField"`. Record the failure in the task notes.
- [ ] T006 [P] [US1] Create `C:\src\GameBot\tests\unit\Commands\UnknownFieldScannerTests.cs`. Define a small test-only type with a `[JsonIgnore]` property in this file. Add cases: unknown field path with `[i]` array index; nested path; OrdinalIgnoreCase match is not unknown; `[JsonIgnore]` property counts as unknown; `Dictionary<string,string>` keys are not flagged; `null` value for an unknown name is unknown; all unknown paths are returned in order; a body with only known fields returns an empty list. The scanner class does not exist yet, so these tests do not compile until T007.

### Implementation for User Story 1

- [ ] T007 [US1] Create `C:\src\GameBot\src\GameBot.Service\Endpoints\UnknownFieldScanner.cs` with `Find(JsonElement root, Type dtoType)`. Use cached reflection on public properties and OrdinalIgnoreCase. Return full paths from the body root (`steps[0].primitiveTap.bogusField`). Do not recurse into `Dictionary<string,string>`. Treat `[JsonIgnore]` properties as unknown. Walk arrays and nested DTOs. Do not change any DTO in `C:\src\GameBot\src\GameBot.Service\Models\Commands.cs`.
- [ ] T008 [US1] Run the scanner unit tests and fix until they pass: `dotnet test C:\src\GameBot\GameBot.sln --filter "FullyQualifiedName~UnknownFieldScanner"`.
- [ ] T009 [US1] Edit POST in `C:\src\GameBot\src\GameBot.Service\Endpoints\CommandsEndpoints.cs`. After `ParseAsync` and before `Deserialize`, scan with `CreateCommandRequest`. Skip the root alias `detectionTarget`. If the list is not empty, return 400 `invalid_request` with message `Unknown field(s): <paths>` and hint `Remove the field or check its name in the OpenAPI schema.` Do not change Program.cs.
- [ ] T010 [US1] Run the POST contract tests and confirm they pass: `dotnet test C:\src\GameBot\GameBot.sln --filter "FullyQualifiedName~UnknownStepField"`.

**Checkpoint**: User Story 1 works alone.

## Phase 4: User Story 2 - Valid requests keep working (Priority: P1)

**Goal**: Valid requests keep status 201 and the same body.

**Independent Test**: Existing command tests pass with no change. A request with `holdMs` in `primitiveTap` is stored.

- [ ] T011 [US2] In `C:\src\GameBot\tests\contract\Commands\UnknownStepFieldContractTests.cs` add valid-request tests: (a) a valid request gives 201; (b) `primitiveTap.holdMs` gives 201 and the stored command keeps `holdMs`; (c) field names in a different letter case give 201; (d) the root alias `detectionTarget` gives 201 as before. Run them; they must pass.
- [ ] T012 [US2] Run all existing command contract and unit tests with no edit to expected values: `dotnet test C:\src\GameBot\GameBot.sln --filter "FullyQualifiedName~Commands"`. Confirm SC-002.
- [ ] T013 [US2] Check that the OpenAPI schema for command steps is unchanged (FR-006, SC-004). Compare the generated schema before and after, or confirm that no file in `C:\src\GameBot\src\GameBot.Service\Models\Commands.cs` has a diff. `additionalProperties` stays false.

**Checkpoint**: User Stories 1 and 2 pass.

## Phase 5: User Story 3 - Same rule on PATCH (Priority: P2)

**Goal**: PATCH /api/commands/{id} answers 400 with the path and a hint. The stored command does not change. A missing command gives 404 first.

**Independent Test**: Create a command. PATCH an unknown step field. Expect 400, the path, and an unchanged command.

### Tests for User Story 3 (write first, they MUST fail before the fix)

- [ ] T014 [US3] In `C:\src\GameBot\tests\contract\Commands\UnknownStepFieldContractTests.cs` add PATCH tests: (a) unknown step field gives 400 with `steps[0].primitiveTap.bogusField` and a hint; (b) GET after the 400 shows the stored command unchanged; (c) unknown field on a missing id gives 404; (d) valid PATCH keeps the status recorded in T002; (e) empty body keeps the answer recorded in T002; (f) malformed JSON keeps the answer recorded in T002; (g) malformed JSON on a missing id keeps the answer recorded in T002.
- [ ] T015 [US3] Run the new PATCH tests and confirm the unknown-field tests FAIL with a success status instead of 400 (FR-009). Cases (c) to (g) must already pass: they pin today's answers.

### Implementation for User Story 3

- [ ] T016 [US3] Edit PATCH in `C:\src\GameBot\src\GameBot.Service\Endpoints\CommandsEndpoints.cs`. Read and parse the body by hand. Do the 404 lookup first. Then scan with `UpdateCommandRequest`. Then deserialize with `WebJsonOptions`. Run the scan before any change to the stored command. Keep the answers recorded in T002 for empty body, malformed JSON, and malformed JSON on a missing id. Return the same 400 message and hint as POST.
- [ ] T017 [US3] Run the PATCH tests and confirm all pass: `dotnet test C:\src\GameBot\GameBot.sln --filter "FullyQualifiedName~UnknownStepField"`.

**Checkpoint**: All three stories pass.

## Phase 6: Polish and Cross-Cutting

- [ ] T018 Run the full test suite for the solution: `dotnet test C:\src\GameBot\GameBot.sln`. Fix any failure.
- [ ] T019 FR-007 scope diff check. Run `git -C C:\src\GameBot diff --name-only master...HEAD` and `git -C C:\src\GameBot status --short`. Confirm that the diff touches only the scanner, `CommandsEndpoints.cs`, tests, specs, and docs. Confirm no new step field, no hold behaviour, no reschedule-self validator change, and no fix for issue #251.
- [ ] T020 [P] FR-009 docs: check `C:\src\GameBot\docs\architecture.md`. If the API surface description changes (400 for unknown fields), update it and set a new "Last reviewed" date. If it does not change, record that in `C:\src\GameBot\specs\135-reject-unknown-command-fields\research.md`.
- [ ] T021 [P] FR-009 status: set the `**Status**:` line in `C:\src\GameBot\specs\135-reject-unknown-command-fields\spec.md` to Implemented. Update the row for 135 in `C:\src\GameBot\specs\STATUS.md`.
- [ ] T022 STE polish: read all new or changed text (spec, plan, research, tasks, code comments, error messages, docs) and fix any text that does not follow ASD-STE100.
- [ ] T023 Run the quickstart steps in `C:\src\GameBot\specs\135-reject-unknown-command-fields\quickstart.md` and confirm the results. Do not commit.

## Dependencies and Order

- Phase 1 first. T002 blocks T014 and T016.
- T004 then T005 (fail first). T006 can run with T004 (different file). T007 needs T006 to compile its tests; T008 follows T007. T009 follows T007. T010 follows T009.
- T011 to T013 follow T010. T011 edits the same file as T004, so it is not [P].
- T014 follows T011. T015 follows T014. T016 follows T015. T017 follows T016.
- Phase 6 follows Phase 5. T020 and T021 can run in parallel.

## Parallel Example

- T003 with T001/T002 (different file purpose, read only until it records).
- T006 with T004 (different test files).
- T020 with T021.

## Implementation Strategy

- MVP: Phase 1 and Phase 3 (User Story 1, POST). Then US2 to prove no regression. Then US3 for PATCH.
- Always run each new test and see it fail before the fix (T005, T015).
