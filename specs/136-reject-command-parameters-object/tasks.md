# Tasks: Reject a parameters object in command requests

**Input**: Design documents in `C:\src\GameBot\specs\136-reject-command-parameters-object\`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/commands-parameters-type-error.md, quickstart.md

**Tests**: Required. This is a bug fix (constitution Principle II): tests fail on unchanged code first.

**Rules for all tasks**:
- Use the PowerShell tool only. Use absolute backslash paths. Do not use `cd` or `&&`.
- Build command: `dotnet build C:\src\GameBot\GameBot.sln`. Warnings are errors, so add a helper in the task of its first use.
- Contract tests share a bin data dir. Use a unique command name in each test (for example a name with a new GUID).
- "No command stored" check: send `GET /api/commands` and filter the list by the unique command name. The filtered list must be empty.
- Error body shape: `{ error: { code, message, hint } }`. Code is `invalid_request`. The message is `parameters must be a list of parameter declarations`. The hint is `Send an array, even for one declaration.`
- Do not commit. Do not edit any file other than those named in a task.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an open task)
- **[Story]**: User story label (US1, US2, US3)

---

## Phase 1: Setup

**Purpose**: Know the state of the build and of the tests before any change.

- [ ] T001 Build the solution with `dotnet build C:\src\GameBot\GameBot.sln` and confirm that it passes with no warning. Note the result in the task result.
- [ ] T002 Run the existing command tests and note the pass count in the task result: `dotnet test C:\src\GameBot\tests\contract\GameBot.ContractTests.csproj --filter "FullyQualifiedName~Commands"` and `dotnet test C:\src\GameBot\tests\integration\GameBot.IntegrationTests.csproj --filter "FullyQualifiedName~Command"`.

---

## Phase 2: Foundational (blocks all stories)

**Purpose**: Record what unchanged code answers, so that later tests assert real facts.

- [ ] T003 Read `C:\src\GameBot\src\GameBot.Service\Endpoints\CommandsEndpoints.cs` (POST handler near line 366-372, `ReadUpdateBodyAsync` near line 334, PATCH handler near line 473) and `C:\src\GameBot\tests\contract\Commands\UnknownStepFieldContractTests.cs` (test style: WebApplicationFactory, bearer token `test-token`). Confirm that `ReadUpdateBodyAsync` has one caller (the PATCH handler). Note the finding in the task result.
- [ ] T004 Record the PATCH baseline on unchanged code. Use a temporary scratch test (do NOT commit it; delete it after the run) or a scratch HTTP call against a test host. Create a command with a unique name, then send `PATCH /api/commands/{id}` with `parameters` as an object, as a string, as a number, as `null`, as `[]`, and with a wrong type in another field (for example `name` as a number). Write the status code and error code of each case as a note in the task result. This note is the PATCH baseline. It is not a committed test. If the object case answers 400 and not 500, state it in the note.

**Checkpoint**: The baseline is known. Story work can start.

---

## Phase 3: User Story 1 - Get a 400 when parameters is an object (Priority: P1) MVP

**Goal**: `POST /api/commands` with `parameters` as an object answers 400 that names `parameters`, and stores no command.

**Independent Test**: Send POST `/api/commands` with `"parameters": {"name":"novaOptionImage","type":"text","required":true}` and a valid PrimitiveTap step. Expect 400, `parameters` in the message, and an empty filtered `GET /api/commands` list.

### Tests for User Story 1 (write first, they MUST fail on unchanged code)

- [ ] T005 [US1] Create `C:\src\GameBot\tests\contract\Commands\ParametersTypeContractTests.cs` (namespace and style as `UnknownStepFieldContractTests`, class `ParametersTypeContractTests`). Add a helper that builds a valid create body with a unique command name and a PrimitiveTap step, and a helper that returns the stored commands whose name equals a given name through `GET /api/commands`. Add POST tests: `parameters` as an object gives 400, code `invalid_request`, message contains `parameters`, same `{ error: { code, message, hint } }` shape, and the filtered list is empty. Add POST cases for `parameters` as a string, a number, `true`, and a list with a non-object item (`[1]`): each gives 400 and names `parameters`. Add a case with a wrong type inside an item (`parameters[0].required` as a string): 400 that names `parameters`. Run `dotnet test C:\src\GameBot\tests\contract\GameBot.ContractTests.csproj --filter "FullyQualifiedName~ParametersType"` and confirm the new tests fail with 500 before the fix.

### Implementation for User Story 1

- [ ] T006 [US1] In `C:\src\GameBot\src\GameBot.Service\Endpoints\CommandsEndpoints.cs`, in the POST handler, add a `catch (JsonException)` with a `when` filter after the existing `forceRestart` catch (the `forceRestart` catch stays first). The filter is true when `Path` equals `$.parameters` or starts with `$.parameters.` or `$.parameters[`. The catch returns 400 with code `invalid_request`, the message and the hint from the rules above. Add one private helper that builds this 400 result (used by this task; T010 reuses it).
- [ ] T007 [US1] Build with `dotnet build C:\src\GameBot\GameBot.sln`, then run `dotnet test C:\src\GameBot\tests\contract\GameBot.ContractTests.csproj --filter "FullyQualifiedName~ParametersType"`. All POST tests from T005 must pass.

**Checkpoint**: US1 works alone. This is the MVP.

---

## Phase 4: User Story 2 - Valid requests keep working (Priority: P1)

**Goal**: A list, no `parameters`, `null`, and `[]` keep their old answers. A wrong type in another field keeps its old answer.

**Independent Test**: The existing command tests and the new regression tests pass.

- [ ] T008 [US2] In `C:\src\GameBot\tests\contract\Commands\ParametersTypeContractTests.cs`, add POST regression tests: `parameters` as a valid list gives 201; no `parameters` gives 201; `parameters: null` and `parameters: []` give the answer recorded on unchanged code (read it from the T002 run or run these two cases on unchanged code with `git stash` of the T006 edit, then assert the exact status); a wrong type in another field (for example `name` as a number) gives the answer it has on unchanged code and does not name `parameters`. Run the filter `FullyQualifiedName~ParametersType`. All must pass.
- [ ] T009 [US2] Run the full command suites and confirm the pass counts equal the T002 counts: `dotnet test C:\src\GameBot\tests\contract\GameBot.ContractTests.csproj --filter "FullyQualifiedName~Commands"` and `dotnet test C:\src\GameBot\tests\integration\GameBot.IntegrationTests.csproj --filter "FullyQualifiedName~Command"`.

**Checkpoint**: No regression on POST.

---

## Phase 5: User Story 3 - Same rule on the update endpoint (Priority: P2)

**Goal**: `PATCH /api/commands/{id}` with a bad `parameters` type answers 400 that names `parameters`, before the stored-command lookup, and changes nothing.

**Independent Test**: Create a command. Send PATCH with `parameters` as an object. Expect 400, `parameters` in the message, and an unchanged stored command.

### Tests for User Story 3 (write first, they MUST fail on unchanged code if T004 showed 500)

- [ ] T010 [US3] In `C:\src\GameBot\tests\contract\Commands\ParametersTypeContractTests.cs`, add PATCH tests: after creating a command with a unique name, PATCH with `parameters` as an object, a string, a number, `true`, and `[1]` each gives 400 with `parameters` in the message; the stored command (read with `GET /api/commands/{id}`) is unchanged. Add a test that PATCH with a bad `parameters` type on an unknown id gives 400, not 404. Add tests for `parameters: null`, `parameters: []`, and a wrong type in another field: each asserts the answer recorded in T004. Add a test that a valid PATCH keeps its old answer. Run the filter `FullyQualifiedName~ParametersType` and confirm the bad-type tests fail before the fix.

### Implementation for User Story 3

- [ ] T011 [US3] In `C:\src\GameBot\src\GameBot.Service\Endpoints\CommandsEndpoints.cs`, change `ReadUpdateBodyAsync` to return `(Request, Body, Error)`. When the `JsonException` `Path` is at or under `$.parameters`, return the 400 result from the helper made in T006 as `Error`. Keep all other malformed-body behavior unchanged. Update its one caller, the PATCH handler: return `Error` at once when it is set, before the stored-command lookup.
- [ ] T012 [US3] Build with `dotnet build C:\src\GameBot\GameBot.sln`, then run `dotnet test C:\src\GameBot\tests\contract\GameBot.ContractTests.csproj --filter "FullyQualifiedName~ParametersType"`. All tests must pass.

**Checkpoint**: All three stories work.

---

## Phase 6: Polish and cross-cutting

- [ ] T013 [P] In `C:\src\GameBot\docs\architecture.md`, add one STE sentence for the rule (a `parameters` type error in a command request gives 400 `invalid_request`) and refresh the "Last reviewed" line.
- [ ] T014 [P] Check the contract file `C:\src\GameBot\specs\136-reject-command-parameters-object\contracts\commands-parameters-type-error.md` against the final message and hint. Fix any difference in STE.
- [ ] T015 Run the final gate: `dotnet build C:\src\GameBot\GameBot.sln`, then both command suites from T009 (contract filter `FullyQualifiedName~Commands`, integration filter `FullyQualifiedName~Command`). All must pass with no new warning.
- [ ] T016 Do the manual check from `C:\src\GameBot\specs\136-reject-command-parameters-object\quickstart.md` step 5: POST `/api/commands` with `"parameters": {"name":"p","type":"text","required":true}` gives 400 and `parameters` in the message.

---

## Dependencies and order

- Phase 1 then Phase 2, then US1 (T005-T007). US2 (T008-T009) needs T006. US3 (T010-T012) needs T006 (helper) and T004 (baseline).
- T005, T008, T010 edit the same test file: run them in order, not in parallel.
- T011 edits the same source file as T006: run after it.
- T013 and T014 can run in parallel (different files).

## Implementation strategy

- MVP: Phase 1, Phase 2, and US1 (T001-T007). This closes the defect in the issue for POST.
- Then add US2 (regression guard) and US3 (PATCH). Run the final gate (T015) before the pull request.

## Summary

- Total tasks: 16 (T001-T016)
- Phases: 6 (Setup 2, Foundational 2, US1 3, US2 2, US3 3, Polish 4)
