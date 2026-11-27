# Tasks: Move the Notifications area to the shared UI components

**Input**: Design documents from `specs/203-notifications-shared-ui/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/ui-components.md, contracts/notifications-layout.md, quickstart.md

**Tests**: The spec asks for tests (FR-028, FR-029). Each new shared part gets a unit test. The area gets browser tests.

**Paths**: Source paths are relative to `src/web-ui/` unless they start with `docs/` or `specs/`.

**Rules for each task**: Use only theme tokens in new CSS. Put each hover rule inside `@media (hover: hover)`. Write all comments and UI text in STE. Do not remove a behavior check from a test.

## Format: `[ID] [P?] [Story] Description`

---

## Phase 1: Setup

- [X] T001 Run `npm --prefix "C:\src\GameBot\src\web-ui" test`, `npm --prefix "C:\src\GameBot\src\web-ui" run lint`, and `npm --prefix "C:\src\GameBot\src\web-ui" run build` before any change. Record the results in the implementation notes.

---

## Phase 2: Foundational (blocks all user stories)

- [X] T002 Add the icon `send` (path `M2.01 21L23 12 2.01 3 2 10l15 2-15 2z`) to `src/components/ui/icons.ts`, and add `send` to the name list of `src/components/ui/__tests__/icons.test.ts`.
- [X] T003 [P] Create `src/components/ui/SectionHeader.tsx` and its CSS in `src/components/ui/ui.css` per contracts/ui-components.md, and the test `src/components/ui/__tests__/SectionHeader.test.tsx` (the title is an `<h3>`; the actions show).
- [X] T004 [P] Create `src/components/ui/Toggle.tsx` and its CSS per contracts/ui-components.md, and the test `src/components/ui/__tests__/Toggle.test.tsx` (`role="switch"`, `aria-checked`, the accessible name is the label, a click on the switch and on the label calls `onChange(!checked)`, `disabled` blocks the change, Space on the focused switch calls `onChange`).
- [X] T005 Create `src/components/ui/SidePanel.tsx` and its CSS per contracts/ui-components.md, and the test `src/components/ui/__tests__/SidePanel.test.tsx` (desktop mode: `role="dialog"`, `aria-modal="false"`, the accessible name is the title, the focus moves to the first input of the body on open, Escape calls `onClose`, the "Close" button calls `onClose`, the focus goes back to the opener on close, nothing renders when `open` is false; phone mode: the class `ui-side-panel--page` and a "Back" button that calls `onClose`).
- [X] T006 Add the form field classes of contracts/ui-components.md ("Form field classes") to `src/components/ui/ui.css`, with the 16 px input font inside the phone media query `(max-width: 768px), (max-height: 500px)`.
- [X] T007 Export `SidePanel`, `Toggle`, and `SectionHeader` from `src/components/ui/index.ts`.
- [X] T008 Run the unit tests and lint. All tests that passed in T001 must pass.

**Checkpoint**: The new shared parts are ready.

---

## Phase 3: User Story 1 - Targets in the shared list (Priority: P1) 🎯 MVP

**Goal**: The target list uses `ListView`, icon actions, and the shared Delete dialog.

**Independent Test**: The target part of `tests/e2e/notifications-layout.spec.ts` and the changed `TargetList.spec.tsx` pass.

### Tests for User Story 1

- [X] T009 [P] [US1] Create `tests/e2e/helpers/notificationsMock.ts`: mock `/health`, the Google Fonts hosts, `GET /api/notification-targets` (3 targets: "My phone" enabled with chat ID and hint, "Team chat" disabled with no hint, and a target with a name of 100 characters), `POST` and `PUT /api/notification-targets[/<id>]` (return the saved target), `DELETE /api/notification-targets/<id>` (204), `POST /api/notification-targets/<id>/test` (`{ ok: true }`), `GET /api/queues` (3 queues with levels), `PUT /api/queues/<id>/notification-level` (return the queue), and `{}` for other `/api/**` calls. Read the exact routes in `src/services/notifications.ts` and `src/services/queues.ts`. Keep a list of the calls, as `helpers/queuesMock.ts` does. Add a state field `testDelayMs` (default 0) that the test route waits before it answers.
- [X] T010 [US1] Create `tests/e2e/notifications-layout.spec.ts` (chromium, 1280 x 800, `/?area=notifications`): the target list columns Type 120, Enabled 132, Chat ID 200, Bot token 160, Actions 148 (±1); each row 56 px (±1); the chips "On" (`ui-chip--done`) and "Off" (`ui-chip--stopped`); the order of the actions of "My phone"; **Send test message** sends `POST …/test`, the icon "Send test message My phone" is off while the call runs (set `testDelayMs` of the helper to 500 for this check), and the message "The test message was sent." shows after the call; "More actions for My phone" → "Delete" opens "Delete target My phone?"; **Cancel** sends no call; **Delete** sends the DELETE call and the row goes away.
- [X] T011 [US1] Update `src/components/notifications/__tests__/TargetList.spec.tsx` for the new layout: the icon buttons "Send test message <name>" and "Edit <name>", the menu with "Delete", the menu item "Delete" calls the prop `onDelete` with the target (the dialog is in the page, T014), the chip text, the cell texts ("Saved (<hint>)", "Saved", "Not set", "—"), and the empty text. Keep each current behavior check (edit calls `onEdit` with the target, test calls `onTest` with the id).

### Implementation for User Story 1

- [X] T012 [US1] Rewrite `src/components/notifications/TargetList.tsx` with `ListView` per contracts/notifications-layout.md and data-model.md. Props: `targets`, `state` (`ListState`), `onEdit(target)`, `onDelete(target)` (opens the dialog in the page), `onTest(id)`, `testingId?: string | null` (turns the test icon of that row off), `mode?`. Remove `ConfirmDeleteModal`, `useState`, and the text buttons. Use `rowActions` with the layout from `useLayoutMode()` (or `mode`) to give the desktop or the phone action sets.
- [X] T013 [US1] Change `src/pages/NotificationsPage.tsx`: `PageHeader` with the title "Notifications" and the primary `Button` (icon `add`) "Add target" that sets `editing` to `'new'`; one message area under the header (errors `role="alert"` with the class `form-error`, notices `role="status"` with the class `message`); the intro text and `TelegramSetupGuide` stay; `SectionHeader` "Targets" above the list; pass `state` to `TargetList` (`{ kind: 'loading' }` while loading, `{ kind: 'error', text, onRetry: load }` on a load error, `{ kind: 'empty', text: 'No target is saved. Follow the guide and add one.' }` when empty, else `'ready'`). Remove the old "Add target" button under the list.
- [X] T014 [US1] In `src/pages/NotificationsPage.tsx`: add the state `deleteTarget` and one `ConfirmDialog` (title `Delete target ${name}?`, message "The target gets no more messages. You cannot undo this.", `confirmText="Delete"`, `danger`, `busy` while the call runs). On confirm call the current `remove(id)` logic (it also closes the editor when the deleted target is open). Add the state `testingId`: set it before `testNotificationTarget` and clear it in `finally`; keep the current notice texts.
- [X] T015 [US1] Run the unit tests and `npx --prefix "C:\src\GameBot\src\web-ui" playwright test --config "C:\src\GameBot\src\web-ui\playwright.config.ts" notifications-layout.spec --project chromium --trace off`. Fix failures.

---

## Phase 4: User Story 2 - Target form in the side panel or the phone page (Priority: P1)

**Goal**: The form uses cards, the Toggle, field checks, and the footer bar, in a `SidePanel`.

**Independent Test**: The changed `TargetForm.spec.tsx` and the editor part of both browser test files pass.

### Tests for User Story 2

- [X] T016 [P] [US2] Update `src/components/notifications/__tests__/TargetForm.spec.tsx`: keep all current behavior checks (an empty token sends no token; Save and test saves first; a save error stops the test; the messages); change the selectors for the `Toggle` (`getByRole('switch', { name: 'Enabled' })`) and the footer buttons; add checks: an empty Name shows "Enter a name." and sends no call; an empty Chat ID shows "Enter a chat ID." and sends no call; typing in the field removes its error; an API error shows in an alert; the Delete button shows only in edit mode and calls `onDelete`; all footer buttons are off while busy.
- [X] T017 [US2] Add to `tests/e2e/notifications-layout.spec.ts`: **Add target** opens the editor (`data-testid="target-editor"`) of 480 px (±1) at the right edge of the window, and the target list stays visible; **Save** with empty fields shows both field errors and sends no POST; a click on the row "Team chat" opens "Edit target" with the name "Team chat"; Escape closes the panel and the focus is on the "Add target" button again after a close that was opened with it; footer **Delete** → **Delete** in the dialog sends the DELETE call and closes the panel; with the panel open on "Team chat", the row menu "Delete" of "Team chat" → **Delete** also closes the panel (spec edge case); with the panel open at 1280 x 800, the **Save** button is fully inside the window with no scroll (SC-004).

### Implementation for User Story 2

- [X] T018 [US2] Rewrite `src/components/notifications/TargetForm.tsx`: one `<form id="target-form">` (flex column) with an alert for the API error, the card "Target" (`ui-form-card`, fields Name, Bot token, Chat ID with `ui-field` classes and labels above), the card "Status" (`Toggle` "Enabled"), the result message (`role="status"`), and the `FooterBar` as the last child (left: `Button variant="danger-outline"` "Delete" only when `target` and `onDelete` are set; right: `Button variant="secondary"` "Cancel", `Button variant="secondary"` "Save and test", `Button variant="primary" type="submit"` "Save"). Add the prop `onDelete?: (target) => void`. Add the field checks of data-model.md (on submit and on Save and test; clear on change; `aria-invalid` and `aria-describedby` on the input). Keep the save and test logic and texts. Remove the `<h3>` title (the panel has the title) and the use of `FormField`.
- [X] T019 [US2] Create `src/components/notifications/TargetEditor.tsx`: props `editing` (`NotificationTargetDto | 'new' | null`), `onSaved`, `onClose`, `onDelete`, `mode?`. It renders `SidePanel` (`open={editing !== null}`, title "Add target" or "Edit target", `testId="target-editor"`, `onClose`) with `TargetForm` as the child (`onCancel={onClose}`).
- [X] T020 [US2] In `src/pages/NotificationsPage.tsx`: render `TargetEditor` with the current `onSaved` logic, `onClose={() => setEditing(null)}`, and `onDelete={(t) => setDeleteTarget(t)}`. On the phone (`useLayoutMode() === 'phone'`) and while `editing !== null`, render only `TargetEditor` (and the `ConfirmDialog`), not the header, the message area, the guide, and the lists. Row click and **Edit** call `setEditing(target)`.
- [X] T021 [US2] Run the unit tests and the desktop browser test. Fix failures.

---

## Phase 5: User Story 3 - Queue levels in the shared list (Priority: P2)

**Goal**: The level section uses `SectionHeader` and `ListView`.

**Independent Test**: The changed `QueueLevelTable.spec.tsx` and the level part of both browser test files pass.

- [X] T022 [P] [US3] Update `src/components/notifications/__tests__/QueueLevelTable.spec.tsx`: keep the checks of the level change call and the error text; change the loading text to "Loading…"; add checks for the empty text "No queue exists." and for the error state with **Try again** that loads again.
- [X] T023 [US3] Rewrite `src/components/notifications/QueueLevelTable.tsx`: `SectionHeader` "Queue notification levels"; `ListView` per contracts/notifications-layout.md (desktop columns Queue flex and Level 240 with the select of class `ui-field__input`; phone: `phone.title` = name, `renderPhoneActions` = the select with the class `ui-level-select--phone` of 160 px); `state` from `loading`, `error` (with `onRetry: load`), and the empty text. Keep the `change` logic. Move the error of a level change to an alert under the section header.
- [X] T024 [US3] Add to `tests/e2e/notifications-layout.spec.ts`: the Level column is 240 px (±1), each level row is 56 px, and a change of the select sends one `PUT` call.

---

## Phase 6: Phone checks (User Stories 1 to 3)

- [X] T025 [US1] Update `src/components/notifications/__tests__/NotificationsPhone.spec.tsx` for the phone layout: no table; the name, the chip, and "Telegram · -100123" show; the main icon "Send test message My phone" calls `onTest`; the menu "More actions for My phone" has "Edit" (calls `onEdit` with the target) and "Delete" (calls `onDelete`); the level list shows the select "Notification level for Farm-1" and a change calls the API. Keep each current behavior check.
- [X] T026 [US2] Create `tests/e2e/notifications-layout.mobile.spec.ts` (mobile project, 360 x 640) with the helper of T009: each `[data-testid^="target-row-"]` and `[data-testid^="level-row-"]` is 64 px (±1); each icon button in the rows is at least 44 x 44 px; each select in the rows is at least 40 px high; `scrollWidth <= clientWidth` at 320, 360, and 390 px; "Add target" opens a full page with a "Back" button and the title "Add target", and the target list does not show; "Back" shows the list again.
- [X] T027 Run `npx --prefix "C:\src\GameBot\src\web-ui" playwright test --config "C:\src\GameBot\src\web-ui\playwright.config.ts" --project mobile --trace off`. If a phone test shows an empty page, see the note in quickstart.md. Fix failures. `tests/e2e/mobile.spec.ts` must stay green.

---

## Phase 7: User Story 4 - Documents (Priority: P3)

- [X] T028 [US4] In `docs/ui-guidelines.md`, add to rule F6: "Exception: a choice inside a fixed list row uses a select, because a segmented control does not fit the row on the phone." Add SidePanel, Toggle, SectionHeader, and the form field classes to section 13, with the rules that they implement (D2, F1, F5, F6). Refresh "Last reviewed".
- [X] T029 [P] Add the line `| 203 | Notifications area on the shared UI components | Implemented |` to `specs/STATUS.md`, and set the `**Status**:` line of `specs/203-notifications-shared-ui/spec.md` to `Implemented`.
- [X] T030 [P] Check `docs/architecture.md` for the web UI section that feature 202 changed. Add the new shared parts if that section lists the parts. Refresh "Last reviewed" when you change it.
- [X] T031 Add a `CHANGELOG.md` entry in STE: "The Notifications page uses the shared list, icon actions, and one Delete dialog. The target form opens in a side panel on the desktop and as a full page on the phone. The form shows an error under an empty Name or Chat ID."

---

## Phase 8: Polish

- [X] T032 Run all checks: unit tests, lint, build, all Playwright tests (`npx --prefix "C:\src\GameBot\src\web-ui" playwright test --config "C:\src\GameBot\src\web-ui\playwright.config.ts" --trace off`), and `npm --prefix "C:\src\GameBot\src\web-ui" test -- --coverage --collectCoverageFrom="src/components/ui/{SidePanel,Toggle,SectionHeader}.tsx" --collectCoverageFrom="src/components/notifications/*.tsx"`. Check 80% line and 70% branch coverage of these files. Fix each failure, also in old code.
- [X] T033 Check SC-003: search `src/components/notifications` and `src/pages/NotificationsPage.tsx` for `btn`, `btn-secondary`, `btn-danger`, and `ConfirmDeleteModal`. The result must be empty.
- [X] T034 Take screenshots with Playwright and the mock of T009 (desktop 1280 with the side panel open; phone 360 with the list and with the form). Compare them with the mockups in `docs/ui-guidelines/`. Fix visible defects (text color, alignment, sizes). Measure the size that the new CSS adds to `src/components/ui/ui.css` (goal: less than 5 KB). Measure the time from the click on "Add target" to the visible side panel in the desktop browser test (goal: less than 100 ms). Record the values in the PR description.

---

## Dependencies

- Phase 1 → Phase 2 → US1 (Phase 3) → US2 (Phase 4) → US3 (Phase 5) → Phase 6 → Phase 7 → Phase 8.
- US2 needs the page state of US1 (T013, T014). US3 is independent of US1 and US2 after Phase 2.

### Parallel opportunities

- Phase 2: T003 and T004 after T002.
- US1: T009 in parallel with T011.
- US2: T016 in parallel with T018.
- Phase 7: T029 and T030.

## Implementation strategy

MVP: Phases 1 to 3. Then the form (US2), the levels (US3), the phone checks, the documents, and the polish. Run the tests after each phase.
