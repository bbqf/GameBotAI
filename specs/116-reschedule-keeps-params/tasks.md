---

description: "Task list for feature 116: keep the parameter values of a queue entry on a run that reschedule-self starts"
---

# Tasks: Keep the parameter values of a queue entry on a run that reschedule-self starts

**Input**: Design documents from `/specs/116-reschedule-keeps-params/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/self-reschedule-coordinator.md, quickstart.md

**Tests**: This is a bug fix. Constitution Principle II and the plan (section "Test approach") tell you to write the failing tests first. Thus each user story has test tasks. Write them, and make sure that they fail before you do the fix.

**Organization**: The tasks are in groups for each user story, so that you can do and test each story independently.

**Booking options**: This file uses one term for the booking options: the four options (five variants, because Timer has a relative offset and a time of day). The five variants are OncePerRun, EveryStep, AtQueueStart, Timer with a relative offset, and Timer with a time of day.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: You can do the task in parallel with other [P] tasks (different files, no dependencies).
- **[Story]**: The user story of the task (US1, US2, US3).
- Each description has the exact file path.

## Path Conventions

- Service code: `src/GameBot.Service/Services/`
- Unit tests: `tests/unit/` (project `C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj`)
- Integration tests: `tests/integration/` (project `C:\src\GameBot\tests\integration\GameBot.IntegrationTests.csproj`)
- Use the PowerShell tool with absolute backslash paths. Use the Edit and Write tools for UTF-8 files. Write all new comments and docs in ASD-STE100 Simplified Technical English (Constitution Principle VI).

---

## Phase 1: Setup (Baseline)

**Purpose**: Make sure that the build and the current tests are green before you change the code.

- [X] T001 Run the baseline gate and record the result: `dotnet build "C:\src\GameBot\GameBot.sln" -c Debug`, then `dotnet test "C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj" --filter "FullyQualifiedName~Queues|FullyQualifiedName~Sequences|FullyQualifiedName~Parameter"` and `dotnet test "C:\src\GameBot\tests\integration\GameBot.IntegrationTests.csproj" --filter "FullyQualifiedName~Queues"`. T019 uses the same filters. If a test fails before any change, record it as a pre-existing failure (do not fix unrelated failures in this feature).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Add the scope parameter to the internal contract, so that the tests of all stories can compile.

**CRITICAL**: The user story tests call `ScheduleSelf(..., scope)`. They do not compile before this phase is complete.

- [X] T002 In `src/GameBot.Service/Services/QueueExecution/ISelfRescheduleCoordinator.cs`, add the last parameter `GameBot.Domain.Parameters.ParameterScope? scope = null` to `ScheduleSelf` (signature in `specs/116-reschedule-keeps-params/contracts/self-reschedule-coordinator.md`). Add the XML doc: "The parameter scope of the run that makes the booking. The booked run uses this scope. Null means the queue scope."
- [X] T003 In `src/GameBot.Service/Services/QueueExecution/SelfRescheduleCoordinator.cs`, add the same optional parameter `ParameterScope? scope = null` to the `ScheduleSelf` implementation, so that the class implements the interface. Do not use the parameter yet (the tests of Phase 3 must fail first). This unused parameter is temporary: T009 uses it. It does not break the analyzer gate: `Directory.Build.props` sets `TreatWarningsAsErrors` and `AnalysisLevel` `latest-all`, but it does not set `EnforceCodeStyleInBuild`, so the build does not report IDE0060. Also, IDE0060 does not report a parameter of an interface implementation, and the current analyzers do not have CA1801. If the build reports an unused-parameter warning, add the temporary line `_ = scope;` and remove it in T009. Build `C:\src\GameBot\src\GameBot.Service\GameBot.Service.csproj` and make sure that the build is green and that all current callers (about 30 test calls in `tests/unit/Queues`) compile with no change.

**Checkpoint**: The contract is ready. The build is green. The behavior is the same as before.

---

## Phase 3: User Story 1 - A booked run resolves the parameters of the run that booked it (Priority: P1) MVP

**Goal**: A `reschedule-self` booking keeps the scope that the service gave the run that booked it, for all four options (five variants), and the booked run resolves each parameter from that scope (FR-001, FR-002, FR-003, FR-006).

**Independent Test**: A queue with one template entry that gives `parameterValues` for a required parameter. The sequence books a `reschedule-self` run and uses the parameter in a step field. Start the queue. The booked run resolves the parameter to the entry value and does not fail with "could not be resolved from any scope".

### Tests for User Story 1 (write first, make sure that they FAIL)

- [X] T004 [P] [US1] In `tests/unit/Queues/SelfRescheduleCoordinatorTests.cs`, add a Theory (or one test for each case) that calls `ScheduleSelf(..., scope)` with a `ParameterScope` object for each of the five variants. For AtQueueStart, test a cycling queue (entry in `PendingNextCycleStart`) and a queue with no cycling (entry in `PendingOncePerRun`), because each goes into a different register. Assert that the entry in the correct register has `Scope` that is the same object (`BeSameAs`) as the given scope.
- [X] T005 [P] [US1] In `tests/unit/Queues/QueueParameterScopeTests.cs` (partial class `QueueExecutionServiceTests`), add a Theory over the five variants. Use a template entry with `parameterValues` `slot = one`. Let `FakeSequenceExecution.Handler` read the scope of its firing (from `h.Sequences.Scopes`) and call `h.Coordinator.ScheduleSelf(..., scope)` on the first firing, the same way that the real service does. Advance the `FakeTimeProvider` for Timer. Assert that the booked firing resolves `slot` to `one` from the entry layer (`ParameterScopeLayers.Entry`). Also add one case (analyze C1) where an EveryStep template entry with `parameterValues` books the run: the booked firing resolves `slot` from the entry layer of that EveryStep entry. A BeforeEachRun entry uses the same `EntryScope(entry)` path, so the design covers it.
- [X] T006 [P] [US1] In `tests/integration/Queues/SelfRescheduleRunIntegrationTests.cs`, add the OncePerRun test with the real DI graph (`GAMEBOT_USE_ADB=false`, `TestEnvironment.PrepareCleanDataDir()`). Use the seed of plan.md "Test approach" step 4:
  - A command `c116` with the required parameter `slot` and one `PrimitiveTap` step whose `FieldTemplates` sets `primitiveTap.detectionTarget.referenceImageId` to `{{slot}}` (the form of `NovaParamTap` in `tests/integration/Commands/BindingPlaceholderScopeIntegrationTests.cs`).
  - A sequence with the required parameter `slot` (no default). Step 0 is `reschedule-self` OncePerRun. Step 1 (`StepId` = `s1`) is a Command step to `c116` with the `parameterBindings` value `slot` = `{{slot}}` (feature 115).
  - A queue with no cycling and one OncePerRun template entry with `parameterValues` `slot` = `pns-nova-option-affinity`.

  Run the queue to completion. Assert that the queue summary has "2 sequence(s) executed". Assert that the command log of `c116` has 2 `parameters` items, and that each item resolves `slot` to `pns-nova-option-affinity` with the origin layer `entry` (`ParameterScopeLayers.Entry`). Assert that no sequence log entry has "could not be resolved from any scope". Do not assert the failed count or the final status, because the tap result depends on the host.

  Do not use an `imageVisible` condition with `imageId` = `{{slot}}`. The code trace shows that `SequenceExecutionService.EvaluateImageConditionAsync` throws `image_unavailable` for an image id that is not in `IImageRepository`. Thus the If step fails with "If '<step>' condition evaluation failed: image_unavailable" before and after the fix, and the test cannot show the defect.
- [X] T007 [US1] In `tests/integration/Queues/SelfRescheduleRunIntegrationTests.cs` (same file as T006, so not [P] with T006), add the Timer test. Use the seed of T006, but step 0 is `reschedule-self` Timer with `timerRelativeOffset` `00:00:01`. A pending Timer booking keeps a queue with no cycling alive, and each run books a new run. Start the queue. Wait (60 seconds at most, so that a slow CI runner does not fail the test) until the command log of `c116` has 3 `parameters` items (the entry run and two booked runs). Then stop the queue with `IQueueExecutionService.StopAsync`. This test uses the real clock: the host DI graph does not let the test move a fake clock. Assert that each of the 3 items resolves `slot` to `pns-nova-option-affinity` with the origin layer `entry`, and that no sequence log entry has "could not be resolved from any scope". This test covers the real dispatch path for a Timer booking and a chain of two booked runs.
- [X] T008 [US1] Run the tests of T004, T005, T006, and T007. Make sure that they FAIL on the current code. Expected failures: `Scope` is null in T004; the booked firing has the queue scope only in T005; in T006 and T007 the booked run fails at step `s1` with "Step 's1': parameter 'slot' used by field 'parameterBindings.slot' could not be resolved from any scope.", so the command log has only 1 `parameters` item. If a test passes before the fix, change the test until it shows the defect.

### Implementation for User Story 1

- [X] T009 [US1] In `src/GameBot.Service/Services/QueueExecution/SelfRescheduleCoordinator.cs`, give `Scope: scope` to each of the four `new SelfRescheduleEntry(...)` calls, one for each option (OncePerRun, EveryStep, AtQueueStart, Timer). The AtQueueStart entry goes into one of two registers. If T003 added the line `_ = scope;`, remove it. Make no other logic change.
- [X] T010 [US1] In `src/GameBot.Service/Services/SequenceExecution/SequenceExecutionService.cs`, add a `ParameterScope scope` parameter to the private methods `DispatchActionAsync` and `DispatchSelfReschedule`. In `ExecuteCoreAsync`, let the action dispatcher lambda send the `scope` argument of `ExecuteCoreAsync` (the scope that the service gave the run, before `SequenceRunner` adds the sequence layer; see research R-002). In `DispatchSelfReschedule`, give this scope to `ISelfRescheduleCoordinator.ScheduleSelf`.
- [X] T011 [P] [US1] In `src/GameBot.Service/Services/QueueExecution/QueueRunHandle.cs`, change the doc comment of `SelfRescheduleEntry.Scope`: "The parameter scope that the queue run loop gave the run that made the booking. The booked run uses this scope. Null means the queue scope." Do not change `QueueExecutionService.cs` (the drains already use `entry.Scope ?? queueScope`).
- [X] T012 [US1] Run the tests of T004, T005, T006, and T007 again. Make sure that they all pass.

**Checkpoint**: User Story 1 works. The MVP fixes issue #249 for one booking.

---

## Phase 4: User Story 2 - A chain of bookings keeps the values (Priority: P1)

**Goal**: Each run in a chain of bookings resolves the parameters to the same values as the first run of the chain (FR-004, SC-002).

**Independent Test**: A sequence that books itself with `reschedule-self` Timer each time it runs. Let the queue run three or more booked runs. Each booked run resolves the parameter to the entry value.

### Tests for User Story 2

- [X] T013 [US2] In `tests/unit/Queues/QueueParameterScopeTests.cs`, add a Timer chain test: the `FakeSequenceExecution.Handler` books `reschedule-self` Timer with a relative offset on each firing, with the scope of that firing. Advance the `FakeTimeProvider` for at least three booked runs. Assert that each recorded scope in `h.Sequences.Scopes` resolves `slot` to `one` from the entry layer.
- [X] T014 [US2] In `tests/unit/Queues/QueueParameterScopeTests.cs`, add a test for a sequence with two booking steps: on one firing, the handler calls `ScheduleSelf` with Timer `00:15:00` and then with a second Timer offset (the form of `ocrOffset`). Assert that the Timer register keeps only the second booking (FR-010), that the run that the kept Timer booking starts resolves `slot` to `one`, and that a later booking from that run also resolves `slot` to `one`. Add a second case where the handler calls `ScheduleSelf` only with Timer `00:15:00` (the run stops before the later step). Assert that the run that this step-0 booking starts also resolves `slot` to `one`.
- [X] T015 [US2] Run the tests of T013 and T014 on the code from Phase 3. Make sure that they pass. (The fix of T009 and T010 covers the chain, because the booked run gets the kept scope as its `scope` argument. T007 covers a chain on the real dispatch path. If a test fails, find the cause before you continue.)

**Checkpoint**: User Stories 1 and 2 work. A chain keeps the values for each generation.

---

## Phase 5: User Story 3 - No change for other runs (Priority: P2)

**Goal**: The runs that the queue start, the template timers, the daily clock, the daily retry, the every-step pass, the before-each-run pass, and the live schedules start resolve parameters as before. A booking with no kept scope uses the queue scope (FR-005, FR-007, FR-008, FR-009, FR-010, SC-004).

**Independent Test**: Run the current queue and parameter tests. They all pass with no change to their expected results.

### Tests for User Story 3

- [X] T016 [P] [US3] In `tests/unit/Queues/SelfRescheduleCoordinatorTests.cs`, add two tests: (a) a `ScheduleSelf` call with no scope makes an entry with `Scope == null` for each of the five variants (FR-007); (b) two EveryStep bookings and two Timer bookings of the same sequence with different scopes keep only the scope of the second call (FR-010).
- [X] T017 [P] [US3] In `tests/unit/Queues/QueueRunHandleTimerFiringTests.cs`, add a test for the spec edge case "liveness gate" (research R-004): add a Timer entry with scope S (`AddTimerFiring`), drain it with `DrainDueTimerFirings`, put it back with `RearmTimerFiring`, and drain it again. Assert that the drained entry has `Scope` that is the same object (`BeSameAs`) as S. This test is a regression guard: it passes before and after the fix, because `SelfRescheduleEntry.Scope` already exists.
- [X] T018 [US3] In `tests/unit/Queues/QueueParameterScopeTests.cs`, add a test: the handler calls `h.Coordinator.ScheduleSelf(...)` with no scope. Assert that the booked firing gets the queue scope (the entry layer is not in it), the same as before this change (FR-007). Add a second test for FR-008: use a queue with no cycling and one template entry with `parameterValues` `slot = one`. On the first firing, the handler calls `h.Coordinator.ScheduleSelf(...)` OncePerRun with the scope of that firing (from `h.Sequences.Scopes`). Then, in the same handler call, it calls `h.Templates.UpdateAsync(...)` with a new `QueueTemplate` object that has the same `Id` and one entry with `parameterValues` `slot = two`. Assert that the booked firing resolves `slot` to `one` from the entry layer (`ParameterScopeLayers.Entry`), and not to `two`. This form is deterministic: `QueueExecutionService.StartAsync` reads the template one time at the start, `FakeTemplateRepository.UpdateAsync` replaces the stored object, and `ParameterScope.Child` copies the values. Before the fix, the test fails because the booked firing gets the queue scope only. After the fix, the FR-008 test is a regression guard: it makes sure that a later template edit does not change a booked run.
- [X] T019 [US3] Run all current tests with no change to their expected results: `dotnet test "C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj" --filter "FullyQualifiedName~Queues|FullyQualifiedName~Sequences|FullyQualifiedName~Parameter"` and `dotnet test "C:\src\GameBot\tests\integration\GameBot.IntegrationTests.csproj" --filter "FullyQualifiedName~Queues"`. Compare with the baseline of T001.

**Checkpoint**: All user stories work, and the other runs have no change.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Living documentation (Constitution V) and the final gate.

- [X] T020 [P] In `docs/architecture.md`, change the self-reschedule paragraph: a booked run keeps the parameter scope of the run that booked it (the scope that the queue run loop gave that run), for all four booking options, and a chain keeps it for each generation. Update the "Last reviewed" line to 2026-09-29. Use STE.
- [X] T021 [P] In `specs/078-sequence-parameters/spec.md`, set the Status line to "Implemented (iterated by 116)" (feature 078 FR-015 had this rule, but the code did not do it until 116).
- [X] T022 [P] In `specs/STATUS.md`, set the status of row 078 to "Implemented (iterated by 116)". Add row 116 with the status "Implemented": `| 116 | Keep the parameter scope on a run that reschedule-self books | Implemented |` (issue #249).
- [X] T023 [P] In `specs/116-reschedule-keeps-params/spec.md`, set the Status line from "Draft" to "Implemented".
- [X] T024 Run the full gate: `dotnet build "C:\src\GameBot\GameBot.sln" -c Debug` with no new analyzer warnings, then `dotnet test "C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj"` and `dotnet test "C:\src\GameBot\tests\integration\GameBot.IntegrationTests.csproj"`. The web UI does not change, so its gate does not apply.
- [X] T025 Do section 1 of `specs/116-reschedule-keeps-params/quickstart.md` (automated tests) and make sure that the result is as the quickstart states. Section 2 (live service) needs a deploy; it is not part of this pipeline. T006 and T007 are the automated proxy for SC-001. Do the live reproduction after the deploy.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Phase 1. BLOCKS all user stories (the tests do not compile without the new parameter).
- **User Story 1 (Phase 3)**: Depends on Phase 2.
- **User Story 2 (Phase 4)**: Depends on the fix of Phase 3 (T009, T010). Its tests share the file `QueueParameterScopeTests.cs` with T005.
- **User Story 3 (Phase 5)**: Depends on Phase 2. T016, T017, and T018 can start after Phase 2, but T019 must run after Phase 3 and Phase 4.
- **Polish (Phase 6)**: Depends on all user stories.

### User Story Dependencies

- **US1 (P1)**: No dependency on other stories. This is the MVP.
- **US2 (P1)**: Uses the fix of US1. It adds tests only.
- **US3 (P2)**: Regression guard. Independent tests, but the final run (T019) needs the full fix.

### Within Each User Story

- Tests first. Make sure that they fail (T008) before the fix.
- Coordinator (T009) and dispatch (T010) before the green run (T012).
- Tasks that edit the same file are not [P] with each other: T005, T013, T014, and T018 all edit `tests/unit/Queues/QueueParameterScopeTests.cs`; T004 and T016 both edit `tests/unit/Queues/SelfRescheduleCoordinatorTests.cs`; T006 and T007 both edit `tests/integration/Queues/SelfRescheduleRunIntegrationTests.cs`; T003 and T009 both edit `SelfRescheduleCoordinator.cs`.

### Parallel Opportunities

- T004, T005, T006: three different test files. T007 follows T006 (same file).
- T011 in parallel with T009 or T010 (different files).
- T016 and T017 in parallel with T013 or T014 (different files).
- T020, T021, T022, T023: four different documentation files.

---

## Parallel Example: User Story 1

```text
# Write the failing tests together (T007 after T006, same file):
Task: "T004 Coordinator keeps the scope for each variant in tests/unit/Queues/SelfRescheduleCoordinatorTests.cs"
Task: "T005 Booked firing resolves the entry value in tests/unit/Queues/QueueParameterScopeTests.cs"
Task: "T006 Real DI graph OncePerRun run of issue #249 in tests/integration/Queues/SelfRescheduleRunIntegrationTests.cs"

# Then the fix, with the doc comment in parallel:
Task: "T009 Set Scope in src/GameBot.Service/Services/QueueExecution/SelfRescheduleCoordinator.cs"
Task: "T011 Doc comment in src/GameBot.Service/Services/QueueExecution/QueueRunHandle.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Do Phase 1 (baseline) and Phase 2 (contract).
2. Do Phase 3: failing tests, then the fix.
3. **STOP and VALIDATE**: T012 is green. Issue #249 is fixed for one booking.

### Incremental Delivery

1. Setup + Foundational: the contract is ready, the behavior is the same.
2. US1: the fix and its tests (MVP).
3. US2: the chain tests prove FR-004 and SC-002.
4. US3: the regression tests prove FR-005, FR-007, FR-008, FR-009, FR-010, SC-004, and the held-booking edge case.
5. Polish: docs, status rows, spec status, full gate.

---

## Notes

- There is no change to the API, the `reschedule-self` payload, the persisted template, or the execution log (FR-006, FR-009). Do not add a field.
- Do not add a contract test that persists queues: the contract tests share a data directory (research R-006).
- Do not commit in this pipeline step. The pipeline commits later.
