# Tasks: Phone UI Queue Activity and Navigation

**Input**: Design documents in `C:\src\GameBot\specs\140-phone-ui-queues-nav\`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/phone-queues-nav-contract.md, quickstart.md

**Scope**: Only `src/web-ui` changes, plus docs. The service code does not change.
**Green gate**: `vite build` and `jest` (known lint and `tsc --noEmit` failures exist before this feature).
**Text rule**: All new text (UI strings, comments, docs, commit messages) uses ASD-STE100 Simplified Technical English.
**Search rule**: To search the code, use the Grep tool. Do not use `rg` in a shell.
**Parallel rule**: Tasks that edit the same file are NOT marked [P]. They run in the order of their IDs.

## Format: `- [ ] [ID] [P?] [Story?] Description with file path`

- **[P]**: The task can run in parallel (a file that no other open task edits, no dependency on an open task).
- **[Story]**: US1 = queue activity and schedule, US2 = in-app back navigation, US3 = no Execution area on phone.
- All paths are relative to `C:\src\GameBot\src\web-ui\` unless they start with `docs\` or `specs\`. Source paths start with `src/`.

---

## Phase 1: Setup

- [x] T001 Read `specs/140-phone-ui-queues-nav/contracts/phone-queues-nav-contract.md` and `data-model.md`. Read `src/App.tsx`, `src/types/navigation.ts`, `src/lib/navigation.ts`, `src/components/queues/PhoneQueuesView.tsx`, `src/components/queues/QueueMonitor.tsx`, `src/hooks/useConnectionState.ts`, and `src/components/phone/PhoneTabBar.tsx`. Use the Grep tool to find each use of `phone:` and `'execution'`. Note the monitor and template types in `src/services/queues.ts` and `src/services/queueTemplates.ts`. No file change.
- [x] T002 Run `npm run build` and `npx jest` in `C:\src\GameBot\src\web-ui` to record the baseline. Note the tests that fail before the change.

---

## Phase 2: Foundational (blocks all user stories)

**Purpose**: Shared helpers, the `hidden` level, and the queue id prop. More than one story uses them.

- [x] T003 [P] Create `src/lib/phoneHistory.ts` with pure helpers: type `NavigationPosition { area, queueId }`, `buildAddress(position)` (`?area=<area>[&queue=<id>]`; it must not change desktop `tab` rules T-01 to T-04 in `contracts/phone-queues-nav-contract.md`), `parseAddress(location)` (unknown values give the default area and `null` queue), `pushScreen(position)` (no push if the position equals the current one; writes `{ idx, area, queueId }` to `history.state` with `idx + 1`), `replaceScreen(position)` (keeps `idx`), `initFirstEntry()` (sets `idx: 0` with `replaceState` if `history.state` has no `idx`), and `isFirstEntry()` (reads `idx` from `history.state`, so a reload stays correct).
- [x] T004 [P] Create `src/lib/__tests__/phoneHistory.test.ts`. Test address build and parse, unknown values, no duplicate push for the same position, `idx` increase on push, `idx` kept on replace, and `isFirstEntry` after a simulated reload.
- [x] T005 [P] Create `src/hooks/usePolling.ts` with `usePolling(callback, { intervalMs, enabled })`. It runs the callback every `intervalMs` while `enabled` and while `document.visibilityState` is not `hidden`. When the page becomes visible again, or `enabled` becomes true, it calls the callback at once, then restarts the interval. It clears the timer and the `visibilitychange` listener on unmount. It keeps the latest callback in a ref.
- [x] T006 [P] Create `src/hooks/__tests__/usePolling.test.ts` (Jest fake timers). Test the interval, the pause on a hidden page, the pause when `enabled` is false, the immediate call on resume, and the cleanup on unmount.
- [x] T007 [P] Change `src/types/navigation.ts`. Add `'hidden'` to `PhoneSupportLevel`. Set `phone: 'hidden'` for the `execution` area. Keep `execution-logs` as `desktop-only`. Add a helper `isPhoneVisible(area)` that returns false for `hidden`.
- [x] T008 Update `src/types/__tests__/navigation.test.ts`. Assert that `execution` has level `hidden`, `execution-logs` has `desktop-only`, every other area keeps its level, `isPhoneVisible` is false only for `hidden` areas, and the desktop list still has `execution`. Run after T007.
- [x] T009 Change `src/App.tsx`. In phone mode, read `area` and `queue` from the address with `parseAddress` (from T003). Keep the selected queue id in `App.tsx` state (plan D-7). Pass `queueId` and an `onOpenQueue(id)` callback to `PhoneQueuesView`. This task sets state only. T028 adds the history work. Do not change the desktop `tab` rules T-01 to T-04.
- [x] T010 Change `src/components/queues/PhoneQueuesView.tsx`. The selected queue is the prop `queueId` from `src/App.tsx`. Opening a queue calls `onOpenQueue(id)`. Delete any local state for the selected queue. Run after T009. The existing tests in `src/components/queues/__tests__/PhoneQueuesView.test.tsx` must pass: pass the new props in them.

**Checkpoint**: Helpers and their tests pass. The queue detail takes its queue id from `App.tsx`.

---

## Phase 3: User Story 1 - See what each queue does and what is scheduled (P1) MVP

**Goal**: The phone queue detail shows the "Now" block and the ordered schedule list. The queue card shows a one-line summary. Data refreshes every 5 s.

**Independent Test**: On a 360 x 640 screen, open a running queue. The screen shows the running sequence (or "waiting"), the last result, and the entries with next run times. No sideways scroll.

### Tests for User Story 1

- [x] T011 [P] [US1] Create `src/components/queues/__tests__/queueActivity.test.ts`. Test `buildScheduleRows` order (enabled with time ascending, enabled without time in template order, disabled last with a `disabled` mark), a stopped queue (no `nextAt`, "not scheduled" or the template schedule text), a missing template (names from `detail.entries`), `buildCardSummary` (running sequence name, else next time, else "Stopped"), `describeNow` (running, idle pause, waiting, nothing scheduled, last outcome), and `formatRelative` with a fixed `now`.
- [x] T012 [US1] Update `src/components/queues/__tests__/PhoneQueuesView.test.tsx`. Add tests for: the detail shows the state chip, the "Now" text, the last outcome, and the schedule rows in order (FR-004, FR-005, FR-005a); a stopped queue shows no false running data; the card shows a one-line summary (FR-006); an empty queue shows an empty-state text; long names have a `title` attribute.
- [x] T013 [US1] In `src/components/queues/__tests__/PhoneQueuesView.test.tsx`, add the SC-002 poll test with fake timers. Mock the monitor endpoint. Change the mock data. Advance the timers by 5 s. Assert that the screen shows the new data, and that the age of the shown data stays at or below 15 s over a 30 s run. Assert that no request runs while `document.visibilityState` is `hidden` or the connection is lost, and that one request runs at once on resume. Run after T012.
- [x] T014 [US1] In `src/components/queues/__tests__/PhoneQueuesView.test.tsx`, add the FR-009 assertion. In the detail view, assert that no control for schedule creation, reorder, or edit is present (no button or link with the text or role for add, edit, delete, reorder, or timer settings). Assert that only start, stop, and the enable switch exist. Run after T013.
- [x] T015 [US1] In `src/components/queues/__tests__/PhoneQueuesView.test.tsx`, add the FR-015 stale-data tests. Make the monitor request fail after a good load. Assert that the queue card and the queue detail keep the last data and show a visible "old data" mark. Assert that the mark goes away after the next good refresh. Assert that start, stop, and the enable switch are disabled while the connection is lost. Run after T014.
- [x] T016 [P] [US1] Create `src/__tests__/PhoneQueuesView.taps.test.tsx` for SC-001. Render the phone `App` at the first screen. Count each simulated tap (`userEvent.click` or `fireEvent.click`) with a wrapper. Open a queue. Assert that "Now" text and the next run time show after at most 2 taps (tab bar to Queues if needed, then the queue card).

### Implementation for User Story 1

- [x] T017 [US1] Create `src/components/queues/queueActivity.ts` with pure functions: `buildScheduleRows(monitor, template, detail)` (D-2 and the ordering rule in data-model.md), `buildCardSummary(queue, monitor)`, `describeNow(monitor)`, and `formatRelative(date, now)`. Use STE strings. Make T011 pass.
- [x] T018 [US1] Change `src/components/queues/PhoneQueuesView.tsx`. In the detail (it gets `queueId` as a prop since T010), add the "Now" block (state chip, `nowLabel`, last outcome) and the schedule list from `buildScheduleRows`. Mark disabled rows with "disabled". Add `title` to shortened names. Add an empty-state text. Show no control that edits a schedule (FR-009). Keep start, stop, and the enable switch.
- [x] T019 [US1] In `src/components/queues/PhoneQueuesView.tsx`, load the data with `usePolling` (5000 ms). Enable it only while the detail or the list is open and the connection is not lost. In the list, poll `GET /api/queues` and one `GET /api/queues/{id}/monitor` for each running queue. A failed monitor request for one card shows the status only. Show a one-line summary on each card. Run after T018.
- [x] T020 [US1] In `src/components/queues/PhoneQueuesView.tsx`, keep the last good data when a refresh fails. Add a `stale` state to each card and to the detail. Show an "Old data" mark when `stale` is true. Clear it after the next good refresh. Disable the controls while the connection is lost (FR-015). Run after T019. Tests T012 to T015 must pass.
- [x] T021 [P] [US1] Add the styles in `src/components/phone/phone.css`: the activity block, the schedule rows, the card summary line, the "disabled" mark, the "Old data" mark, text shortening with ellipsis, and tap targets of at least 44 x 44 CSS px. Use no fixed width above 320 px, so no sideways scroll occurs (FR-008).
- [x] T022 [US1] Update `src/components/phone/__tests__/phoneCss.test.ts`. Assert that the new classes in `src/components/phone/phone.css` set `min-height: 44px` for each control and set no `overflow-x` scroll on the activity view. Run after T021.

**Checkpoint**: US1 works alone. Run `npx jest src/components/queues src/components/phone`.

---

## Phase 4: User Story 2 - Use the back arrow inside the application (P1)

**Goal**: Each screen has its own history entry. Back and forward move between screens. Back from the first screen leaves the application.

**Independent Test**: Queues, open one queue, open Notifications, then press back 3 times: queue, Queues list, first screen. A fourth back leaves the application.

### Tests for User Story 2

- [x] T023 [US2] Update `src/__tests__/App.phone.test.tsx`. Add the SC-003 path test: Queues, open a queue, Notifications. Call `history.back()` 3 times and assert each screen in order. The test browser cannot leave the page. So the test states in a comment that it checks the first-screen marker (`idx` = 0 and no in-app `popstate` screen change) as the accepted proxy for "back leaves the application". The manual real-phone check in T042 confirms the real behavior. Add a forward test (scenario 4). Run at least 2 different paths.
- [x] T024 [US2] In `src/__tests__/App.phone.test.tsx`, add tests for: a tab bar move pushes one entry; a move to the same screen pushes none; a direct link `?area=queues&queue=<id>` shows the detail (SC-004); each phone area has a working direct link; the on-screen back control of the detail calls `history.back()` when `idx > 0` and does `replaceState` to the list when `idx` is 0 (FR-011a); a reload keeps `idx`. Run after T023.
- [x] T025 [US2] In `src/__tests__/App.phone.test.tsx`, add the FR-013 layout test. Start in phone mode with a queue detail open after at least 2 moves. Record `history.length`, the area, and the selected queue id. Resize to desktop width and fire the resize event. Then resize back to phone width. Assert at each step that the area, the selected queue id, and `history.length` are the same as the recorded values, and that back still goes to the previous screen. Run after T024.
- [x] T026 [US2] In `src/components/queues/__tests__/PhoneQueuesView.test.tsx`, add tests: `queue=<unknown id>` shows "Queue not found" with a button to the queue list (FR-014, A-04); the open queue comes from the `queueId` prop, not from local state. Run after T015.
- [x] T027 [P] [US2] In `src/__tests__/navigation.spec.tsx`, add a test that the desktop layout still uses only `replaceState` (H-07), that the desktop `tab` address rules T-01, T-02, and T-03 still hold, and that the desktop navigation is unchanged (FR-003, SC-007).

### Implementation for User Story 2

- [x] T028 [US2] Change `src/App.tsx` (it already holds the queue id since T009). In phone mode, replace the single `replaceState` effect with `pushScreen` on each move to a new screen. Call `initFirstEntry()` at start. Add a `popstate` listener that sets the area and the queue from the entry without a push. Keep `replaceState` only in desktop mode, with T-02 unchanged (T-04). Make T023 and T024 pass.
- [x] T029 [US2] Change `src/components/queues/PhoneQueuesView.tsx`. `onOpenQueue(id)` now adds a history entry through `App.tsx`. The on-screen back control uses `isFirstEntry()`: `history.back()` when not first, else `replaceScreen` to the list (H-04). Show "Queue not found" with a button to the list for an unknown id. Run after T028. Make T026 pass.
- [x] T030 [US2] Make the history stay valid when the layout changes between phone and desktop (FR-013). In `src/App.tsx`, keep the current area and queue on a layout change and do not add an entry. Make T025 pass. Use the Grep tool to check that no other `replaceState` or `pushState` call exists in `src/`. Run after T029.

**Checkpoint**: US2 works alone. Run `npx jest src/__tests__ src/lib`.

---

## Phase 5: User Story 3 - Phone UI without the Execution area (P2)

**Goal**: The phone UI does not list Execution. An old Execution link shows Queues and replaces the history entry.

**Independent Test**: On a phone-size screen, Execution is not in the tab bar or the More sheet. `/?area=execution` shows Queues.

### Tests for User Story 3

- [x] T031 [P] [US3] Update `src/components/phone/__tests__/PhoneTabBar.test.tsx`. Assert that neither the tab bar nor the More sheet shows "Execution" (N-01, SC-005). Assert that "Execution Logs" still shows as "Desktop only".
- [x] T032 [US3] In `src/__tests__/App.phone.test.tsx`, add redirect tests: `?area=execution` and the path `/execution` show Queues; the redirect uses `replaceState` (the history length does not grow); back does not return to Execution (FR-002, A-03); `/execution-logs` is not redirected. Run after T025.
- [x] T033 [US3] In `src/__tests__/App.phone.test.tsx`, add the permanent redirect test. Start in phone mode with `?area=execution`. Assert Queues shows and the address is `area=queues`. Change the layout to desktop (resize and fire the event). Assert that Queues still shows (not Execution) and that the Execution area is in the desktop navigation and opens on a click (spec edge case, clarification Q7). Run after T032.
- [x] T034 [P] [US3] Update `src/__tests__/App.desktopOnly.test.tsx`, `src/__tests__/execution.spec.tsx`, and `src/__tests__/navigation.spec.tsx`. Remove or change each assertion that expects the Execution area in the phone UI (FR-018). Keep each desktop assertion. Keep the T027 test.

### Implementation for User Story 3

- [x] T035 [P] [US3] Change `src/components/phone/PhoneTabBar.tsx`. Filter areas with `isPhoneVisible` in the tab bar and in the More sheet.
- [x] T036 [US3] Change `src/App.tsx`. In phone mode, when the requested area has level `hidden`, use the Queues area and call `replaceScreen`. Use the same rule for an unknown area. Do not change the desktop mode. Run after T030. Make T032 and T033 pass.
- [x] T037 [US3] Use the Grep tool to search `src/` for other links to the Execution area in phone code (for example `#/execution` in `src/components/queues/`). Remove each one in phone components. Keep the desktop `QueueMonitor.tsx` link. Edit `src/components/queues/PhoneQueuesView.tsx` only if the search finds a link there.

**Checkpoint**: All 3 stories work. Run `npx jest`.

---

## Phase 6: Polish and cross-cutting

- [x] T038 [P] Update `docs/mobile-ui-principles.md`. Add the level `hidden`, state that Execution is not in the phone UI, and describe the phone queue activity view and the back-navigation rules (FR-017). Write in STE.
- [x] T039 [P] Update `docs/architecture.md` (web UI section, area table, and the Last reviewed date) to match this feature (FR-017, Principle V). Write in STE.
- [x] T040 [P] Update `specs/STATUS.md` for feature 140. Set the `Status` line of `specs/139-mobile-friendly-ui/spec.md` to show that feature 140 iterates it.
- [x] T041 Run `npx jest` and `npm run build` in `C:\src\GameBot\src\web-ui`. All tests must pass except the failures that T002 recorded. No new failure in a desktop test (SC-007).
- [ ] T042 Final verification. Do the checks in `specs/140-phone-ui-queues-nav/quickstart.md` at 1280 px and at 360 x 640. The manual 360 x 640 check is the SC-006 evidence, together with the CSS assertions of T022: no sideways page scroll, and controls of at least 44 x 44 CSS px. Do the manual SC-003 check on a real phone: after the last in-app back, the next back leaves the application. Use the Grep tool to confirm that no phone navigation file lists `execution` as an area (SC-005). Record each result.
  - Result: SC-005 grep done. The tab bar has no Execution. `execution` has level `hidden` in `src/types/navigation.ts`. SC-006 CSS asserts pass in `phoneCss.test.ts`. The manual 360 x 640 check and the real-phone SC-003 check are PENDING (not done by the agent).

---

## Dependencies and order

- Phase 1, then Phase 2. Phase 2 blocks all stories.
- T009 and T010 (queue id prop) come before all US1 work on `PhoneQueuesView.tsx`.
- US1 (Phase 3) needs T005 (`usePolling`). US2 (Phase 4) needs T003 and T009. US3 (Phase 5) needs T007 and T028.
- Same-file chains (never parallel):
  - `src/App.tsx`: T009, T028, T030, T036.
  - `src/components/queues/PhoneQueuesView.tsx`: T010, T018, T019, T020, T029, T037.
  - `src/components/queues/__tests__/PhoneQueuesView.test.tsx`: T012, T013, T014, T015, T026.
  - `src/__tests__/App.phone.test.tsx`: T023, T024, T025, T032, T033.
  - `src/__tests__/navigation.spec.tsx`: T027, T034.
- Phase 6 runs last.
- Tests in each story come before the code that makes them pass.

## Parallel examples

- Phase 2: T003, T004, T005, T006, T007 together (5 files). T008 follows T007. T009 and T010 follow T003.
- US1: T011 and T016 together (new files), and T021 with the T017 to T020 chain. T012 to T015 run in order.
- US2: T027 in parallel with the App.phone test chain.
- US3: T031, T034, and T035 together.
- Polish: T038, T039, T040 together.

## Implementation strategy

- MVP: Phase 1, Phase 2, and US1 (queue activity). Stop and check.
- Then US2 (back navigation) and US3 (Execution removed). Each adds value alone.
- Run the green gate (`jest` and `vite build`) after each phase.
