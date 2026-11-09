# Tasks: Run Next for Unscheduled Tasks

**Input**: Design documents from `/specs/184-showdown-run-next/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/run-next-api.md, contracts/editor-icons-contract.md, quickstart.md

**Tests**: Required. Constitution Principle II needs tests for all executable logic.

**Format**: `- [ ] T### [P?] [Story?] Description with file path`

**Conventions**: Do not use underscores in method names. Write all new text and comments in STE. Test names use CamelCase.

## Phase 1: Setup

- [X] T001 Read these files. Make no change.
  - `src/GameBot.Service/Services/QueueExecution/QueueRunHandle.cs`: the live-schedule register and the lock rules.
  - `src/GameBot.Service/Services/QueueExecution/QueueExecutionService.cs`: `ScheduleRelative`, the live-schedule step near line 688, `HasPendingRelativeOrLive`, and the operator pause gate.
  - `src/GameBot.Service/Services/QueueExecution/QueueMonitorService.cs`: `BuildUpcoming`.
  - `src/GameBot.Service/Services/QueueExecution/QueueRunSchedule.cs`: `RemainingOncePerRun`, `RelativeFired`, `OncePerRunCompletedThisCycle`.
  - `src/web-ui/src/components/queues/IconButton.tsx` and `SchedulingSequenceCard.tsx`.

---

## Phase 2: Foundational

**Purpose**: Types that all user stories use. They add no behavior.

- [X] T002 [P] Add the value `RunNext` to the `ScheduleKind` enum in `src/GameBot.Service/Services/QueueExecution/QueueMonitorSnapshot.cs`. Add the same value to the `ScheduleKind` type in `src/web-ui/src/services/queues.ts`.
- [X] T003 [P] Add `RunNextRequest` and `RunNextResponse` in `src/GameBot.Service/Contracts/Queues/RunNextRequest.cs`. Take the fields from `contracts/run-next-api.md`. Add XML comments with inputs, outputs, and error modes.
- [X] T004 [P] Add the field `EntryStates` to `src/GameBot.Service/Contracts/Queues/QueueMonitorResponse.cs`. It maps an entry ID to a state text. In `src/web-ui/src/services/queues.ts`, add the type `EntryState = 'running' | 'queued' | 'scheduled' | 'idle'` and the field `entryStates?: Record<string, EntryState>`.
- [X] T005 [P] Add the icon `runNext` to `ICON_PATHS` in `src/web-ui/src/components/queues/IconButton.tsx`. Use a play arrow with a bar, as a 24 px path. Add it to the icon list test in `src/web-ui/src/components/queues/__tests__/IconButton.test.tsx`.

**Checkpoint**: The solution builds. No behavior changed.

---

## Phase 3: User Story 1 - Run an unscheduled task next (Priority: P1) MVP

**Goal**: The operator presses **Run next** on an idle entry of a running queue in the desktop editor. The entry runs once, right after the running sequence ends.

**Independent Test**: Start a queue with an enabled idle entry. Call run-next for it. The entry runs once. No other entry changes. A second call adds no second run.

### Tests for User Story 1

> Write these tests first. They MUST fail before T013 to T020.

- [X] T006 [P] [US1] Add handle tests in `tests/unit/Queues/QueueRunHandleRunNextTests.cs`. Check these cases:
  - Adding a request works.
  - A second add for the same entry returns "already queued" and adds nothing.
  - Requests keep the order of adding.
  - Taking the requests returns them in order.
  - Putting back a held request keeps its place at the front.
  - `HasPendingSelfRescheduleWork` is true while a request waits.
  - The register is empty after the run clears it.
- [X] T007 [P] [US1] Add loop tests in `tests/unit/Queues/QueueExecutionServiceRunNextTests.cs`. Check these cases:
  - A request for an idle entry runs the sequence once, with the entry parameter values (FR-005).
  - A request does not change the timers or the once-per-run state of other entries (FR-006).
  - A request made during a once-per-run pass of many entries fires right after the running sequence, not after the whole pass (FR-004).
  - A request made while no sequence runs fires in the next loop pass.
  - A request made during a failure-policy pause or an operator pause fires only after the resume.
  - A non-cycling run stays alive for a pending request and ends after it fires.
  - A failed run of the entry does not stop the queue. The entry can be queued again.
  - A device-liveness hold puts the request back (feature 106 rule).
  - A stop clears the register. A new start has no request (FR-007).
  - A template edit that is not applied does not change the entry that runs. The run uses the entry of the running plan.
- [X] T008 [P] [US1] Add monitor tests in `tests/unit/Queues/QueueMonitorServiceRunNextTests.cs`. Use one case for each row of the table in `data-model.md` ("State of an entry by schedule type"). Also check these cases:
  - The entry that fires now has the state `running`.
  - An entry with a request has the state `queued`.
  - A disabled entry has no state.
  - The map is empty when the queue does not run.
  - A queued request shows in `upcoming` with kind `RunNext`, reason "Run next", and label "next". It comes before the other items.
- [X] T009 [P] [US1] Add contract tests in `tests/contract/Queues/QueueRunNextApiContractTests.cs`. Write one test for each row of the response table in `contracts/run-next-api.md`. Check that each error message is not empty.
- [X] T010 [US1] Add one integration test in `tests/integration/Queues/QueueRunNextEndToEndTests.cs`. Use a template with two once-per-run entries and one far timer.
  - Start the queue and wait until both entries end.
  - Call run-next for the first entry.
  - Check that the execution log has a second firing of its sequence.
  - Check that the log has one line for the request, with the queue ID, the entry ID, and the request time (FR-010).
  - Check that the second entry did not run again.
- [X] T011 [P] [US1] Add Vitest tests in `src/web-ui/src/components/queues/__tests__/RunNextButton.test.tsx` for the card in the editor. Check these cases:
  - The state `idle` gives an on button with the name `Run next <label>`.
  - The states `queued`, `running`, and `scheduled` give an off button with the tooltips from `contracts/editor-icons-contract.md`.
  - A press calls `runNextEntry` once. The button stays off until the call returns.
  - An error shows its text.
  - Four cases give an off button. The row shows the reason as text.
  - The four cases are: a new row, a row with no entry ID, a disabled entry, and a stopped queue.
  - The row mark shows "Queued to run next" or "Running".
- [X] T012 [P] [US1] Add a test in `src/web-ui/src/components/queues/__tests__/RunningQueueEditor.test.tsx`. The editor reads the monitor and passes the state to the rows. It calls `runNextEntry` with the entry ID. It reads the monitor again after a success. It sends no call when the connection is lost.

### Implementation for User Story 1

- [X] T013 [US1] In `src/GameBot.Service/Services/QueueExecution/QueueRunHandle.cs`, add the register. It is a list of `PendingRunNext` records under a lock. A record has the entry ID, the sequence ID, the scope, and the requested time. Do not use the name `RunNextRequest` here. That name is the API type of T003.
  - Add the methods `TryAddRunNext`, `SnapshotRunNext`, `TakeRunNext`, and `PutBackRunNext`.
  - `TryAddRunNext` returns false for a repeat.
  - Add the register to `HasPendingSelfRescheduleWork`.
  - Add XML comments (Principle I).
- [X] T014 [US1] In `IQueueExecutionService.cs` and `QueueExecutionService.cs`, add `RunNext(string queueId, string entryId)`. Add the enum `RunNextOutcome` with the values `Queued`, `AlreadyQueued`, `NotRunning`, `EntryNotAvailable`, `EntryRunning`, and `EntryScheduled`.
  - Find the entry in the plan of the run (`handle.Schedule.Plan`).
  - Build the scope with the same rule as `EntryScope`.
  - Get the state with the class of T015.
  - Write one information log line for each accepted request (FR-010). The line has the queue ID, the entry ID, the sequence ID, and the request time.
- [X] T015 [US1] Add the class `QueueEntryStateReader` in `src/GameBot.Service/Services/QueueExecution/QueueEntryStateReader.cs`. It gives the state of an entry (`running`, `queued`, `scheduled`, or `idle`) by the table in `data-model.md`. The service call (T014) and the monitor (T017) MUST use this class. Then the two cannot disagree.
- [X] T016 [US1] In `QueueExecutionService.cs`, add one private method that drains the register. Call it at the live-schedule step. Call it also after each firing group in the once-per-run pass.
  - Each call comes after the pause gates.
  - The method takes the requests in order.
  - It fires each request with `FireGroupAsync(sequenceId, scope, beforeEachRun: true)`. Pass no `originActionId`.
  - It puts a held request back with `PutBackRunNext`.
  - It counts the run in `executed` and `failed`, and it calls `handle.Cycles.RecordEntry`.
  - It runs the every-step pass after the firing, as the live-schedule step does.
  - Add the register to `HasPendingRelativeOrLive`.
  - Clear the register where the run clears its other registers at the end.
- [X] T017 [US1] In `QueueMonitorService.cs`, fill `EntryStates` with the class of T015. Add the queued requests to `BuildUpcoming` as `RunNext` items, before the other items. If `QueuesEndpoints.cs` maps the monitor response, update the mapping.
- [X] T018 [US1] In `src/GameBot.Service/Endpoints/QueuesEndpoints.cs`, add `POST {id}/run-next`. Use the validation order in `data-model.md` (404, 400, then the outcome of T014). Use the same error helper as the live-schedule call. Write STE messages that say what the operator can do. Add the call and the new fields to `src/GameBot.Service/Swagger/SwaggerConfig.cs`.
- [X] T019 [US1] In `src/web-ui/src/services/queues.ts`, add `runNextEntry(queueId, entryId)` and its result type. The function posts to `/run-next`. It MUST NOT retry (mobile principle 10).
- [X] T020 [US1] In `SchedulingSequenceCard.tsx`, add the props `entryState`, `onRunNext`, and `runNextReason`.
  - Show the **Run next** `IconButton` with the icon `runNext` and the name `Run next <label>`.
  - Show the row mark. Show the reason of an off button as text on the row.
  - Pass the props through `QueueSchedulingAreas.tsx`.
  - In `RunningQueueEditor.tsx`, poll the monitor with `usePolling` (5 s, only when the connection is up). Keep `entryStates`. Call `runNextEntry`. Read the monitor again after a success. Show the error text after a failure.
  - In `src/web-ui/src/pages/QueuesPage.tsx`, the page renders `QueueSchedulingAreas` for a stopped queue. Show the button off there, with the reason "Start the queue to use Run next".
  - Add the CSS for the mark next to the component.

**Checkpoint**: T006 to T012 pass. The desktop flow works from end to end.

---

## Phase 4: User Story 2 - Use Run next from a phone (Priority: P1)

**Goal**: The operator runs an idle entry again from the phone queue detail.

**Independent Test**: Open the queue detail at 375 px. Press **Run next** on an idle row. The row shows "Queued to run next". The page has no sideways scroll.

### Tests for User Story 2

> Write these tests first. They MUST fail before T024 to T026.

- [X] T021 [P] [US2] Add Vitest tests in `src/web-ui/src/components/queues/__tests__/PhoneQueuesView.test.tsx`. Check these cases:
  - An idle row of a running queue shows the **Run next** button.
  - The button is off when the state is not `idle`, when the queue does not run, and when the connection is lost.
  - A press calls `runNextEntry` once. After the next poll, the row summary shows "Queued to run next".
  - No call is sent again after the connection returns (principle 10).
  - A disabled row has no button.
- [X] T022 [P] [US2] Add tests for `buildScheduleRows` in `src/web-ui/src/components/queues/__tests__/queueActivity.test.ts`. Each row carries the entry ID and the entry state from `entryStates`. The summary text is "Queued to run next" for `queued`, "Running" for `running`, and "Scheduled" for `scheduled`. A row of an old template with no entry ID has no state.
- [X] T023 [P] [US2] Add a layout test for the phone row in `PhoneQueuesView.test.tsx`. The button has the class that gives a 44 x 44 px target. A long name has the ellipsis class (principle 7).

### Implementation for User Story 2

- [X] T024 [US2] In `src/web-ui/src/components/queues/queueActivity.ts`, add `entryId` and `entryState` to the rows of `buildScheduleRows`. Add the summary text for `queued`, `running`, and `scheduled`.
- [X] T025 [US2] In `src/web-ui/src/components/queues/PhoneQueuesView.tsx`, add the **Run next** `IconButton` to each schedule row, next to the enable switch.
  - Call `runNextEntry`, then call `loadDetail()`.
  - Keep the button off when `lost` or `busy` is true, or when the state is not `idle`.
  - Show the error text in the existing message area.
- [X] T026 [US2] Add the CSS for the row button in the phone CSS file next to `PhoneQueuesView`. The target is at least 44 x 44 px. Put each `:hover` rule inside `@media (hover: hover)`. Check that the page has no sideways scroll at 320 px.

**Checkpoint**: T021 to T023 pass. The phone flow works.

---

## Phase 5: User Story 3 - Compact icon buttons in the editor (Priority: P2)

**Goal**: All action buttons of the editor page are icon buttons in the style of the Queues list. A sequence row uses fewer lines.

**Independent Test**: Open the template editor with 10 entries at 1024 px. Each row uses 1 line. Each action is an icon with a name and a tooltip. The editor tests pass.

### Tests for User Story 3

> Write these tests first. They MUST fail before T030 to T033.

- [X] T027 [P] [US3] Add Vitest tests in `src/web-ui/src/components/queues/__tests__/SchedulingSequenceCard.icons.test.tsx`. Check these cases:
  - **Parameters** and **Remove** are `IconButton`s with the names `Parameters for <label>` and `Remove <label>`, and the tooltips `Parameters` and `Remove`.
  - Remove has the destructive tone.
  - Parameters keeps `aria-expanded`.
  - The card has no visible button text.
  - The form fields and the enable switch are still there.
- [X] T028 [P] [US3] Add tests for the other buttons of the editor page in `QueueTemplateControls.test.tsx` and `QueueSchedulingAreas.test.tsx`. Each button is an `IconButton` with an accessible name and a tooltip. Each keeps its function (save, reload, load, add). Add an audit test. It finds each `button` on the editor page, for a running and a stopped queue. It fails when a button has visible text and no icon.
- [X] T029 [P] [US3] Add a browser test in `src/web-ui/tests/e2e/editor-row-height.spec.ts`. Run it with `npm run e2e` (the project uses `@playwright/test`). Pass `--trace off`.
  - Open the template editor at 1024 px with 10 entries.
  - Check that each row is one line.
  - Check that the row height is at most half of the old height (SC-004).
  - Measure the old height one time on `master`, with a `git worktree` copy. Do not use `git stash`. Write the value as a constant in the test.

### Implementation for User Story 3

- [X] T030 [P] [US3] Add the icons `parameters`, `remove`, `add`, `save`, `reload`, and `load` to `ICON_PATHS` in `IconButton.tsx`. The `remove` icon can use the `delete` path with its own name. Update the icon list test in `IconButton.test.tsx`.
- [X] T031 [US3] In `SchedulingSequenceCard.tsx`, change the **Parameters** and **Remove** buttons to `IconButton`. Keep the names and `aria-expanded`. Keep the form that opens below the row. Add the optional prop `ariaExpanded` to `IconButton.tsx`, and add a case for it in its test.
- [X] T032 [US3] Change the other buttons of the editor page to `IconButton`. Find them in `QueueTemplateControls.tsx`, `QueueSchedulingAreas.tsx`, `SchedulingArea.tsx`, `RunningQueueEditor.tsx`, and `QueueForm.tsx`. Keep each accessible name that exists today, so tests that find a button by name stay valid. Keep **Save** with an icon, a tooltip, and the same accessible name.
  - Done for: Add, Save Template, Reload Template, Unlink, and Link template (`QueueSchedulingAreas.tsx`, `QueueTemplateControls.tsx`, `QueueGameControls.tsx`, `RunningQueueEditor.tsx`).
  - Not changed, on purpose: Save and Cancel of the queue form (`QueueForm.tsx`) stay text buttons. They are form buttons, not template editor buttons, and the create form uses them too.
  - Not changed, on purpose: Pause, Resume, and Review changes (`QueuePauseControls.tsx`, `TemplateChangesNotice.tsx`) stay text buttons. The phone view shares them, and the phone uses text buttons of 44 px.
- [X] T033 [US3] Change the row layout in the CSS of `SchedulingSequenceCard`. The row does not wrap at 1024 px or wider. Use one flex row with a gap. Cut the name with an ellipsis. Give the controls a fixed size. The parameter form opens below the row. Do not change the layout at the phone width.
- [X] T034 [US3] Run the full web UI test suite. Some tests find an editor button by its text. Change each such test to find the button by its accessible name. Do not remove a test.

**Checkpoint**: T027 to T029 pass. The full web UI suite is green.

---

## Phase 6: Polish and Cross-Cutting Concerns

- [X] T035 [P] Add a timing test for the run-next call in `tests/unit/Queues/QueueExecutionServiceRunNextTests.cs`. Check that 1000 calls for idle entries on one handle take less than 100 ms each (p95). Write a perf note for the PR text (Principle IV). The note says that the drain adds one empty-list check for each firing group.
- [X] T036 [P] Review all new UI text and API messages. Check that each text obeys STE (Principle VI, FR-014). Fix each violation. Write the result in the PR text.
- [X] T037 [P] Update `docs/architecture.md`. Add the new call, the register, `entryStates`, and the `RunNext` kind. Refresh the "Last reviewed" date (Principle V).
- [X] T038 [P] Update `docs/mobile-ui-principles.md`. Add **Run next** to "Queue activity on a phone", with the phone support level `full`. Refresh "Last reviewed" with feature 184.
- [X] T039 [P] Add a `CHANGELOG.md` entry in STE for **Run next** and the editor icon buttons. Mention the version bump.
- [X] T040 [P] Set the `Status` line of `specs/184-showdown-run-next/spec.md` to `Implemented`. Add the feature to `specs/STATUS.md` (Principle V).
- [X] T041 Raise `patch` in `installer/versioning/version.override.json` by the rule in CLAUDE.md. Run `git fetch origin master` and compare with `origin/master`. Set `patch` to the `patch` on master plus 1, or to 0 when `minor` or `major` changed. Set `updatedBy` and `updatedAtUtc`.
- [X] T042 Run `scripts/ci/measure-shards.ps1` for the new test files (see `docs/ci-time.md`). Split each shard that is more than 20% above the average of its project.
  - Result: the script was not run. The rule asks for a measure when a change adds many tests. The new tests add about 7 s in total (unit about 1 s, contract about 4 s, integration about 2 s), far below 1 % of any shard. No split is needed.
- [X] T043 Run `dotnet build` and `dotnet test` for the solution. Run `npm run lint` and `npm run test` in `src/web-ui`. All MUST pass. Check that the new code has 80% line coverage and 70% branch coverage or more.
- [ ] T044 Run the checks in `quickstart.md` by hand. (Open: the automatic tests cover the editor layout, the phone row, and the API. A person still does the check on a real phone and in the real UI.) Check the desktop flow, the phone flow at 375 px, and the editor layout at 1024 px. Check the phone list in `docs/mobile-ui-principles.md`.

---

## Dependencies and Execution Order

- **Phase 1** has no dependency.
- **Phase 2** depends on Phase 1. It blocks all user stories.
- **US1** depends on Phase 2.
- **US2** depends on Phase 2 and on T013 to T019 (the service and the monitor field).
- **US3** depends on Phase 2. It shares `SchedulingSequenceCard.tsx` with T020, so do T020 first.
- **Phase 6** depends on all user stories.
- Inside a story, write the tests first. They MUST fail.
- Then do the tasks in this order:
  - The handle (T013) and the state class (T015).
  - The service (T014) and the loop (T016).
  - The monitor (T017) and the endpoint (T018).
  - The UI (T019, T020).

## Parallel Opportunities

- T002 to T005 touch different files. They can run together.
- T006 to T009, T011, and T012 touch different test files. They can run together.
- T021 to T023, and T027 to T029, can run together.
- T035 to T040 can run together.

## Implementation Strategy

- **MVP**: Phase 1, Phase 2, and US1. This gives the working desktop flow and the service call that US2 uses.
- **Next**: US2 (phone), then US3 (icons).
- Stop at each checkpoint and run the tests. Commit after each task or small group of tasks.
