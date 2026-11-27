# Tasks: Shared UI components and design tokens, with the Queues page as the first user

**Input**: Design documents from `specs/202-ui-shared-components/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/ui-components.md, contracts/queues-list-layout.md, quickstart.md

**Tests**: The spec asks for tests (FR-027, SC-006). Each shared component gets a unit test. The Queues list gets browser tests.

**Paths**: All source paths are relative to `src/web-ui/` unless they start with `docs/` or `specs/`.

**Rules for each task**: Use only CSS variables of the theme in new CSS. Put each hover rule inside `@media (hover: hover)`. Write all comments and UI text in STE.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: The user story of the task (US1, US2, US3, US4)

---

## Phase 1: Setup

- [X] T001 Run `npm --prefix "C:\src\GameBot\src\web-ui" test`, `npm --prefix "C:\src\GameBot\src\web-ui" run lint`, and `npm --prefix "C:\src\GameBot\src\web-ui" run build` on the branch before any change. Record the results (pass counts, failures) in the implementation notes, so that a later failure is known as new or old.
- [X] T002 Create the folder `src/components/ui/` with an empty `index.ts` and an empty `ui.css`, and import `./ui.css` in `src/components/ui/index.ts`.

---

## Phase 2: Foundational (blocks all user stories)

**Purpose**: Tokens, the icon set, and the base components that all stories use.

- [X] T003 Update `src/theme/tokens.ts`: set `typography.fontFamily` to `'Segoe UI', system-ui, -apple-system, Roboto, sans-serif`; add the color tokens `dangerSoft` `#fdecec`, `successSoft` `#e7f6ec`, `warningSoft` `#fdf1e3`, `idle` `#64748b`, `idleSoft` `#eef1f5`, `loop` `#7c3aed`, `ifBlock` `#0891b2`; add the group `size` with `appBar` 56px, `control` 40px, `iconButton` 36px, `iconButtonTouch` 44px, `icon` 20px, `row` 56px, `rowPhone` 64px, `headerRow` 40px, `keyValueRow` 40px, `footer` 68px, `sidePanel` 480px, `formWidth` 720px, `chipWidth` 96px, `chipHeight` 24px, `thumbnail` 40px; add the radius tokens `control` 8px and `dialog` 14px; add each new value to `cssVarMap` with the names `--gb-color-danger-soft`, `--gb-color-success-soft`, `--gb-color-warning-soft`, `--gb-color-idle`, `--gb-color-idle-soft`, `--gb-color-loop`, `--gb-color-if`, `--gb-size-<name>` (kebab case, for example `--gb-size-row-phone`), `--gb-radius-control`, `--gb-radius-dialog`, and `--gb-color-text-muted` (same value as `muted`).
- [X] T004 Update `src/theme/global.css`: remove the Google Fonts `@import`; set `--gb-font-family` to the new font; add each new variable of T003 with the same value; add `--gb-color-text-muted: #475467`.
- [X] T005 [P] Add the unit test `src/theme/__tests__/tokens.test.ts`: read `src/theme/global.css` with `fs`, and check that each entry of `cssVarMap` is declared in `:root` with the same value (normalize spaces), and that the file has no `@import` of `fonts.googleapis.com`. Export `cssVarMap` from `tokens.ts` for this test if it is not exported.
- [X] T006 Update the global `button` rule in `src/styles.css` (lines 26 to 29): background `var(--gb-color-surface)`, no gradient, no `transform`, no `translateY`, no hover shadow; move the `button:hover` rule into `@media (hover: hover)` with only `background: var(--gb-color-surface-alt)`; keep the height variable (research R2); keep `button:disabled` without `transform`.
- [X] T007 Create `src/components/ui/icons.ts`: move `ICON_PATHS` from `src/components/queues/IconButton.tsx` into it, add the paths `more`, `back`, `close`, `drag`, `expand`, `collapse`, `upload`, `download`, `search`, `filter` (Material icon paths, 24 px view box; `close` is the same as the old `remove`), and export `IconName` and `ICON_NAMES`.
- [X] T008 Create `src/components/ui/Icon.tsx` per contracts/ui-components.md (an `<svg>` with `viewBox="0 0 24 24"`, `aria-hidden="true"`, `focusable="false"`, default size 20, `fill="currentColor"`).
- [X] T009 Create `src/components/ui/IconButton.tsx` and its CSS in `src/components/ui/ui.css` per contracts/ui-components.md (36 px, `touch` 44 px, 44 px in the phone media query `(max-width: 768px), (max-height: 500px)`, no border, radius `--gb-radius-control`, muted color, `danger` tone red, off state opacity 0.4, focus outline `--gb-color-focus`; props `icon`, `label`, `title`, `tone`, `size`, `disabled`, `ariaExpanded`, `ariaHasPopup`, `onClick`, `className`, `testId`). Do not call `event.stopPropagation()` in IconButton. ListView does this.
- [X] T010 [P] Create `src/components/ui/Button.tsx` and its CSS per contracts/ui-components.md (variants `primary`, `secondary`, `danger-outline`, `danger`; optional `icon`; 40 px; `type` default `button`).
- [X] T011 [P] Create `src/components/ui/StatusChip.tsx` and its CSS per contracts/ui-components.md and data-model.md (`ChipState` type; desktop 96 x 24 px; phone 20 px high with width from the text; dot; colors from the soft and strong tokens: running and done = success, paused = warning, failed = danger, stopped and unknown = idle; props `state`, `text`, `testId`).
- [X] T012 Change `src/components/queues/IconButton.tsx` to a wrapper: import `ICON_PATHS`, `IconName`, `ICON_NAMES` from `../ui/icons` and re-export `IconName` and `ICON_NAMES`; keep the props, the classes (`icon-button`, `icon-button--touch`, `icon-button--destructive`), and the markup. Do not change `IconButton.css` in this task.
- [X] T013 Do not change `src/features/execution/StatusChip.tsx` and `StatusChip.css` (spec FR-014). Check that the new `src/components/ui/StatusChip.tsx` uses only the class prefix `ui-chip`, so that the two chips do not share CSS rules.
- [X] T014 [P] Add unit tests in `src/components/ui/__tests__/`: `icons.test.ts` (each name of rule B4 and each old name is in `ICON_NAMES`; each path is a non-empty string), `IconButton.test.tsx` (accessible name, tooltip, off state, danger class, touch class), `Button.test.tsx` (variant classes, off state, `type="button"`), `StatusChip.test.tsx` (class and `aria-label` for each state).
- [X] T015 Run `npm --prefix "C:\src\GameBot\src\web-ui" test` and `npm --prefix "C:\src\GameBot\src\web-ui" run lint`. All tests that passed in T001 must pass.

**Checkpoint**: Tokens, icons, Button, IconButton, and StatusChip are ready. The old wrappers keep the other pages unchanged.

---

## Phase 3: User Story 1 - Read the Queues list on the desktop with a stable layout (Priority: P1) 🎯 MVP

**Goal**: The desktop Queues table has fixed column widths and 56 px rows, and the status chip has the correct color.

**Independent Test**: `tests/e2e/queues-list-layout.spec.ts` passes (contracts/queues-list-layout.md, desktop part).

### Tests for User Story 1

- [X] T016 [P] [US1] Create `tests/e2e/queues-list-layout.spec.ts` (project `chromium`). Mock the API as in `tests/e2e/queues-actions-width.spec.ts`, with 5 queues: a stopped queue, a running queue with `operatorPause: 'none'`, a running queue with `operatorPause: 'paused'`, a queue with a name of 200 characters, and a queue with an empty activity. At 1280 x 800: check the widths of the Cycle (96), Status (132), Activity (320), and Actions (184) header cells (±1); check that each `tr.queues-row` is 56 px high (±1) and the header row is 40 px (±1); check that the long name cell has `scrollWidth > clientWidth` and a `title` with the full name; check the chip texts "Stopped", "Running", "Paused" and their classes `ui-chip--stopped`, `ui-chip--running`, `ui-chip--paused`. Then change the mock so that the stopped queue is `Running`, click Start, wait for the list to read again, and check that the widths and heights did not change. At 1024 x 800: check that each row is still 56 px. Use a mock API state variable so that the second list call returns the new status.
- [X] T017 [P] [US1] Create `src/components/ui/__tests__/ListView.test.tsx` for the desktop mode (`mode="desktop"`): the `<colgroup>` has one `<col>` for each column with the given width and no width for the flexible column; empty text shows "—"; each cell has a `title` from `column.text`; a click on a row calls `onRowClick`; a click in the actions cell does not call `onRowClick`; the open button in the first cell calls `onRowClick` with Enter; the states loading ("Loading…"), empty ("No items." or the given text), and error (text and **Try again**, which calls `onRetry`).

### Implementation for User Story 1

- [X] T018 [US1] Create `src/components/ui/ListView.tsx` (desktop mode) and its CSS in `src/components/ui/ui.css` per contracts/ui-components.md: props `ariaLabel`, `columns: ListColumn<T>[]`, `rows`, `getRowKey`, `onRowClick`, `rowActions?: (row) => RowActions`, `renderActions?: (row) => ReactNode`, `renderPhoneActions?: (row) => ReactNode`, `menuLabel?`, `openLabel?`, `actionsColumn?: { header: string; width: number }`, `state?: ListState`, `phone?`, `mode?`, `tableClassName?`, `rowClassName?`, `rowTestId?`; `<div class="ui-list">` card with `overflow-x: auto`; `<table class="ui-list__table <tableClassName>" aria-label>`; `<colgroup>`; `table-layout: fixed`; table `min-width` = sum of fixed widths + 160 px (inline style); header cells 40 px, uppercase 12 px muted; `tr` height `var(--gb-size-row)`; `td` one line with "…", `title`, `vertical-align: middle`, `padding: 0 16px`; the first column renders a `ui-list__open` button around the content (text style, no button look, full cell width, `text-align: start`); the actions cell is right-aligned, renders the visible actions as `IconButton` and the menu (Phase 4 adds `RowActionMenu`; until then render the menu actions as `IconButton`s), and calls `stopPropagation` on click; the state row spans all columns. Export the types `ListColumn`, `RowAction`, `RowActions`, `ListState` from `src/components/ui/index.ts`.
- [X] T019 [US1] Create `src/components/ui/PageHeader.tsx` and its CSS per contracts/ui-components.md, and add the unit test `src/components/ui/__tests__/PageHeader.test.tsx` (title is `<h2>`, back button with label "Back" calls `onBack`, the status chip shows, the actions show).
- [X] T020 [US1] Create `src/components/queues/queueStatus.ts` with `queueChipState(queue: Pick<QueueDto, 'status' | 'operatorPause'>): { state: ChipState; text: string }` per the table "Queue to chip state" in data-model.md, and the unit test `src/components/queues/__tests__/queueStatus.test.ts` for each row of the table.
- [X] T021 [US1] Change the list part of `src/pages/QueuesPage.tsx` (lines 485 to 539): replace `<h2>` and the "Create Queue" button with `<PageHeader title="Queues" actions={<Button variant="primary" icon="add" onClick={openCreate}>Create queue</Button>} />` (show the actions only when `!panelOpen`); replace the `<table>` with `<ListView>` with `tableClassName="queues-table"`, `rowClassName="queues-row"`, `ariaLabel="Queues table"`, the columns of contracts/queues-list-layout.md (Name flexible with `text` = name; Cycle 96 with "On"/"Off"; Status 132 with `<StatusChip {...queueChipState(q)} testId="queue-status" />`; Activity 320 with `text` = `buildQueueActivity(q, monitors[q.id])` and the cell attribute `data-testid="queue-activity"` (add a `cellTestId` field to `ListColumn` for this)), `actionsColumn={{ header: 'Actions', width: 184 }}`, `onRowClick={(q) => void openEdit(q.id)}`, and `state` from `loading` and the list length (empty text "No queues found."). Keep `tableMessage` and `tableError` above the list.
- [X] T022 [US1] Update the CSS of the old Queues table (search `queues-table`, `link-button`, and `status-chip status-running` in `src/**/*.css` and `src/pages/QueuesPage.tsx`) so that no old rule changes the new table: remove rules that only the old table used.
- [X] T023 [US1] Run the unit tests and `npx --prefix "C:\src\GameBot\src\web-ui" playwright test --config "C:\src\GameBot\src\web-ui\playwright.config.ts" queues-list-layout.spec --trace off`. Fix failures.

**Checkpoint**: The desktop Queues list has a stable layout.

---

## Phase 4: User Story 2 - Use the same row actions on the desktop (Priority: P1)

**Goal**: Three icons in fixed positions, a "⋮" menu with Duplicate and Delete, and the shared confirmation dialog.

**Independent Test**: `src/components/queues/__tests__/QueueRowActions.test.tsx` and the action part of `tests/e2e/queues-list-layout.spec.ts` pass.

### Tests for User Story 2

- [X] T024 [P] [US2] Create `src/components/ui/__tests__/RowActionMenu.test.tsx`: the button has `aria-haspopup="menu"` and `aria-expanded`; open moves the focus to the first item that is on; ArrowDown and ArrowUp move the focus; Enter selects and closes; Escape closes and gives the focus back to the button; a click outside closes; a danger item is last and has the danger class even when the caller gives it first; an off item is `aria-disabled="true"` and a click does nothing; when the button is near the bottom of the window (mock `getBoundingClientRect` and `window.innerHeight` so that the space below is less than the menu height), the menu has the class `ui-menu--up` and its `top` is above the button.
- [X] T025 [P] [US2] Create `src/components/ui/__tests__/ConfirmDialog.test.tsx`: `role="dialog"`, `aria-modal`, the title is the accessible name; the focus starts on **Cancel**; Escape calls `onCancel`; the confirm button has the danger class when `danger`; `busy` turns both buttons off; nothing renders when `open` is false.
- [X] T026 [US2] Rewrite `src/components/queues/__tests__/QueueRowActions.test.tsx` for the new actions (data-model.md, table "Queue row actions"): the 3 visible slots and their accessible names and off states for each queue state; the menu items Duplicate and Delete; Delete off while running; a pause call turns slot 1 off while it runs; a refused pause call calls `onError` with the text. Keep the current checks of the pause calls (`pauseQueue`, `resumeQueue`).
- [X] T027 [US2] Add to `tests/e2e/queues-list-layout.spec.ts`: for a stopped queue, the buttons in the actions cell are, in order, "Start <name>", "Stop <name>" (off), "Edit <name>", "More actions for <name>"; for a running queue, "Pause <name>", "Stop <name>", "Edit <name>", "More actions for <name>"; the menu of a stopped queue has "Duplicate" and "Delete"; Delete opens a dialog "Delete queue <name>?"; **Cancel** closes it with no DELETE call; **Delete** sends `DELETE /api/queues/<id>` and shows "Queue deleted successfully."; a click on the row name opens the edit form.
- [X] T028 [US2] Update `tests/e2e/queues-actions-width.spec.ts`: keep the selectors `table.queues-table` and `tr.queues-row`; check that the Actions header is 184 px (±1) instead of the old half-width rule; keep the 1024 px row check with the limit 57 px.

### Implementation for User Story 2

- [X] T029 [US2] Create `src/components/ui/RowActionMenu.tsx` and its CSS per contracts/ui-components.md (research R5: `position: fixed` from `getBoundingClientRect()`, upward when the space below is less than the menu height, close on scroll, resize, outside click, Escape, Tab; items 44 px; danger item sorted last). Use it for the menu in `src/components/ui/ListView.tsx` (desktop and phone), with the label "More actions for <row title>" (add a `menuLabel?: (row) => string` prop to `ListView`; default "More actions").
- [X] T030 [P] [US2] Create `src/components/ui/ConfirmDialog.tsx` and its CSS per contracts/ui-components.md (backdrop, 440 px wide card, radius `--gb-radius-dialog`, title 18 px, one paragraph, buttons on the right; save `document.activeElement` at open and restore it at close).
- [X] T031 [P] [US2] Create `src/components/ui/FooterBar.tsx` and its CSS per contracts/ui-components.md, and `src/components/ui/__tests__/FooterBar.test.tsx` (left and right areas render). FooterBar has no user in this PR; it is for the later form PRs (FR-012).
- [X] T032 [US2] Rewrite `src/components/queues/QueueRowActions.tsx`: export a hook `useQueuePauseCalls(queue, { onChanged, onError })` that gives `{ busy, slot1Action: RowAction }` (the current `runPauseCall` logic and the `busyRef` guard), and a function `queueRowActions(queue, handlers, pause): RowActions` that gives the 3 visible actions and the menu (Duplicate with icon `duplicate`; Delete with icon `delete`, tone `danger`, off while running), per data-model.md. Accessible names "<Action> <queue name>"; tooltips "<Action>". Keep the component `QueueRowActions` (same props) that renders the 3 icons and a `RowActionMenu`, for its unit test.
- [X] T033 [US2] In `src/pages/QueuesPage.tsx`: pass `rowActions` to the `ListView` with `queueRowActions` (use a small row component or call `useQueuePauseCalls` inside a cell component, because a hook cannot run in a loop; use a `QueueActionsCell` component that `ListView` renders through its `renderActions` prop, and keep the fixed actions column width). Start and Stop keep the current `runAction` calls and messages. Edit calls `openEdit`. Duplicate keeps `setDuplicateError(undefined); setDuplicateSource(q)`.
- [X] T034 [US2] In `src/pages/QueuesPage.tsx`: add the state `deleteTarget: { id: string; name: string } | undefined`; the Delete menu action sets it; render `<ConfirmDialog open={Boolean(deleteTarget)} title={`Delete queue ${name}?`} message="The queue and its settings are deleted. You cannot undo this." confirmText="Delete" danger ... />`; on confirm call `deleteQueue(id)` and use the same success message, `refresh()`, and 409 handling as the current `ConfirmDeleteModal` handler (lines 692 to 711); keep `ConfirmDeleteModal` for the Delete button of the edit form (`deleteOpen`).
- [X] T035 [US2] Run the unit tests and the browser tests `queues-list-layout.spec` and `queues-actions-width.spec`. Fix failures. Search the other unit tests that use the Queues page (`src/**/__tests__/*Queue*.test.tsx`, `src/__tests__/*.test.tsx`) for the old names ("Create Queue", "Delete <name>", "Duplicate <name>" as a button) and update them to the new names and the menu. Do not change the checks of the tests for polling, the start and stop messages, and the form panel (FR-021): they must stay green as they are.

**Checkpoint**: The desktop Queues list is complete.

---

## Phase 5: User Story 3 - See the same list on the phone (Priority: P2)

**Goal**: The phone Queues list uses the phone mode of `ListView`: 64 px rows, one main icon, and a "⋮" menu.

**Independent Test**: `tests/e2e/queues-list-layout.mobile.spec.ts` passes; `PhoneQueuesView` unit tests pass.

### Tests for User Story 3

- [X] T036 [P] [US3] Create `tests/e2e/queues-list-layout.mobile.spec.ts` (project `mobile`, 360 x 640). Use the same API mock and 5 queues as T016 (put the mock in `tests/e2e/helpers/queuesMock.ts` and use it in T016 and here). Check: each `[data-testid^="queue-card-"]` is 64 px high (±1); the main icon of a stopped queue is "Start <name>" and of a running queue is "Stop <name>"; each icon button is at least 44 x 44 px; the menu of a running queue with no pause has "Open" and "Pause", and of a stopped queue only "Open"; a tap on the row title opens the queue detail (`data-testid="phone-queue-detail"`); `scrollWidth <= clientWidth` at 320, 360, and 390 px; a queue with `deviceLiveness: 'not_live'` shows the "Not live" mark and its row is still 64 px.
- [X] T037 [P] [US3] Add phone-mode tests to `src/components/ui/__tests__/ListView.test.tsx` (`mode="phone"`): title on line 1, chip and meta on line 2, main action as a touch icon button, the menu, a tap on the open area calls `onRowClick`, the state rows.

### Implementation for User Story 3

- [X] T038 [US3] Add the phone mode to `src/components/ui/ListView.tsx` and its CSS per contracts/ui-components.md (`<ul class="ui-list__phone">`, `<li>` 64 px with `data-testid` from `rowTestId`, open button with two lines, main action `IconButton size="touch"`, `RowActionMenu size="touch"`; line 1 and line 2 one line each with "…"; no element wider than the row).
- [X] T039 [US3] Change the list part of `src/components/queues/PhoneQueuesView.tsx` (lines 454 to 475) to `<ListView mode="phone" ...>`: `rowTestId={(q) => `queue-card-${q.id}`}`; `phone.title` = name; `phone.chip` = `<StatusChip {...queueChipState(q)} testId="queue-status" />`; `phone.meta` = the activity text plus " (old data)" when `listStale`, plus `<NotLiveBadge />` when not live; main action Start or Stop with icon `play` or `stop`, label "<Start|Stop> <name>", calls `toggleRun(q)`, off when `busy || lost`; menu: "Open" (icon `expand`) that calls `open(q.id)`, and for a running queue the slot 1 action of `useQueuePauseCalls` (off when `lost`). Use a row component for the hook, as in T033, through the `renderPhoneActions` prop of `ListView`. Keep the texts "Loading…" and "No queues yet. Use a larger screen to create one." through `state`. Remove the local `QueueStatusChip` only if no other part of the file uses it (the detail page uses it: keep it, but make it use `queueChipState`).
- [X] T040 [US3] Update `src/components/queues/__tests__/PhoneQueuesView.test.tsx`, `src/components/queues/__tests__/PhoneQueuesView.entry.test.tsx`, `src/__tests__/PhoneQueuesView.taps.test.tsx`, and `src/__tests__/QueueActivity.parity.test.tsx` for the new names: the main action is an icon button named "Start <name>" or "Stop <name>" (was a text button "Start" or "Stop"); the card open button keeps the name "Open <name>" (set `aria-label` of the open button to "Open <title>"). Change only what the new layout changes.
- [X] T041 [US3] Run the unit tests and `npx --prefix "C:\src\GameBot\src\web-ui" playwright test --config "C:\src\GameBot\src\web-ui\playwright.config.ts" --project mobile --trace off`. Fix failures. The current `tests/e2e/mobile.spec.ts` must stay green. Do not change the checks of the tests for the connection-lost state and for "no action sent again" (FR-025).

**Checkpoint**: The phone and the desktop Queues lists use the same components.

---

## Phase 6: User Story 4 - Use the same tokens and components in later pages (Priority: P3)

**Goal**: The shared components are documented and exported, so that later PRs use them.

**Independent Test**: Each shared component has a unit test; `src/components/ui/index.ts` exports all of them.

- [X] T042 [US4] Export all components and types from `src/components/ui/index.ts`: `Icon`, `ICON_NAMES`, `IconName`, `Button`, `IconButton`, `PageHeader`, `StatusChip`, `ChipState`, `ListView`, `ListColumn`, `RowAction`, `RowActions`, `ListState`, `RowActionMenu`, `FooterBar`, `ConfirmDialog`.
- [X] T043 [US4] Add the section "13. Shared components" to `docs/ui-guidelines.md`: one line for each component in `src/web-ui/src/components/ui/` with the guideline rules that it implements, and the note that the old `components/queues/IconButton.tsx` and `features/execution/StatusChip.tsx` are wrappers until all pages move.
- [X] T044 [P] [US4] Add a theme check to `src/components/ui/__tests__/ui-css.test.ts`: read `src/components/ui/ui.css` and check that it has no hex color (`#` followed by 3 or 6 hex digits) and no `:hover` outside `@media (hover: hover)` (SC-005).

---

## Phase 7: Polish and cross-cutting concerns

- [X] T045 [P] Update `docs/mobile-ui-principles.md` "Last reviewed" date to 2026-11-27 and add feature 202 to its list. Check that the change of the pipeline input (link to `docs/ui-guidelines.md`, rules 5 and 11) is in the file.
- [X] T046 [P] Add the line `| 202 | Shared UI components and design tokens; Queues list | Implemented |` to `specs/STATUS.md`, and set the `**Status**:` line of `specs/202-ui-shared-components/spec.md` to `Implemented`.
- [X] T047 [P] Check `docs/architecture.md` for a web UI section that lists components or the theme. If it has one, add the shared component folder and refresh "Last reviewed". If not, make no change and record this in the implementation notes.
- [X] T048 Add a `CHANGELOG.md` entry (user-visible): "The web UI uses one design for lists, buttons, icons, and status chips. The Queues list has fixed column widths and row heights on the desktop and the phone. Delete asks for confirmation in one dialog." Write it in STE.
- [X] T049 Run the full checks: `npm --prefix "C:\src\GameBot\src\web-ui" test`, `npm --prefix "C:\src\GameBot\src\web-ui" run lint`, `npm --prefix "C:\src\GameBot\src\web-ui" run build`, and all Playwright tests (`npx --prefix "C:\src\GameBot\src\web-ui" playwright test --config "C:\src\GameBot\src\web-ui\playwright.config.ts" --trace off`). Fix each failure, also in old code. Then run `npm --prefix "C:\src\GameBot\src\web-ui" test -- --coverage --collectCoverageFrom="src/components/ui/**/*.{ts,tsx}" --collectCoverageFrom="src/components/queues/QueueRowActions.tsx" --collectCoverageFrom="src/components/queues/queueStatus.ts"` and check 80% line and 70% branch coverage (constitution II). Add tests until the values are met. Record the results.
- [X] T050 Do the manual check of `quickstart.md` with a screenshot of the desktop list (1280 px) and the phone list (360 px) by Playwright against the mocked API, and compare them with `docs/ui-guidelines/1-list-pattern.png`. Record differences in the implementation notes. Also measure the performance goals of plan.md: in the desktop browser test, mock 50 queues and measure the time from the response of `/api/queues` to the 50th visible `tr.queues-row` (record it; the goal is less than 200 ms); and check the size of `src/components/ui/ui.css` (the goal is less than 15 KB). Record both values in the PR description.

---

## Dependencies and execution order

- Phase 1 → Phase 2 → US1 (Phase 3) → US2 (Phase 4) → US3 (Phase 5) → US4 (Phase 6) → Phase 7.
- US2 depends on US1 (`ListView` desktop mode, the Queues table). US3 depends on US2 (`RowActionMenu`, `useQueuePauseCalls`).
- US4 depends on all components.

### Parallel opportunities

- Phase 2: T005, T010, T011, T014 after T003, T004, T007.
- US1: T016 and T017 in parallel, before T018.
- US2: T024, T025 in parallel; T030 and T031 in parallel with T029.
- US3: T036 and T037 in parallel.
- Phase 7: T045, T046, T047 in parallel.

## Implementation strategy

1. MVP: Phases 1 to 3 (tokens, base components, the stable desktop table).
2. Then US2 (actions and dialog), US3 (phone), US4 (exports and docs).
3. Run all tests after each phase. A red test stops the work until it is fixed.
