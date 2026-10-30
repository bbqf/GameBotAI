# Tasks: Apply template changes to a running queue

**Input**: Design documents from `/specs/171-live-template-changes/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/api-changes.md, quickstart.md

**Tests**: Tests are required. The constitution (Principle II) needs tests for all executable logic. Write each
test first. Check that it fails. Then write the code.

**Organization**: Tasks are grouped by user story. Phase 2 holds the shared base (entry ID, run plan, upgrade).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an open task)
- **[Story]**: The user story of the task (US1 to US5)
- Use absolute paths in commands. Use CamelCase method names (no underscore).

## Phase 1: Setup

- [X] T001 Run `dotnet build` and `dotnet test` for `C:\src\GameBot` and `npm test` in `C:\src\GameBot\src\web-ui`. Record that the baseline is green. Stop if a run is red (constitution gate).

---

## Phase 2: Foundational (blocks all user stories)

**Purpose**: Entry ID in the stored template, the run plan, the schedule keyed by entry ID, and the manual
upgrade. This phase does not change how a queue behaves. All existing queue tests stay green.

**CRITICAL**: No user story starts before this phase ends.

- [X] T002 [P] Write tests for `EntryId` and `NeedsUpgrade` (JSON omits an empty `entryId`; an old file loads with empty IDs and `NeedsUpgrade` true; an empty template does not need an upgrade) in `C:\src\GameBot\tests\unit\QueueTemplates\QueueTemplateEntryIdTests.cs`
- [X] T003 Add `EntryId` (string, omitted when empty) to `C:\src\GameBot\src\GameBot.Domain\QueueTemplates\QueueTemplateEntry.cs` and the computed `NeedsUpgrade` to `C:\src\GameBot\src\GameBot.Domain\QueueTemplates\QueueTemplate.cs`. Check that `FileQueueTemplateRepository` round-trips both.
- [X] T004 [P] Write tests for the upgrade (gives IDs to entries with none; changes no other field; a repeat changes 0 entries; IDs are unique) in `C:\src\GameBot\tests\unit\QueueTemplates\QueueTemplateUpgradeTests.cs`
- [X] T005 Implement `QueueTemplateUpgrade` in `C:\src\GameBot\src\GameBot.Domain\QueueTemplates\QueueTemplateUpgrade.cs` (depends on T003)
- [X] T006 [P] Write integration tests for the ID rules of a template save (new template: all entries get IDs; overwrite of an upgraded template: a supplied ID is kept, a new entry gets an ID; overwrite of an old template: no ID is created; a duplicate ID gives 400) in `C:\src\GameBot\tests\integration\QueueTemplateEntryIdEndpointTests.cs`
- [X] T007 Add `entryId` and `needsUpgrade` to the request, the summary, and the detail of `C:\src\GameBot\src\GameBot.Service\Endpoints\QueueTemplatesEndpoints.cs`. Apply the ID rules of research R-003 (depends on T003, T005).
- [X] T008 [P] Write tests: `SetEntries` keeps the given entry IDs; a new `ReplaceEntries` swaps the entries of a queue in one step; an entry with no ID gets a generated ID in `C:\src\GameBot\tests\unit\Queues\QueueRuntimeStoreEntryIdTests.cs`. Update `QueueRuntimeStoreSetEntriesTests.cs` for the new signature.
- [X] T009 (done as: keep the `SetEntries` overload, add `ReplaceEntries` that keeps given entry IDs) Add `ReplaceEntries` in `C:\src\GameBot\src\GameBot.Domain\Queues\IQueueRuntimeStore.cs` and `QueueRuntimeStore.cs`. Update all callers: `C:\src\GameBot\src\GameBot.Service\Services\QueueExecution\QueueExecutionService.cs`, `C:\src\GameBot\src\GameBot.Service\Endpoints\QueuesEndpoints.cs`, `C:\src\GameBot\src\GameBot.Domain\QueueSettings\QueueSettingsImporter.cs`, and the test kit `C:\src\GameBot\tests\unit\QueueSettings\TestDoubles\QueueSettingsTestKit.cs` (depends on T008).
- [X] T010 [P] Write tests for `QueueRunPlan` (same partitions as the loop made before: at-start, once-per-run, every-step, before-each-run, timer; disabled entries excluded; an entry with no ID gets the key `idx:<position>`) in `C:\src\GameBot\tests\unit\Queues\QueueRunPlanTests.cs`
- [X] T011 Create the immutable `QueueRunPlan` in `C:\src\GameBot\src\GameBot.Service\Services\QueueExecution\QueueRunPlan.cs` (depends on T003, T010)
- [X] T012 Change `QueueRunSchedule` to key all consumed-work state by entry key. Add `Plan` and `SwapPlan(newPlan, droppedEntryIds, clearedEntryIds, renameMap)`. The rename map moves consumed state from `idx:<n>` to a real entry ID (research R-006). Update `ComputeNextDue` to read the plan. File: `C:\src\GameBot\src\GameBot.Service\Services\QueueExecution\QueueRunSchedule.cs`. Update the existing tests that use the schedule (`C:\src\GameBot\tests\unit\Queues\QueueMonitorServiceTests.cs`) (depends on T011).
- [X] T013 Replace the partition locals in `RunAsync` with `QueueRunPlan`. Read the plan at the top of each loop iteration. Keep all behavior. File: `C:\src\GameBot\src\GameBot.Service\Services\QueueExecution\QueueExecutionService.cs` (depends on T012).
- [X] T014 Update `C:\src\GameBot\src\GameBot.Service\Services\QueueExecution\QueueMonitorService.cs` and `QueueCycleLedger.cs` (`SetCurrentEntryIndex`) to use entry keys (depends on T012).
- [X] T015 [P] Write tests: the settings export writes `entryId`; the importer keeps it; a document with no ID makes a template that needs upgrade; a duplicate ID in one template is rejected. Files: `C:\src\GameBot\tests\unit\QueueSettings\QueueSettingsEntryIdTests.cs`
- [X] T016 Carry `entryId` in `C:\src\GameBot\src\GameBot.Domain\QueueSettings\QueueSettingsExporter.cs`, `QueueSettingsImporter.cs`, `SettingsDocument.cs`, and `SettingsDocumentValidator.cs` (depends on T003, T015)
- [X] T017 [P] Write a test that a gamepack seed template gets entry IDs, and that an existing seed template is not changed, in `C:\src\GameBot\tests\unit\Gamepacks\SeedTemplateEntryIdTests.cs`. Then update the seed creation in `C:\src\GameBot\src\GameBot.Domain\Gamepacks\Managed\TemplateEntryBooker.cs` and `C:\src\GameBot\src\GameBot.Domain\Gamepacks\Seeds\SeedPlan.cs`.
- [X] T018 [P] Write contract and integration tests for `POST /api/queue-templates/{id}/upgrade` (dryRun, confirm, `confirm_required`, `unmatched_entries`, repeat gives 0, 404) in `C:\src\GameBot\tests\integration\QueueTemplateUpgradeEndpointTests.cs`. Add a test that the upgrade of a running queue of an old template keeps all consumed state (a fired time-of-day timer, a fired relative timer, a done once-per-run entry, and a pending daily retry) and changes no next due time (FR-027).
- [X] T019 Add the upgrade endpoint and its DTOs in `C:\src\GameBot\src\GameBot.Service\Endpoints\QueueTemplatesEndpoints.cs` and `C:\src\GameBot\src\GameBot.Service\Contracts\Queues\`. For a running queue that uses the template, match the running entries by order and sequence (FR-027) and write the IDs to the runtime entries and the plan in the same step. No schedule changes. Pass the rename map (`idx:<n>` to entry ID) to `SwapPlan` so no consumed state is lost (depends on T005, T009, T012, T018).
- [X] T020 Run `dotnet build` and `dotnet test`. All existing queue tests MUST pass with no change in behavior. Fix any red test before you go on. (done: the unit, integration, and contract projects pass in full; see T059)

**Checkpoint**: Entry IDs, the run plan, and the upgrade work. A queue behaves as before.

---

## Phase 3: User Story 1 - Pause the queue between sequences (Priority: P1) MVP

**Goal**: The operator pauses a running queue at a firing boundary and resumes the same run.

**Independent Test**: Start a queue with a long sequence. Pause. The sequence ends, no new sequence starts, the
state is "paused". Resume. The next due sequence starts.

- [X] T021 [P] [US1] Write unit tests for the operator pause register on the run handle (running to pausing to paused; pause with no running group is paused at once; cancel while pausing; resume; a repeat request is safe; the run end clears it; the `GameBackedOut` flag) in `C:\src\GameBot\tests\unit\Queues\QueueRunHandleOperatorPauseTests.cs`
- [X] T022 [US1] Add the operator pause register, its gate, and `WaitIfOperatorPausedAsync` to `C:\src\GameBot\src\GameBot.Service\Services\QueueExecution\QueueRunHandle.cs`. Add the kind `operator` to `QueuePauseSnapshot.cs` (depends on T021).
- [X] T023 [P] [US1] Write integration tests for the loop gate: no new firing group after the request; the running group (with its every-step helpers) ends first; the pause works between once-per-run entries, between timer firings, and at the iteration top; no idle-pause hold while paused; the sequence time limit (170) ends the running sequence when a pause is requested and then the pause starts; the liveness watch and the device recovery keep running while paused (FR-034); a pause request while a failure-policy pause is in force is accepted (FR-035); a pause request during an idle hold gives "paused" at once, the hold stops early, the game is not brought to the front while paused, and on resume it is brought to the front before the next firing group (FR-036); a pause alone changes no cycle position, no schedule, and no log history (FR-006); the run end clears the pause. File: `C:\src\GameBot\tests\unit\Queues\QueueExecutionServiceOperatorPauseTests.cs`
- [X] T024 [US1] Add the gate call to `FireGroupAsync`, to the daily-retry path, and to the top of the loop iteration in `C:\src\GameBot\src\GameBot.Service\Services\QueueExecution\QueueExecutionService.cs`. Skip the idle-pause hold while the operator pause is in force. In `IdlePauseHoldAsync`, check the operator pause request on each poll tick: on a request, stop the hold early, skip the foreground call, and set `GameBackedOut`. After the gate opens on resume, bring the game to the front when `GameBackedOut` is set, then clear it (FR-036, research R-007). Clear the pause in the `finally` of `RunAsync`. Write a log line for each pause request, pause, cancel, and resume (FR-022) (depends on T022, T023).
- [X] T025 [P] [US1] Write contract and integration tests for `POST /api/queues/{id}/pause`, the extended `POST /api/queues/{id}/resume` (operator pause first, then `failurePolicy`; `kind` in the response; a resume while the state is "pausing" cancels the request), and the health members `operatorPause` and `pauseKind: operator` (`failurePolicy` is reported first when both pauses are in force) in `C:\src\GameBot\tests\integration\QueueOperatorPauseEndpointTests.cs`
- [X] T026 [US1] Implement the pause endpoint, extend resume, and extend the health projection in `C:\src\GameBot\src\GameBot.Service\Endpoints\QueuesEndpoints.cs`, `C:\src\GameBot\src\GameBot.Service\Contracts\Queues\QueueHealthResponse.cs`, and `QueueResumeResponse.cs`. Add the OpenAPI text in `C:\src\GameBot\src\GameBot.Service\Swagger\QueueHealthSchemaFilter.cs` (depends on T022, T025).
- [X] T027 [P] [US1] (done as `AFreshRunAfterAPausedRunIsNotPaused` in `QueueExecutionServiceOperatorPauseTests.cs`) Write an integration test that a restart leaves no queue in "pausing" or "paused" and does not change the `ResumeOnServiceStart` behavior, in `C:\src\GameBot\tests\unit\Queues\QueueResumeOnStartupServiceTests.cs` (add a case)
- [X] T028 [P] [US1] Write Vitest tests for `QueuePauseControls` (Pause, "Pausing (waiting for the running sequence)", "Paused (between sequences)", Resume, hit area at least 44 x 44 px, error text) in `C:\src\GameBot\src\web-ui\src\components\queues\__tests__\QueuePauseControls.test.tsx`
- [X] T029 [US1] Add `pauseQueue` and the new health and resume types to `C:\src\GameBot\src\web-ui\src\services\queues.ts`. Create `C:\src\GameBot\src\web-ui\src\components\queues\QueuePauseControls.tsx`. Show the pause time. Wire it in `C:\src\GameBot\src\web-ui\src\pages\QueuesPage.tsx` and `C:\src\GameBot\src\web-ui\src\components\queues\PhoneQueuesView.tsx` (depends on T026, T028).

**Checkpoint**: US1 works alone. The operator can hold and release a queue.

---

## Phase 4: User Story 2 - Change the entries of a running queue (Priority: P1)

**Goal**: The operator applies enable, disable, parameter, add, and remove changes to a paused queue as one
atomic change.

**Independent Test**: Run a queue with three entries. Pause. Disable one, change the values of a second, add a
fourth, remove the third. Apply. Resume. The queue runs the new set. The run does not restart.

- [X] T030 [P] [US2] Write unit tests for `QueueTemplateDiff` (types `added`, `removed`, `enabled`, `disabled`, `parameters`; the key format; two entries of one sequence stay separate; `baseToken` changes with the template content; an empty diff) in `C:\src\GameBot\tests\unit\Queues\QueueTemplateDiffTests.cs`
- [X] T031 [US2] Implement `QueueTemplateDiff` and the base token in `C:\src\GameBot\src\GameBot.Service\Services\QueueExecution\QueueTemplateDiff.cs` (depends on T011, T030)
- [X] T032 [P] [US2] Write unit tests for `QueueLiveApplyService` (applies only the selected keys; a failing part leaves the plan and the runtime entries unchanged; refuses when not paused, when the token differs, when the template needs upgrade, and when another apply runs; validates the new entries with the template rules; removed entries drop their consumed state and daily retry; disabled entries keep it) in `C:\src\GameBot\tests\unit\Queues\QueueLiveApplyServiceTests.cs`
- [X] T033 [US2] Implement `QueueLiveApplyService`: build the new plan and runtime entries off to the side, validate, then swap both under the handle lock. Write one run log line for each apply with the list of applied difference keys (FR-022). Re-use the validation of the template save (schedule fields, parameter names, image references) from `C:\src\GameBot\src\GameBot.Service\Endpoints\QueueTemplatesEndpoints.cs` and `C:\src\GameBot\src\GameBot.Domain\QueueSettings\QueueTemplateRules.cs`. File: `C:\src\GameBot\src\GameBot.Service\Services\QueueExecution\QueueLiveApplyService.cs` (depends on T009, T012, T022, T031, T032).
- [X] T034 [P] [US2] Write integration tests for the re-plan path of the loop (R-004): after an apply and a resume, the loop reads the new plan; a once-per-run entry that ran in this cycle does not run again; `executed`, `cycles`, and the cycle ledger do not change in the re-plan iteration; a removed entry never runs; an added entry runs; a disabled entry does not run; changed parameters reach the next firing. File: `C:\src\GameBot\tests\unit\Queues\QueueExecutionServiceReplanTests.cs`
- [X] T035 [US2] Implement the re-plan path in `C:\src\GameBot\src\GameBot.Service\Services\QueueExecution\QueueExecutionService.cs`: the gate compares `PlanVersion`; on a change `FireGroupAsync` returns `null` for the rest of the iteration; the end of the iteration skips the cycle completion, sets `oncePerRunPassHeld`, and continues with no wait (depends on T024, T033, T034).
- [X] T036 [P] [US2] Write contract and integration tests for `GET /api/queues/{id}/template-diff` and `POST /api/queues/{id}/apply-template` (all status codes and error codes in `contracts/api-changes.md`; apply while running gives `not_paused`; stale token gives `template_changed`; two applies at once give one `apply_in_progress`) in `C:\src\GameBot\tests\integration\QueueApplyTemplateEndpointTests.cs`
- [X] T037 [US2] Implement both endpoints and their DTOs in `C:\src\GameBot\src\GameBot.Service\Endpoints\QueuesEndpoints.cs` and `C:\src\GameBot\src\GameBot.Service\Contracts\Queues\`. Add the OpenAPI text through a schema filter in `C:\src\GameBot\src\GameBot.Service\Swagger\` (depends on T031, T033, T036).
- [X] T038 [US2] (done with no change of `C:\src\GameBot\specs\openapi.json`: that file has no `/resume` or `/monitor` entry, so it is stale and no test reads it) The live `/swagger/v1/swagger.json` is the contract. `OpenApiBackwardCompatTests` and the new OpenAPI tests check the new members.
- [X] T039 [P] [US2] Write Vitest tests for the `applyTemplate` and `getTemplateDiff` service calls and the error mapping (`not_paused`, `template_changed`, `needs_upgrade`) in `C:\src\GameBot\src\web-ui\src\services\__tests__\queuesApply.test.ts`
- [X] T040 [US2] Add the two calls and their types to `C:\src\GameBot\src\web-ui\src\services\queues.ts` (depends on T037, T039).

**Checkpoint**: US1 and US2 work. The apply works through the API and the services layer.

---

## Phase 5: User Story 3 - Schedules stay the same (Priority: P1)

**Goal**: No schedule or next due time changes unless the operator selects a schedule change.

**Independent Test**: Run a queue with an interval entry and a daily entry. Let time pass. Pause. Change another
entry and apply. Resume. The two entries keep the same next due times.

- [X] T041 [P] [US3] Write integration tests with a fake clock: (a) time-of-day entry, relative entry, and once-per-run entry keep their consumed state and next due time across pause, apply, and resume; (b) an entry that is due during the pause runs at once after the resume with the original due time; (c) a pending daily retry survives; (d) a disabled and re-enabled entry keeps its due time; (e) a new entry starts as the template states and changes no other schedule. File: `C:\src\GameBot\tests\unit\Queues\QueueExecutionServiceScheduleStabilityTests.cs`
- [X] T042 [P] [US3] Write unit tests for the `schedule` difference (type, time of day, and offset changes; not selected by default; with the key selected, the entry gets the new schedule and its consumed state clears; without the key it keeps the old schedule) in `C:\src\GameBot\tests\unit\Queues\QueueTemplateDiffScheduleTests.cs`
- [X] T043 [US3] Add the `schedule` type to `QueueTemplateDiff.cs` and to `QueueLiveApplyService.cs` (opt-in only; clear the consumed state of that entry only) (depends on T031, T033, T042).
- [X] T044 [US3] Fix any defect that T041 finds in `QueueRunSchedule.cs` or in the plan swap. Check with a property test that a swap with no schedule key never changes `ComputeNextDue` for an entry that stays (depends on T041, T043).

**Checkpoint**: The three P1 stories work. The core of the feature is complete.

---

## Phase 6: User Story 5 - Review the change before it is applied (Priority: P2)

**Goal**: The operator sees the differences, selects some, and applies them in the web UI.

**Independent Test**: Pause a queue. Edit the template in several ways. Open **Review changes**. Check the list.
Clear one item. Apply. Only the selected items are in the queue.

- [X] T045 [P] [US5] Write Vitest tests for `ReviewTemplateChangesDialog` (lists each difference with type and name; `schedule` items not selected by default and marked; clear one item; nothing to apply state; apply is off while the queue is not paused with a hint; stale token shows "Review the changes again."; phone layout: one column, hit area at least 44 x 44 px, input font at least 16 px) in `C:\src\GameBot\src\web-ui\src\components\queues\__tests__\ReviewTemplateChangesDialog.test.tsx`
- [X] T046 [US5] Create `C:\src\GameBot\src\web-ui\src\components\queues\ReviewTemplateChangesDialog.tsx` and its styles. Wire a **Review changes** button into `QueuePauseControls.tsx`, `QueuesPage.tsx`, and `PhoneQueuesView.tsx`. After an apply, refresh the entry list and the health (depends on T029, T040, T045).
- [X] T047 [P] [US5] Write Vitest tests for the "Needs upgrade" mark and the upgrade action (preview list, unmatched entries, confirm, repeat shows 0) in `C:\src\GameBot\src\web-ui\src\components\queues\__tests__\QueueTemplateControlsUpgrade.test.tsx`
- [X] T048 [US5] Add the "Needs upgrade" mark and the **Upgrade template** action to `C:\src\GameBot\src\web-ui\src\components\queues\QueueTemplateControls.tsx` and `C:\src\GameBot\src\web-ui\src\services\queueTemplates.ts`. Send `entryId` back on a template save. Merge schedule state by ID when IDs exist, and by position when they do not (`QueueEntryList.tsx`, `schedulingAreas.ts`) (depends on T019, T047).
- [ ] T049 [US5] Run a phone check of Pause, Review changes, Apply, Resume, and Upgrade at a width of 375 px. Record the result in the PR description. Follow `C:\src\GameBot\docs\mobile-ui-principles.md` (depends on T046, T048). **NOT DONE**: this check needs a running service with a device, and no device was available. The CSS tests check the 44 px hit area, the 16 px font, the wrap, and the one column of each new control. Do the check by hand before the release.

**Checkpoint**: The full flow works in the web UI, on desktop and on a phone.

---

## Phase 7: User Story 4 - Change the order of entries (Priority: P3, can be deferred)

**Goal**: The operator moves an entry in the template and applies the new order. No schedule changes.

**Independent Test**: Run a queue with three entries. Pause. Move one entry in the template. Apply. Resume. The
queue runs the new order. No schedule changed.

**Decision gate**: If T050 to T053 add large complexity or a risk to US1 to US3, stop. Defer this phase and
record it in the PR description (the operator allowed this).

- [X] T050 [P] [US4] Write unit tests for the `order` difference (one difference for a new order; key; selected by default; apply puts the plan in the template order; consumed state and next due times stay) in `C:\src\GameBot\tests\unit\Queues\QueueTemplateDiffOrderTests.cs`
- [X] T051 [US4] Add the `order` type to `QueueTemplateDiff.cs` and `QueueLiveApplyService.cs` (depends on T043, T050).
- [X] T052 [P] [US4] Write an integration test: after an order apply and a resume, the once-per-run pass follows the new order, an entry that ran in this cycle does not run again, and `ComputeNextDue` is the same. File: `C:\src\GameBot\tests\unit\Queues\QueueExecutionServiceOrderApplyTests.cs`
- [X] T053 [US4] Fix any loop defect that T052 finds in `QueueExecutionService.cs` and `QueueRunPlan.cs`. Show the `order` item in `ReviewTemplateChangesDialog.tsx` with a Vitest case (depends on T046, T051, T052).

---

## Phase 8: Polish and cross-cutting

- [X] T054 [P] Write `C:\src\GameBot\docs\live-template-changes.md` in STE: the pause, the review, the apply, the resume, the manual upgrade steps with a check of the result (FR-030), and one short paragraph on the difference between "Pause" and "Pause when idle" (FR-033). Link it from `C:\src\GameBot\docs\architecture.md`.
- [X] T055 [P] Update `C:\src\GameBot\docs\architecture.md` (domain model: entry ID and run plan; API surface; persistence: `entryId`; the new pause kind). Refresh its "Last reviewed" date.
- [X] T056 [P] Add a `CHANGELOG.md` entry under `[Unreleased]` / `Added` in STE. Say that the release raises the minor version to 12 and that old templates need the manual upgrade to use the apply action.
- [X] T057 [P] Update `C:\src\GameBot\specs\STATUS.md` and the `Status` line of `C:\src\GameBot\specs\171-live-template-changes\spec.md` to "Implemented". Mark the spec of feature 087 as "iterated by 171" if the resume text changed.
- [X] T058 Set the version in `C:\src\GameBot\installer\versioning\version.override.json`: run `git fetch origin master`, set `minor` to `"12"`, set `patch` to `"0"`, set `updatedBy` and `updatedAtUtc`. Do this before you push (constitution, Version bump).
- [X] T059 Run `dotnet build`, `dotnet test`, and `npm test` with lint. Fix all red results. Check the coverage baseline (80% line, 70% branch) for the touched areas. Time the diff and the apply for a template of 200 entries and the gate wait after a firing group. Put a short perf note in the PR description (constitution, Principle IV). (done: `dotnet build` clean; unit 4443 pass, integration 1003 pass (2 skipped), contract 1177 pass, `npm test` 1259 pass with the coverage thresholds, lint clean on the new files; the diff, build, and plan of 200 entries take 54 ms with the JIT start, in `QueueTemplateDiffPerformanceTests`; `TemplateMatcherBench` failed one time under load and passes alone)
- [ ] T060 Run the steps of `C:\src\GameBot\specs\171-live-template-changes\quickstart.md` on a test queue. Record the result. Check SC-005, SC-006, and SC-008 by hand (they have no time goal). **NOT DONE**: the steps need a queue that runs on a device. The API steps are covered by the integration tests with a hand-built run handle. Do the steps by hand before the release.
- [X] T061 Check all new text (code comments, messages, UI text, docs) for STE. Fix each violation (constitution, Principle VI). (done as a scan for `-ing` forms and long sentences in the new docs, UI text, and messages; the state words "Pausing" and "Paused" are technical names)

---

## Dependencies and execution order

### Phase dependencies

- **Phase 1** has no dependency.
- **Phase 2** depends on Phase 1 and blocks all stories.
- **US1 (Phase 3)** depends on Phase 2.
- **US2 (Phase 4)** depends on Phase 2 and on T022 and T024 of US1 (the pause gate and the pause state).
- **US3 (Phase 5)** depends on US2 (T031, T033).
- **US5 (Phase 6)** depends on US1 (T029) and US2 (T040). The upgrade UI (T047, T048) depends only on T019.
- **US4 (Phase 7)** depends on US3 and US5. It can be deferred.
- **Phase 8** depends on the stories that you ship.

### Order inside a phase

- Write the test, check that it fails, then write the code.
- Domain before service, service before endpoint, endpoint before web UI.
- The loop file `QueueExecutionService.cs` changes in T013, T024, T035, T044, and T053. Do these in order, not in parallel.

### Parallel opportunities

- Phase 2: T002, T004, T006, T008, T010, T015, T017, and T018 (all tests, different files).
- US1: T021, T023, T025, T027, and T028 (tests) can start together.
- US2: T030, T032, T034, T036, and T039 (tests) can start together.
- US5: T045 and T047 can run in parallel. Phase 8 docs (T054 to T057) can run in parallel.

### Parallel example: User Story 1 tests

```text
Task: "Unit tests for the operator pause register in tests/unit/Queues/QueueRunHandleOperatorPauseTests.cs"
Task: "Integration tests for the loop gate in tests/unit/Queues/QueueExecutionServiceOperatorPauseTests.cs"
Task: "Endpoint tests in tests/integration/QueueOperatorPauseEndpointTests.cs"
Task: "Vitest tests in src/web-ui/src/components/queues/__tests__/QueuePauseControls.test.tsx"
```

## Implementation strategy

### MVP first (US1)

1. Finish Phase 1 and Phase 2.
2. Finish US1. Stop and check it by hand (quickstart step 2, pause and resume only).
3. The operator can already hold a queue between sequences.

### Core delivery (P1)

1. Add US2, then US3. The core of the feature is then complete: the operator pauses, applies, and resumes with
   no schedule change.
2. Ship at this point if the review dialog is not ready. The API is complete. The apply works through the API.

### Incremental delivery

1. Add US5 (the review dialog and the upgrade UI).
2. Add US4 (order) only if the decision gate allows it.
3. Finish Phase 8 and open the PR.

## Notes

- The loop re-plan (T035) is the main risk. Do it with the tests of T034 open.
- Each task ends with the build and the touched tests green. Commit after each logical group.
- Do not add an underscore to a method name.
- Write each new message, log line, UI text, and code comment in STE when you write it. T061 is the final check, not the first.
