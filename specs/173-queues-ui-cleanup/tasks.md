# Tasks: Clean up the Queues page on the big screen

**Input**: Design documents in `specs/173-queues-ui-cleanup/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/queues-api-and-ui.md, quickstart.md
**Tests**: Required. The constitution (Principle II) needs tests for all changed behavior. Write each test first. See it fail.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: The task can run in parallel. It uses other files and has no open dependency.
- **[Story]**: The user story of the task (US1 to US7). Setup, Foundational, and Polish tasks have no label.
- All web UI paths are under `src/web-ui/src/`. All text you write is STE (Principle VI).

---

## Phase 1: Setup

- [X] T001 Run the web UI tests (`npm test` in `C:\src\GameBot\src\web-ui`). Run the queue integration tests in `tests/integration/Queues/`. Record that all pass. Stop if a test fails (release blocker). Also measure the width of the Actions column on the old list at a 1280 px window. Write the number in a note for T031.

---

## Phase 2: Foundational (blocks all user stories)

**Goal**: The service changes and the shared activity rule.

- [X] T002 [P] Add a failing integration test in `tests/integration/Queues/QueueOperatorPauseEndpointTests.cs`. `GET /api/queues` returns `operatorPause` for a running queue. It returns no field for a stopped queue.
- [X] T003 Add the optional `OperatorPause` property to `src/GameBot.Service/Contracts/Queues/QueueResponse.cs`. The pause state is in the run handle, not in the runtime store. So give the list route in `src/GameBot.Service/Endpoints/QueuesEndpoints.cs` the `IQueueRunRegistry`. Use `TryGetLiveRun` and `OperatorPauseName` to fill the field. Run T002 until it passes.
- [X] T004 [P] Add a failing integration test in `tests/integration/Queues/QueuesCrudEndpointTests.cs`. `PUT /api/queues/{id}` for a running queue returns 200. It saves name, cycle, idle settings, and resume flag. The emulator serial stays the same.
- [X] T005 Remove the `queue_running` refusal from `PUT {id}` in `src/GameBot.Service/Endpoints/QueuesEndpoints.cs` (about line 101). Keep all other validation. Run T004 until it passes.
- [X] T006 [P] Add a failing unit test in `tests/unit/Queues/QueueSettingsWhileRunningTests.cs`. A setting saved during a run must not change that run. Test cycle execution, pause when idle, and idle threshold. If a test shows a change, report it. Then update research R3, the spec note (FR-019), and T015 for that setting.
- [X] T007 [P] Add failing integration tests in `tests/integration/Queues/QueueEntriesEndpointTests.cs`. `POST {id}/entries` and `DELETE {id}/entries/{entryId}` return 409 `queue_running` for a running queue. The message points to the template.
- [X] T008 Add the running guard to `POST {id}/entries` and `DELETE {id}/entries/{entryId}` in `src/GameBot.Service/Endpoints/QueuesEndpoints.cs`. Check that the existing entry tests pass.
- [X] T009 [P] Add `operatorPause?: OperatorPauseState` to `QueueDto` in `src/web-ui/src/services/queues.ts`.
- [X] T010 [P] Add failing tests for the shared rule in `src/web-ui/src/components/queues/__tests__/queueActivity.test.ts`. Cover the seven texts of data-model.md. Cover the order of checks. Cover a missing monitor.
- [X] T011 Add `buildQueueActivity(queue, monitor)` to `src/web-ui/src/components/queues/queueActivity.ts`. Use the order of checks from data-model.md. Make `buildCardSummary` call it. Run T010 until it passes.

**Checkpoint**: The service and the shared rule are ready. The phone tests still pass.

---

## Phase 3: User Story 1 - Edit a running queue (Priority: P1)

**Goal**: Edit opens for a running queue. The page shows the monitor, the settings, and a template-only editor.

**Independent Test**: Start a queue. Open Edit. Check the monitor and the editor. Change an entry and save. Check that the run goes on and that no queue entry call is sent.

- [X] T012 [P] [US1] Add failing page tests in `src/web-ui/src/pages/__tests__/QueuesPage.runningEdit.spec.tsx`. Edit and the name link are enabled for a running queue. The page shows the monitor and the editor. A template save does not call `addQueueEntry`, `removeQueueEntry`, or `replaceQueueEntries`. A settings save calls `updateQueue`. The emulator field is read-only. The note "Settings apply at the next start." shows. The page stays open when the queue stops. A queue that starts during an edit keeps the unsaved edits.
- [X] T013 [P] [US1] Add failing tests in `src/web-ui/src/components/queues/__tests__/RunningQueueEditor.test.tsx`. The editor reads the entries from the linked template. It keeps the template entry IDs. It sends changes to the template only. Add a test for a queue with no linked template (FR-018). It shows read-only entries, the message "Link a template to edit this queue while it runs.", and a link action.
- [X] T014 [US1] Create `src/web-ui/src/components/queues/RunningQueueEditor.tsx`. It is the template-only editor (research R4). Reuse `QueueSchedulingAreas` and `saveQueueTemplate`. Use `QueueTemplateControls` with the load picker and Reload turned off for a running queue. Run T013 until it passes.
- [X] T015 [US1] Add a `running` prop to `src/web-ui/src/components/queues/QueueForm.tsx`. The emulator stays read-only. The note "Settings apply at the next start." shows near Save.
- [X] T016 [US1] Change `src/web-ui/src/pages/QueuesPage.tsx`. Enable Edit and the name link for a running queue. Replace the monitor-only branch with a running page. It shows the monitor, `QueueForm` with `running`, and `RunningQueueEditor`. Keep the stopped branch as it is. Run T012 until it passes.

**Checkpoint**: A running queue opens in Edit. The stopped page tests pass.

---

## Phase 4: User Story 2 - See the queue activity in the list (Priority: P1)

**Goal**: The Activity column replaces the Emulator column.

**Independent Test**: Show a running queue with a sequence, a waiting queue, and a stopped queue. Check the three texts. Pause one queue. Check "Paused".

- [X] T017 [P] [US2] Add failing tests in `src/web-ui/src/pages/__tests__/QueuesPage.activity.spec.tsx`. There is no Emulator column. The Activity cell shows each text. The text updates after a poll. A failed monitor call keeps the row and shows the status only.
- [X] T018 [US2] Change `src/web-ui/src/pages/QueuesPage.tsx`. Load the monitor of each running queue with `usePolling` every 5 s. Stop when the page is hidden. Replace the Emulator column with the Activity column. Use `buildQueueActivity`. Run T017 until it passes.

**Checkpoint**: The list shows the activity. US1 and US2 are the MVP.

---

## Phase 5: User Story 5 - A clear way to review template changes (Priority: P2)

**Goal**: The Edit page of a running queue shows the pause controls and the notice of waiting changes. The review leaves the list row. This phase comes before the icon work, so the review is always reachable.

**Independent Test**: Edit the template of a running queue. Check the notice "N changes wait". Pause, review, apply, and resume. Check that the notice goes away.

- [X] T019 [P] [US5] Add failing tests in `src/web-ui/src/components/queues/__tests__/TemplateChangesNotice.test.tsx`. The notice and the Review button show only when the count is more than 0. They do not show for a queue with no template. A template that needs an upgrade shows "needs upgrade" with the upgrade panel. The notice reads the diff again after a save and on each poll.
- [X] T020 [US5] Create `src/web-ui/src/components/queues/TemplateChangesNotice.tsx`. It reads `getTemplateDiff`. It opens `ReviewTemplateChangesDialog`. Run T019 until it passes.
- [X] T021 [P] [US5] Add a failing test in `src/web-ui/src/components/queues/__tests__/ReviewTemplateChangesDialog.test.tsx`. The dialog shows one sentence. It says what the dialog compares, what Apply does, and that the queue must be paused first.
- [X] T022 [US5] Add the sentence to `src/web-ui/src/components/queues/ReviewTemplateChangesDialog.tsx`. Do not change the apply behavior (FR-015). Run T021 and the existing dialog tests.
- [X] T023 [US5] Remove the `reviewChanges` flag and the Review button from `src/web-ui/src/components/queues/QueuePauseControls.tsx`. Remove the flag from its use in the list row in `src/web-ui/src/pages/QueuesPage.tsx`. Update `src/web-ui/src/components/queues/__tests__/QueuePauseControls.test.tsx`. The notice now owns the Review button. The phone gets the notice in T036.
- [X] T024 [US5] Show `QueuePauseControls` and `TemplateChangesNotice` at the top of the running Edit page in `src/web-ui/src/pages/QueuesPage.tsx` (FR-017, FR-028). Add a page test in `QueuesPage.runningEdit.spec.tsx` for this flow: pause, review, apply, resume.

**Note**: Between T023 and T036 the phone detail has no Review button. Do T036 before a release.

---

## Phase 6: User Story 3 - Compact icon buttons (Priority: P2)

**Goal**: The row actions are icon buttons with names and tooltips.

**Independent Test**: Check the icons for each queue state. Check the tooltip and the accessible name. Check that each button does what the text button did.

- [X] T025 [P] [US3] Add failing tests in `src/web-ui/src/components/queues/__tests__/IconButton.test.tsx`. Test `aria-label`, `title`, `disabled`, keyboard use, and a different look for the destructive tone.
- [X] T026 [P] [US3] Add failing tests in `src/web-ui/src/components/queues/__tests__/QueueRowActions.test.tsx`. Test the icons for each state in data-model.md. Test the names Start, Stop, Pause, Resume, Cancel pause, Edit, Duplicate, and Delete. Test the accessible name "<Action> <queue name>". Delete is disabled while the queue runs. Delete opens the confirmation dialog.
- [X] T027 [P] [US3] Create `src/web-ui/src/components/queues/IconButton.tsx` with inline SVG icons. Create `src/web-ui/src/components/queues/IconButton.css` with a 32 px button, a focus ring, and the destructive tone. Run T025 until it passes.
- [X] T028 [US3] Create `src/web-ui/src/components/queues/QueueRowActions.tsx`. Pause, Resume, and Cancel pause call `pauseQueue` and `resumeQueue`. Run T026 until it passes.
- [X] T029 [US3] Use `QueueRowActions` in the Actions column of `src/web-ui/src/pages/QueuesPage.tsx`. Keep the duplicate dialog and the delete confirmation. Remove the text buttons and the `QueuePauseControls` use from the row.
- [X] T030 [US3] Change the selectors of the existing page tests from text buttons to icon labels. The files are in `src/web-ui/src/pages/__tests__/`: `QueuesPage.spec.tsx`, `QueuesPage.execution.spec.tsx`, `QueuesPage.templates.spec.tsx`, `QueuesPage.reload.spec.tsx`, `QueuesPage.link.spec.tsx`, `QueuesPage.autoload.spec.tsx`, `QueuesPage.monitor.spec.tsx`, and `QueuesPage.layout.spec.tsx`. Run them until they pass.
- [X] T031 [US3] Add a Playwright test in `src/web-ui/tests/e2e/queues-actions-width.spec.ts`. It measures the width of the Actions column and the height of one row at a narrow window. A jsdom test cannot measure layout. Use the old width from T001 in the test comment. The new width must be at most half of it (SC-002).

---

## Phase 7: User Story 4 - Remove the Schedule button (Priority: P2)

**Goal**: The list has no Schedule button and no live-schedule form.

**Independent Test**: Run a queue. Check that the row has no Schedule button.

- [X] T032 [P] [US4] Add a failing test in `src/web-ui/src/pages/__tests__/QueuesPage.spec.tsx`. A running row has no Schedule button. No live-schedule form opens.
- [X] T033 [US4] Remove the Schedule button, the expanded row, the `liveScheduleFor` state, and the unused imports from `src/web-ui/src/pages/QueuesPage.tsx`. Delete `src/web-ui/src/components/queues/QueueLiveScheduleControl.tsx` and its test. Delete `src/web-ui/src/pages/__tests__/QueuesPage.liveSchedule.spec.tsx`. Keep `liveScheduleSequence` in `src/web-ui/src/services/queues.ts`. Search that no other file imports the deleted files.

---

## Phase 8: User Story 6 - The big screen and the phone agree (Priority: P2)

**Goal**: The phone card and the big screen show the same activity text. They use the same notice and the same names.

**Independent Test**: Put one queue in each of the six states. Compare the phone card with the big screen cell.

- [X] T034 [P] [US6] Add a failing parity test in `src/web-ui/src/__tests__/QueueActivity.parity.test.tsx`. Cover the six states of FR-024. Render the big screen list and the phone list. Check that the activity text is the same. Check that the action names match (FR-022).
- [X] T035 [US6] Change `src/web-ui/src/components/queues/PhoneQueuesView.tsx`. The card summary uses `buildQueueActivity` with `operatorPause` from the list item. Run T034 until it passes. Update `PhoneQueuesView.test.tsx` for "Paused" and "Pausing".
- [X] T036 [US6] In the phone detail of `PhoneQueuesView.tsx`, replace `QueuePauseControls reviewChanges` with `QueuePauseControls` and `TemplateChangesNotice`. Update `PhoneQueuesView.test.tsx`, `PhoneQueuesView.taps.test.tsx`, and `App.phoneHistory.test.tsx`. Check the 44 px tap size of the Review button.

---

## Phase 9: User Story 7 - Turn sequences on and off from the phone (Priority: P2)

**Goal**: A phone switch on a running queue changes the template. The phone shows the waiting change.

**Independent Test**: Run a queue. Turn one entry off on the phone. Check the notice "1 change waits". Apply it. Check that the entry no longer runs.

- [X] T037 [P] [US7] Add failing tests in `src/web-ui/src/components/queues/__tests__/PhoneQueuesView.test.tsx`. A switch on a running queue calls only `saveQueueTemplate`. It does not pause or apply. The notice shows after the next refresh. A stopped queue shows the entry as "disabled". A queue with no template shows read-only switches and a message.
- [X] T038 [US7] Change `src/web-ui/src/components/queues/PhoneQueuesView.tsx`. For a running queue, replace the note "Changes apply when the queue starts again." with the notice (FR-027). Keep the note for a stopped queue. Run T037 until it passes.
- [X] T039 [P] [US7] Add two tests in `src/web-ui/src/pages/__tests__/QueuesPage.runningEdit.spec.tsx` and `PhoneQueuesView.test.tsx`. An entry turned off by a template save shows as disabled in the running editor after a refresh. An entry turned off in the running editor shows as off on the phone switch after a refresh (FR-026).

---

## Phase 10: Polish and cross-cutting

- [X] T040 [P] Update `docs/architecture.md`. Add the list field `operatorPause`, `PUT /api/queues/{id}` for a running queue, and the entry guard. Refresh "Last reviewed".
- [X] T041 [P] Update `docs/mobile-ui-principles.md`. Add the card summary line with the paused states, and the notice on the phone detail. Refresh "Last reviewed". Update `docs/live-template-changes.md` for the new place of the review.
- [X] T042 [P] Set the `Status` of `specs/171-live-template-changes/spec.md` to "Implemented (iterated by 173)". Set the `Status` of this spec to "Implemented" when the work is done. Make `specs/STATUS.md` agree.
- [X] T043 [P] Add a user-visible entry to `CHANGELOG.md`. Cover the icon buttons, the Activity column, Edit for running queues, no Schedule button, and the phone changes. Mention the patch bump.
- [X] T044 Run `scripts/ci/measure-shards.ps1` for each test project that got new test files. Split a shard that is more than 20% above its average (`docs/ci-time.md`).
- [ ] T045 Run all web UI tests, all service tests, lint, and the build. All must be green. Run the mobile e2e spec `src/web-ui/tests/e2e/mobile.spec.ts` if it can run locally.
- [ ] T046 Run the steps of `quickstart.md` on a running service. Add a manual check for SC-004: an operator finds and applies a template change in under 2 minutes. Record the result in the PR description.
- [X] T047 Check all new and changed text for STE (Principle VI). This covers UI text, messages, comments, and docs. Fix each violation.
- [X] T048 Before you push the branch, run `git fetch origin master`. Compare `installer/versioning/version.override.json` with `origin/master`. Set `patch` to the master `patch` plus 1. Use 0 if `minor` or `major` changed. Set `updatedBy` and `updatedAtUtc`.

---

## Dependencies and order

- Phase 1 comes first. Phase 2 blocks all other phases.
- Phases 3 and 4 (US1, US2) both change `QueuesPage.tsx`. Do them one after the other.
- Phase 5 (US5) needs Phase 3. Phase 6 (US3) and Phase 7 (US4) follow it in the same file.
- Phase 8 (US6) needs Phase 2 and Phase 5. Phase 9 (US7) needs Phase 8.
- Phase 10 comes last. T048 comes right before the push.

## Parallel examples

- Phase 2: T002, T004, T006, T007, T009, and T010 use different files. They can start together.
- Phase 2 service tasks T003, T005, and T008 edit one file. Run them one after the other.
- Phase 6: T025 and T026 (tests) can run together. T027 can run with T028 if the files differ.
- Phase 10: T040 to T043 use different files.

## Implementation strategy

1. **MVP**: Phase 1, Phase 2, US1, and US2. The operator edits a running queue and sees the activity in the list.
2. **Next**: US5 (the notice and the review place). Then US3 and US4 (the row cleanup).
3. **Last**: US6 and US7 (the phone). Then Phase 10.
4. Commit after each phase. Run the tests before each commit (Constitution II).
