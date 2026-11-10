---

description: "Task list for feature 185: sleeker queue entries and parameters on the phone"
---

# Tasks: Sleeker Queue Entries and Parameters on the Phone

**Input**: Design documents from `/specs/185-queues-params-mobile/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/ui-contract.md, quickstart.md

**Tests**: The constitution (Principle II) requires tests for executable logic. Each story has test tasks. Write a test first. Check that it fails.

**Organization**: Tasks are grouped by user story. US1 is the big screen. US2 is the phone entry page. US3 is the phone rules for the new page.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: The task can run in parallel (different files, no dependency on an open task).
- **[Story]**: The user story of the task (US1, US2, US3).
- Paths start at the repository root. `WEB` means `src/web-ui/src`.

## Phase 1: Setup

**Purpose**: Check the start state.

- [X] T001 Run `npm --prefix C:\src\GameBot\src\web-ui run test` and `npm --prefix C:\src\GameBot\src\web-ui run lint`. Check that both pass before any change (release blocker gate). Record the result in the PR text.

---

## Phase 2: Foundational (blocks all user stories)

**Purpose**: Shared helpers that US1 and US2 both use.

**CRITICAL**: No story work starts until this phase is complete.

- [X] T002 [P] Write tests in `WEB/components/queues/__tests__/scheduleSummary.test.ts` for `scheduleSummary`. Cover each schedule type. Cover a Timer entry with a time of day. Cover a Timer entry with a relative offset. Cover a Timer entry with no value. Cover an editor entry in relative mode that keeps an old time of day (the offset must win). Cover an editor entry in time-of-day mode that keeps an old offset (the time must win).
- [X] T003 Create `WEB/components/queues/scheduleSummary.ts`. Export `scheduleSummary(entry)` with the texts of data-model.md. The input has `scheduleType`, `timerTimeOfDay`, `timerRelativeOffset`, and an optional `timerMode`. When `timerMode` is set, it decides the field. When it is not set (a template entry), keep the current rule: the time first, then the offset. Move the body of `scheduleText` out of `WEB/components/queues/queueActivity.ts`. Make `queueActivity.ts` call the new function. The existing `queueActivity` tests must stay green with no change. Depends on T002.
- [X] T004 [P] Add the `settings` icon (a 24 px path, same style as the other icons) to `ICON_PATHS` in `WEB/components/queues/IconButton.tsx`. Update the icon list test in `WEB/components/queues/__tests__/IconButton.test.tsx` for the new name.
- [X] T005 [P] Write tests in `WEB/lib/__tests__/queueEntryParameters.test.ts` for `setTemplateEntryParameters(template, index, values)`. Check that only the entry at `index` gets the new values. Check that the other entries keep their fields and order. Check that a binding with a null value is not sent. Check that `overwrite` is true and that each `entryId` is kept. Add a round-trip test: the saved values of the request equal the values that the editor mapper reads back (SC-004).
- [X] T006 Export `toSaveEntry` from `WEB/lib/queueEntryToggle.ts`. Keep `toggleTemplateEntry` as it is. Create `WEB/lib/queueEntryParameters.ts` with `setTemplateEntryParameters`. It uses `toSaveEntry` and sets the `parameterValues` of the entry at `index`. Depends on T005.
- [X] T007 [P] Write tests in `WEB/lib/__tests__/phoneHistory.entry.test.ts` for the entry position. Cover `buildAddress` with an entry. Cover `parseAddress` with `entry=` (kept only when the area is `queues` and `queue` is set). Cover `pushScreen` and `replaceScreen` with an entry. Check that the same position adds no history entry.
- [X] T008 Change `WEB/lib/phoneHistory.ts`. Add the optional `entryId` (a string or null) to `NavigationPosition`. Update `buildAddress`, `parseAddress`, `samePosition`, `withQuery`, and `readState` as data-model.md says. Old callers that pass only `area` and `queueId` must still compile and behave the same. Depends on T007.

**Checkpoint**: The helpers pass their tests. The old tests are still green.

---

## Phase 3: User Story 1 - Shorter entry rows on the big screen (Priority: P1) MVP

**Goal**: The main row of a card is short. The schedule controls and the parameters are in one Entry settings panel.

**Independent Test**: Open a stopped queue with entries of every schedule type. Check the main row. Open a panel. Change the schedule type and the timer. Save the template. Open the queue again. Check the saved values (quickstart.md, "Big screen").

### Tests for User Story 1

- [X] T009 [P] [US1] Create `WEB/components/queues/__tests__/EntrySettingsPanel.test.tsx`. Cover rules D-04 and D-05 and D-10 of the contract. Check the order of the parts. Check the timer controls of a Timer entry in each mode. Check that a sequence with no declaration and no value shows no Parameters part but shows `Add a value`. Check that the button shows the ad-hoc add field. Check that the controls are disabled when the editor is disabled.
- [X] T010 [P] [US1] Create `WEB/components/queues/__tests__/SchedulingSequenceCard.settings.test.tsx`. Cover D-01, D-02, D-03, D-08, D-09, and D-12. Check that the main row has no schedule select. Check the name and `aria-expanded` of the settings button. Check that a new card is closed. Check that the summary follows the schedule. Check that the `Parameters` badge shows on a closed card. Check that the main row has at most 5 controls (SC-001).
- [X] T011 [P] [US1] Create `WEB/components/queues/__tests__/QueueSchedulingAreas.openState.test.tsx`. Cover D-06 and D-07. Change the schedule type of an open card. Check that the card is in the new scheduling area and its panel is open. Open two cards. Check that each opens and closes on its own. Remove an entry. Check that its open state is gone.

### Implementation for User Story 1

- [X] T012 [US1] Create `WEB/components/queues/EntrySettingsPanel.tsx`. It renders the Schedule part. This part holds the type select, the timer mode select, the time input, and the h, m, s offset inputs. Move them from `SchedulingSequenceCard.tsx` and keep their labels. It renders the Parameters part with the shared `ParameterBindingForm` and `allowAdHoc`. Show the Parameters part by rule D-05. Add the `Add a value` button for a sequence with no declaration and no value. Depends on T009 and T003.
- [X] T013 [US1] Change `WEB/components/queues/SchedulingSequenceCard.tsx`. Remove the schedule controls from the main row. Add the summary text (`scheduleSummary`). Add the settings button (`IconButton`, icon `settings`, label `Entry settings for <name>`, title `Entry settings`, `ariaExpanded`). Remove the old `Parameters for <name>` button and the local `parametersOpen` state. Take `open` and `onToggleOpen` as props. Render `EntrySettingsPanel` when `open` is true. Keep the badges, Run next, and Remove. Depends on T012, T004, and T010.
- [X] T014 [US1] Change `WEB/components/queues/SchedulingArea.tsx` and `WEB/components/queues/QueueSchedulingAreas.tsx`. `QueueSchedulingAreas` holds `openEntryIds` (a set). It gives each scheduling area the set and a toggle function. It removes an ID when its entry is gone. `SchedulingArea` passes `open` and `onToggleOpen` to each card. Depends on T013 and T011.
- [X] T015 [US1] Change `WEB/components/queues/QueueSchedulingAreas.css`. Style the slim main row (one line at 1024 px). Style the summary text (muted, cut with an ellipsis). Style the open panel (indent under the row, a separator line, the Schedule and Parameters parts). Add no `:hover` rule outside `@media (hover: hover)`.
- [X] T016 [US1] Update the existing tests that look for the schedule controls or for the old `Parameters for` button. The files are `WEB/components/queues/__tests__/QueueSchedulingAreas.test.tsx`, `QueueSchedulingAreas.parameters.test.tsx`, `EditorIconButtons.test.tsx`, `IconButton.test.tsx`, `WEB/pages/__tests__/QueuesPage.templates.spec.tsx`, and `WEB/components/commands/__tests__/parameterPanelTestHelpers.tsx`. Update `QueueEntryList.test.tsx` only when it renders the new card. Each test opens the panel first with the button `Entry settings for <name>`. Do not change what the tests assert about data. Depends on T014.
- [X] T017 [US1] Update `src/web-ui/tests/e2e/editor-row-height.spec.ts`. Add a Timer entry (time of day) and a Timer entry (relative offset) to the mocked template. Check that the closed row of every card is at most `OLD_ROW_HEIGHT / 2` px at 1024 px (rule D-11, names of up to 30 characters). Change the icon button test to the names `Run next`, `Entry settings for`, and `Remove`. Depends on T015.
- [X] T018 [US1] Run the full web UI test suite and lint. Fix all failures. Check that the editor of a running queue (`RunningQueueEditor`) shows the same cards and panel (quickstart.md, step 10 of the big screen).

**Checkpoint**: US1 works alone. The big screen shows short rows and the Entry settings panel.

---

## Phase 4: User Story 2 - See and change entry parameters on the phone (Priority: P1)

**Goal**: A tap on a schedule row of the phone queue detail opens a page. The operator changes and saves the parameter values there.

**Independent Test**: At a phone width, open a queue. Tap an entry. Change a value. Save. Go back. Open the entry again and check the value. Open the queue on the big screen and check the same value (quickstart.md, "Phone", steps 1 to 6 and 9 to 11).

### Tests for User Story 2

- [X] T019 [P] [US2] Create `WEB/components/queues/__tests__/PhoneEntryPage.test.tsx`. Cover E-01 to E-07, E-11, and E-12. Check the content of the page. Check the effective value text. Check a successful save (one call to `saveQueueTemplate` with the new values). Check a failed save (the error text shows, the values stay, no second call). Check a sequence with no parameters. Check a failed sequence read (the text "The parameters did not load.", plain stored values, Save off). Check a missing entry (message and way back). Check that a poll does not replace typed values.
- [X] T020 [P] [US2] Extend `WEB/components/queues/__tests__/PhoneQueuesView.test.tsx`. Cover P-01 to P-05 and E-06. A row with an entry ID calls `onOpenEntry`. A tap on the switch or on Run next does not. A row with no entry ID is not tappable. The upgrade hint shows only for a template that needs an upgrade. A queue with no linked template shows no extra hint. The row text and the larger-screen text do not change. An entry address of a queue with no linked template shows "Entry not found". Phone mode shows no inline panel (FR-021).
- [X] T021 [P] [US2] Create `WEB/__tests__/PhoneEntryNavigation.test.tsx` (or extend the existing App navigation test). A tap adds one history entry. Back shows the queue detail. A reload of `?area=queues&queue=<id>&entry=<entryId>` shows the page. A desktop-mode window with that address shows the queue editor (rule M-02). Desktop mode never mounts the entry page (FR-021).

### Implementation for User Story 2

- [X] T022 [US2] Create `WEB/components/queues/PhoneEntryPage.tsx` and `WEB/components/queues/PhoneEntryPage.css`. Props: the queue detail, the template (or null), the entry ID, the connection state, `leaveRequest`, `onBack`, and `onSaved(template)`. The page reads the sequence once with `getSequence`. It holds the draft, `dirty`, and `declarationsState` (data-model.md). It shows the schedule with `scheduleSummary` and no control. It renders the shared `ParameterBindingForm` with `allowAdHoc`. The Save button calls `saveQueueTemplate(setTemplateEntryParameters(...))` once. It shows `Saved.` and the error text as the contract says. Use buttons of at least 44 px. Depends on T019, T006, and T003.
- [X] T023 [US2] Change `WEB/components/queues/PhoneQueuesView.tsx`. Add the props `entryId`, `onOpenEntry`, and `leaveRequest`. Make a schedule row with an entry ID tappable. Use a button that covers the text. The switch and Run next stay separate and stop the click. Show the upgrade hint for a row of a template that needs an upgrade. Show `PhoneEntryPage` when `entryId` is set, and pass `leaveRequest` to it. Keep the template read after a save. Keep `setTemplateSaves` so that the notice of waiting changes reads again. Depends on T022 and T020.
- [X] T024 [US2] Change `WEB/components/phone/phone.css` (or the CSS of `PhoneQueuesView`) for the tappable schedule row. Give it a hit area of at least 44 px and a visible focus ring. Add a mark (for example a chevron) that is not the only signal. Put every `:hover` rule inside `@media (hover: hover)`.
- [X] T025 [US2] Change `WEB/App.tsx`. Add `selectedEntryId` state next to `selectedQueueId`. Read it from the address at the start and in the `popstate` handler. Add `handleOpenEntry(queueId, entryId)` that calls `pushScreen({ area: 'queues', queueId, entryId })`. Clear the entry when the queue or the area changes. Add `entry` to the address sync effect (only when the area is `queues` and a queue is open). Pass `entryId`, `onOpenEntry`, and `leaveRequest` to `PhoneQueuesView`. Do not pass them in desktop mode. Depends on T008, T023, and T021.
- [X] T026 [US2] Run the web UI tests and lint. Fix all failures.

**Checkpoint**: US2 works alone. The phone changes and saves a value. The big screen shows it.

---

## Phase 5: User Story 3 - Phone rules for the new page (Priority: P2)

**Goal**: The entry page follows `docs/mobile-ui-principles.md`. It never loses typed values without a question.

**Independent Test**: Open the page at 320, 360, 390, 480, 600, and 768 px, and at 640 x 360. Turn the connection off. Change a value and use the back button. Check each line of the mobile checklist (quickstart.md, "Phone", steps 7, 8, 10, and 12).

### Tests for User Story 3

- [X] T027 [P] [US3] Create `WEB/lib/__tests__/phoneLeaveGuard.test.ts`. With no guard, leave is allowed. A registered guard that returns true blocks. A guard is removed on unregister. A second register replaces the first.
- [X] T028 [P] [US3] Extend `PhoneEntryPage.test.tsx`. Cover E-08 and E-09. Back with unsaved edits opens the dialog. `Keep editing` closes it and keeps the values. `Discard changes` leaves. Back with no edits leaves at once. A new value of `leaveRequest` opens the dialog. With the connection lost, Save is off. When the state returns to online, the page sends no save.
- [X] T029 [P] [US3] Extend `WEB/__tests__/PhoneEntryNavigation.test.tsx`. Test the phone back step with a registered guard. The handler goes forward one step again. The entry page stays. The `popstate` that its own step causes is ignored (no loop). The `leaveRequest` counter goes up.
- [X] T030 [P] [US3] Create `src/web-ui/tests/e2e/phone-entry-page.spec.ts` (Playwright, mocked API routes, same style as `editor-row-height.spec.ts`). For each width in 320, 360, 390, 480, 600, 768 and for 640 x 360, check E-10 (`scrollWidth` is at most `clientWidth`). Check the size of each control (at least 44 x 44 px). Check the font of each text input (at least 16 px).

### Implementation for User Story 3

- [X] T031 [US3] Create `WEB/lib/phoneLeaveGuard.ts` with `setLeaveGuard(fn | null)` and `shouldBlockLeave()`. Depends on T027.
- [X] T032 [US3] Change `PhoneEntryPage.tsx`. Register the guard while `dirty` is true. Remove it on unmount. Add the keep-or-discard dialog with `Keep editing` and `Discard changes`. The on-screen Back button asks the guard first. Open the dialog when `leaveRequest` changes. Turn Save off when the connection is lost (rule E-09). Depends on T031 and T028.
- [X] T033 [US3] Change the `popstate` handler in `WEB/App.tsx`. When `shouldBlockLeave()` is true, call `window.history.go(1)`. Ignore the next `popstate` with a flag. Increase the `leaveRequest` counter. Depends on T031, T032, and T029.
- [X] T034 [US3] Check the safe-area and `dvh` use of `PhoneEntryPage.css`. Use the same wrapper classes as the queue detail. Run the test of T030. Fix any failure in the CSS.
- [X] T035 [US3] Run the web UI tests, the lint, and the two Playwright specs (`editor-row-height` and `phone-entry-page`). Fix all failures.

**Checkpoint**: All three stories work. The phone checklist passes for the entry page.

---

## Phase 6: Polish and Cross-Cutting Concerns

- [X] T036 [P] Update `docs/mobile-ui-principles.md`. Add the entry page to "Queue activity on a phone". Give the values the level `full` and the schedule editor the level `desktop-only`. Change the line "The phone shows no control to create, reorder, or edit a schedule" so that it stays true. Add feature 185 to the "Last reviewed" line and set the date. Write all new text in STE.
- [X] T037 [P] Update `docs/architecture.md`. Describe the Entry settings panel and the phone entry page. Refresh the "Last reviewed" date.
- [X] T038 [P] Update `specs/STATUS.md` and the `Status` lines. Set the Status of `specs/185-queues-params-mobile/spec.md` to `Implemented`. Add "iterated by 185" to the specs of features 173 and 184, where this feature changes their screens.
- [X] T039 [P] Add a user-visible entry to `CHANGELOG.md` in STE. Mention the version bump.
- [X] T040 Bump the version. Run `git fetch origin master`. Compare `installer/versioning/version.override.json` with `origin/master`. If `minor` or `major` changed, set `patch` to `"0"`. If not, set `patch` to the `patch` on `origin/master` plus 1. Set `updatedBy` and `updatedAtUtc`. Do this before the push, with no prompt.
- [X] T041 Check that all new and changed text obeys STE (Principle VI). This covers the UI text, the code comments, the docs, and the artifacts of this feature (`spec.md`, `plan.md`, `research.md`, `data-model.md`, `contracts/ui-contract.md`, `quickstart.md`, and `tasks.md`). Fix each violation.
- [ ] T042 Do the steps of quickstart.md by hand in the browser pane (big screen and a 375 px window). Time the phone change in step 4 (SC-003: under 30 seconds from the queue detail). Record the results in the PR text.
- [X] T043 Run the full web UI test suite, lint, and the Playwright specs one last time. Check the shard balance rule of the constitution (`scripts/ci/measure-shards.ps1`) if the number of test files in a shard changed much.
- [X] T044 Apply each line of the checklist in `docs/mobile-ui-principles.md` to the entry page (FR-017). Mark each line pass or fail. Record the result in the PR text. Fix each failed line.

---

## Dependencies and Execution Order

### Phase Dependencies

- Setup (Phase 1) has no dependency.
- Foundational (Phase 2) depends on Setup. It blocks all stories.
- US1 (Phase 3) and US2 (Phase 4) depend on Phase 2 only. They touch different files, so they can run in parallel. The one shared file is `WEB/components/queues/queueActivity.ts`, and it changes in Phase 2 (T003).
- US3 (Phase 5) depends on US2, because it changes `PhoneEntryPage.tsx` and `App.tsx`.
- Polish (Phase 6) depends on all stories.

### Story Dependencies

- **US1 (P1)**: needs T003 and T004. It does not depend on US2 or US3.
- **US2 (P1)**: needs T003, T006, and T008. It does not depend on US1.
- **US3 (P2)**: needs US2.

### Within Each Story

- Write the tests first. Check that they fail.
- Write the helpers before the components. Write the components before the page and the App wiring.
- Update the old tests (T016) after the card change.

### Parallel Opportunities

- Phase 2: T002, T004, T005, and T007 can run together. T003, T006, and T008 follow their tests.
- US1 tests T009, T010, and T011 can run together.
- US2 tests T019, T020, and T021 can run together.
- US3 tests T027, T028, T029, and T030 can run together.
- The doc tasks T036 to T039 can run together.
- US1 and US2 can run in two sessions. Each session must touch only its own files.

## Parallel Example: User Story 1

```text
# Start the three US1 test tasks together:
Task: "Create EntrySettingsPanel.test.tsx in WEB/components/queues/__tests__/"
Task: "Create SchedulingSequenceCard.settings.test.tsx in WEB/components/queues/__tests__/"
Task: "Create QueueSchedulingAreas.openState.test.tsx in WEB/components/queues/__tests__/"
```

## Implementation Strategy

### MVP First (US1 only)

1. Complete Phase 1 and Phase 2.
2. Complete Phase 3 (US1).
3. Stop. Check US1 by hand with quickstart.md ("Big screen"). The big screen is better, and no phone code is needed.

### Incremental Delivery

1. Do the foundation, then US1 (big screen). Check it.
2. Add US2 (phone page). Check it in a 375 px window.
3. Add US3 (leave guard and phone rules). Apply the mobile checklist.
4. Do the polish: docs, version bump, STE check, and the full runs.

## Notes

- Mark a task complete only after its tests build and pass (constitution, release blocker).
- Commit after each task or small group of tasks. Write the commit messages in STE.
- The service, the API, and the stored data do not change in this feature.
