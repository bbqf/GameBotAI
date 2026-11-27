# Tasks: Move the Monitoring area to the shared UI components

**Input**: Design documents from `specs/204-monitoring-shared-ui/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/monitoring-layout.md, quickstart.md

**Tests**: The spec asks for tests (FR-024, FR-025).

**Paths**: Source paths are relative to `src/web-ui/` unless they start with `docs/` or `specs/`.

**Rules for each task**: Use only theme tokens in new CSS. Put each hover rule inside `@media (hover: hover)`. Write all comments and UI text in STE. Do not remove a behavior check from a test; change only names, selectors, and layout checks.

## Format: `[ID] [P?] [Story] Description`

---

## Phase 1: Setup

- [X] T001 Run `npm --prefix "C:\src\GameBot\src\web-ui" test`, `npm --prefix "C:\src\GameBot\src\web-ui" run lint`, and `npm --prefix "C:\src\GameBot\src\web-ui" run build` before any change. Record the results in the implementation notes.

---

## Phase 2: Foundational

- [X] T002 Add the optional prop `details?: React.ReactNode` to `src/components/ui/ConfirmDialog.tsx`. Render it in a `<div className="ui-dialog__details">` between the message and the buttons. Add a test to `src/components/ui/__tests__/ConfirmDialog.test.tsx`: the details show between the message and the buttons; with no details, no `.ui-dialog__details` element renders.
- [X] T003 Create `src/components/monitoring/sessionView.ts` with `sessionChip`, `deviceKind`, and `startedText` per plan.md ("Design") and data-model.md, and the test `src/components/monitoring/__tests__/sessionView.test.ts` (each state of the chip table, an unknown state, each device value, a valid date with a fixed locale `en-GB`, and an invalid date → "—").
- [X] T004 Add the CSS classes `ui-kv`, `ui-kv__row`, `ui-kv__label`, `ui-kv__value`, `ui-detail-grid`, `ui-detail-side`, `ui-button--block`, and `ui-dialog__details` (margin-bottom 20 px) to `src/components/ui/ui.css` per plan.md ("Design"). The `ui-detail-grid` is one column inside the phone media query `(max-width: 768px), (max-height: 500px)`.
- [X] T005 Run the unit tests and lint.

---

## Phase 3: User Story 1 - Sessions in the shared list (Priority: P1) 🎯 MVP

**Independent Test**: The list part of `tests/e2e/monitoring-layout.spec.ts` and `SessionList.test.tsx` pass.

- [X] T006 [P] [US1] Create `tests/e2e/helpers/monitoringMock.ts`: read the routes in `src/services/monitoringApi.ts`; mock `/health`, the Google Fonts hosts, the session list (3 sessions: "Device A" `live` with the queue "Farm gold" and the sequence "Daily rewards", `restartAvailable: true`; "Device B" `no_answer` with the reason "No change after input" and no queue; "Device C" `live` with the queue "Arena" and `ownerQueueDeviceLiveness: 'not_live'`, `restartAvailable: false`, `restartUnavailableReason: "The emulator is not an LDPlayer instance."`), the screenshot route (a 1 x 1 PNG), the restart start route (return a record with `state: 'restarting'`, `step: 'rebooting'`), and the restart read route. Keep a list of the calls, as `helpers/queuesMock.ts` does.
- [X] T007 [US1] Rewrite `src/components/monitoring/__tests__/SessionList.test.tsx` for the new `SessionList` (props `sessions`, `state`, `onSelect`, `mode`): desktop columns and cell texts ("—" for an empty queue and sequence, "Live", the "Not live" mark); the chip text and class for each state; a row click and the open button call `onSelect` with the id; phone rows: the name, the chip, the queue name or "No queue", the mark; `data-testid="session-row-<id>"` on both layouts; the states "Loading…", "No running sessions.", and an error with **Try again**.
- [X] T008 [US1] Rewrite `src/components/monitoring/SessionList.tsx` per plan.md ("Design") and contracts/monitoring-layout.md. `SessionStateChip` keeps its export name and its prop `state` and uses `sessionChip` and the shared `StatusChip` with `testId="session-state"`. Remove `PhoneCard`, the old classes, and the import of `features/execution/StatusChip.css`.
- [X] T009 [US1] Change the list part of `src/pages/MonitoringPage.tsx`: when no session is open, render `PageHeader title="Monitoring"`, the message area (the list error and "Session ended" as now, with the same test ids and roles), and `SessionList` with `state` (`{ kind: 'loading' }` while `sessions === null`; `{ kind: 'error', text: listError, onRetry: () => void loadList() }` when the list call failed and the list is empty (with data from an earlier call, the list stays and the error shows only in the message area, FR-007); `{ kind: 'empty', text: 'No running sessions.' }`; else `'ready'`). Remove the desktop two-column layout and the text "Select a session to see its screen.".
- [X] T010 [US1] Remove `{!phone && <h1>Monitoring</h1>}` from `src/App.tsx`. Update tests that look for the Monitoring `<h1>` (search `src/__tests__` and `tests/e2e` for `'Monitoring', level: 1` and similar).
- [X] T011 [US1] Create `tests/e2e/monitoring-layout.spec.ts` (chromium, 1280 x 800, `/?area=monitoring`) with the helper of T006: the State 132, Device 120, Queue 220, and Sequence 260 column widths (±1); each row 56 px (±1); the chips "Live" (`ui-chip--done`) and "No answer" (`ui-chip--failed`); "—" in Device and Queue of "Device B"; the "Not live" mark in the row of "Device C"; no "Actions" header.

---

## Phase 4: User Story 2 - Detail page (Priority: P1)

**Independent Test**: The detail part of both browser test files and `MonitoringPage.layout.test.tsx` pass.

- [X] T012 [US2] Change the detail part of `src/pages/MonitoringPage.tsx` per plan.md ("Design"), FR-009 to FR-015, and data-model.md ("Key-value card", "Restart control"): `PageHeader` with `onBack` (desktop `select(null)`, phone `closeDetail()`), the title, the chip, and on the desktop the `Button variant="secondary" icon="reload"` "Restart emulator"; the body `ui-detail-grid` with `ScreenshotView` and `ui-detail-side` (the `ui-kv` card, the restart status, the restart message, and the "not available" text with the reason); on the phone a `FooterBar` with the same button (`ui-button--block`). Keep all test ids. Keep the "Not live" mark only in the Device row (not in the header). The header title stays on one line and ends with "…" for a long name: if `PageHeader` does not do this now, add `white-space: nowrap; overflow: hidden; text-overflow: ellipsis; min-width: 0` to its title in `src/components/ui/ui.css` and add a check to `src/components/ui/__tests__/PageHeader.test.tsx` that the title has the class for it. A list call that fails while the detail shows does not close the detail.
- [X] T013 [US2] Update `src/pages/__tests__/MonitoringPage.layout.test.tsx` and `src/pages/__tests__/MonitoringPage.screenshot.test.tsx`: the selection opens the detail page (on the desktop the list does not show while the detail shows); the back arrow ("Back") shows the list; keep the checks of the selection after a layout switch (no remount), the phone history entry, `history.back()`, the address query, and the popstate behavior.
- [X] T014 [US2] Update `src/pages/__tests__/MonitoringPage.restart.test.tsx` for the card: the sequence value has `data-testid="monitoring-sequence"`; with no sequence, no element has that test id (the value shows "—"); the "Not live" mark is in the Device row; the reason row shows only for `no_answer`; the Started row shows a date.
- [X] T015 [US2] Add to `tests/e2e/monitoring-layout.spec.ts`: a click on the row "Device A" opens the detail (`data-testid="monitoring-detail"`) and the list does not show; the `.ui-detail-side` column is 320 px (±1) and on the right of the picture; the card shows "Farm gold" and "Daily rewards"; "Back" shows the list again.

---

## Phase 5: User Story 3 - Restart with the shared dialog (Priority: P2)

**Independent Test**: The restart tests of `MonitoringPage.restart.test.tsx` and the dialog part of the desktop browser test pass.

- [X] T016 [US3] In `src/pages/MonitoringPage.tsx`: replace `RestartEmulatorDialog` with `ConfirmDialog` per FR-016 to FR-018 (`open={dialogOpen}`, `title={\`Restart emulator ${session.name}?\`}`, `message="The whole emulator stops and starts again."`, `confirmText="Restart"`, `danger`, `details` = `<p role="note" data-testid="restart-queue-warning">Queue {ownerQueueName ?? '(no name)'} uses this emulator. The queue work will stop for a short time.</p>` only when `ownerQueueId` is set, `onCancel`, `onConfirm={() => void confirmRestart()}`).
- [X] T017 [US3] Delete `src/components/monitoring/RestartEmulatorDialog.tsx` and `src/components/monitoring/__tests__/RestartEmulatorDialog.test.tsx`. Move each behavior check of the deleted test to `src/pages/__tests__/MonitoringPage.restart.test.tsx`: Escape closes the dialog and sends no call; **Cancel** sends no call; the focus is on **Cancel** at open; the warning shows only with a queue; a queue with no name shows "(no name)". Change the button and dialog names to the new texts.
- [X] T018 [US3] Add to `tests/e2e/monitoring-layout.spec.ts`: on "Device A", "Restart emulator" opens the dialog "Restart emulator Device A?" with the warning "Queue Farm gold uses this emulator…"; **Cancel** sends no restart call; **Restart** sends one call and the status "Restarting: stopping and starting the emulator" shows; on "Device C", the button is off and "Restart is not available for this session." and the reason show.

---

## Phase 6: Phone checks

- [X] T019 [US1] Create `tests/e2e/monitoring-layout.mobile.spec.ts` (mobile, 360 x 640) with the helper of T006: each `[data-testid^="session-row-"]` is 64 px (±1); a tap on "Device A" opens the detail; the footer button "Restart emulator" is at least 44 px high and fully in the window; `scrollWidth <= clientWidth` at 320, 360, and 390 px on the list and on the detail; "Back" shows the list.
- [X] T020 Update `src/__tests__/App.phoneHistory.test.tsx`, `src/__tests__/navigation.spec.tsx`, and `src/__tests__/PhoneEntryNavigation.test.tsx` where they use the old Monitoring names (`session-card-<id>`, "← Back", "Select <name>"). Keep their behavior checks.
- [X] T021 Run all unit tests and `npx --prefix "C:\src\GameBot\src\web-ui" playwright test --config "C:\src\GameBot\src\web-ui\playwright.config.ts" monitoring-layout mobile.spec --trace off`. If a local browser test times out at a page load, see quickstart.md. Fix failures.

---

## Phase 7: Cleanup and documents

- [X] T022 Remove the CSS that no code uses after the change from `src/pages/Monitoring.phone.css` and `src/styles.css` (the split view `monitoring-columns*`, `monitoring-list*`, `monitoring-detail__back`, `monitoring-detail__row`, `monitoring-detail__name`, `monitoring-detail__actions`, `monitoring-detail__restart`). Search each class in `src/` before you remove it. Keep the `monitoring-screenshot*` rules. Remove the file and its import if it becomes empty.
- [X] T023 [P] Add the `details` prop to the `ConfirmDialog` line of section 13 of `docs/ui-guidelines.md`. Refresh "Last reviewed".
- [X] T024 [P] Update `docs/mobile-ui-principles.md`: "Last reviewed" 2026-11-27 and feature 204 in its list. Check that no text there still says that Monitoring uses cards from `PhoneCard`; change such text to the shared list.
- [X] T025 [P] Add the line `| 204 | Monitoring area on the shared UI components | Implemented |` to `specs/STATUS.md`, and set the `**Status**:` line of `specs/204-monitoring-shared-ui/spec.md` to `Implemented`.
- [X] T026 [P] Check `docs/architecture.md` for text about the Monitoring layout (split view) or the restart dialog. Change it to the list page and the detail page, and refresh "Last reviewed" when you change it.
- [X] T027 Add a `CHANGELOG.md` entry in STE: "Monitoring uses the shared list and a detail page on the desktop and the phone. The detail page shows the live picture and a card with the session data. The emulator restart uses the shared confirmation dialog."

---

## Phase 8: Polish

- [X] T028 Run all checks: unit tests, lint, build, all Playwright tests, and `npm --prefix "C:\src\GameBot\src\web-ui" test -- --coverage --collectCoverageFrom="src/components/monitoring/*.{ts,tsx}" --collectCoverageFrom="src/pages/MonitoringPage.tsx"`. Check 80% line and 70% branch coverage of these files. Fix each failure, also in old code.
- [X] T029 Check SC-003: search `src/components/monitoring` and `src/pages/MonitoringPage.tsx` for `btn`, `status-chip`, and `←`. The result must be empty.
- [X] T030 Take screenshots with Playwright and the helper of T006 (desktop 1280: the list and the detail of "Device A"; phone 360: the list and the detail). Compare them with `docs/ui-guidelines/1-list-pattern.png` and mockup B of `docs/ui-guidelines/2-form-detail-editor-logs.png`. Fix visible defects (a second title, text color, alignment, sizes, controls that the general phone button rule changes). Measure the time from the row click to the visible detail (goal: less than 100 ms). Record the values and the defects that you fixed in the PR description.

---

## Dependencies

- Phase 1 → Phase 2 → US1 (Phase 3) → US2 (Phase 4) → US3 (Phase 5) → Phase 6 → Phase 7 → Phase 8.

### Parallel opportunities

- T006 with T007. T023 to T026 together.

## Implementation strategy

MVP: Phases 1 to 3. Then the detail page, the dialog, the phone checks, the cleanup, and the polish. Run the tests after each phase.
