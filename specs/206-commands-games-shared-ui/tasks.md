# Tasks: Move the Commands and Games pages to the shared UI components

**Input**: Design documents from `specs/206-commands-games-shared-ui/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/commands-games-layout.md, quickstart.md

**Tests**: The spec asks for tests (FR-025, FR-026).

**Paths**: Source paths are relative to `src/web-ui/` unless they start with `docs/` or `specs/`.

**Rules for each task**: Use only theme tokens in new CSS. Put each hover rule inside `@media (hover: hover)`. Write all comments and UI text in STE. Do not remove a behavior check from a test; change only names, selectors, texts, and layout checks. Edit UTF-8 files with the Edit or Write tool, not with PowerShell `Set-Content`.

## Format: `[ID] [P?] [Story] Description`

---

## Phase 1: Setup

- [ ] T001 Run `npm --prefix "C:\src\GameBot\src\web-ui" test`, `npm --prefix "C:\src\GameBot\src\web-ui" run lint`, and `npm --prefix "C:\src\GameBot\src\web-ui" run build` before any change. Record the results in the implementation notes.
- [ ] T002 Search `src/` and `tests/e2e/` for the parts that this feature removes or renames: `games-table`, `commands-table`, `command-name-cell`, `command-name`, `link-button`, `actions-header`, `FormActions`, `ConfirmDeleteModal`, `btn-secondary`, `btn-primary`, `btn-danger`, "Back to list", "Edit Game", "Edit Command", "Create Game", "Create Command", "Confirm Delete", "successfully.", "No games found.", "No commands found.". Keep the list of hits in the implementation notes; T020 and T021 use it.

---

## Phase 2: Foundational

- [ ] T003 Add `confirmDisabled?: boolean` to `src/components/ui/ConfirmDialog.tsx` per plan.md ("Shared changes"). Add tests to `src/components/ui/__tests__/ConfirmDialog.test.tsx`: with `confirmDisabled`, the confirm button is off and Cancel is on and calls `onCancel`; with `busy`, both are off.
- [ ] T004 Add the CSS classes `ui-filter-bar`, `ui-message-area`, `ui-form` (and its inner rules), `ui-references`, and `ui-num` to `src/components/ui/ui.css` per plan.md ("Shared changes").
- [ ] T005 Create `src/components/authoring/useDeleteFlow.ts` and `src/components/authoring/DeleteReferences.tsx` per plan.md ("Delete flow"). The hook gets the item kind (`'game' | 'command'`) for the default texts.
- [ ] T006 [P] Create `src/components/authoring/__tests__/useDeleteFlow.test.tsx`: `open` sets the target; `confirm` success calls `remove` once and `onDeleted`, and closes; a second `confirm` while busy sends no second call; a 409 `ApiError` with references keeps the target and sets the conflict (message and references); a 409 with an empty message gives the default text; another error closes and calls `onError`; `cancel` clears the target and the conflict. Add a `DeleteReferences` test: "Referenced by:", each group "<key> (<count>)", and the names.
- [ ] T007 Run the unit tests and lint.

---

## Phase 3: User Story 1 - Find a game or a command in the shared list (Priority: P1)

**Goal**: Both list pages on `PageHeader`, the filter bar, and `ListView`.

**Independent Test**: contract C-01 to C-04.

- [ ] T008 [US1] Change the list part of `src/pages/GamesPage.tsx` per plan.md ("Games page", list page). Remove the `<h2>Games</h2>`, the "Create Game" button, and the old table.
- [ ] T009 [US1] Change the list part of `src/pages/CommandsPage.tsx` per plan.md ("Commands page", list page). Remove the `<h2>Commands</h2>`, the old table, the Game column, `filterGame`, and the Game select. Remove the `listGames` call only if no other code in the page needs it.
- [ ] T010 [P] [US1] Update `src/pages/__tests__/GamesPage.spec.tsx` and the Commands page tests (`CommandsPage.spec.tsx` and the other `CommandsPage.*.spec.tsx`, `commandsPageTestSetup.tsx`, `CommandsAndSequencesPrimitiveAuthoring.spec.tsx`) for the list: the button names "Create game" / "Create command", the row open by a row click or "Edit <name>" (no link button), the empty texts "No games." / "No commands.", the error state with **Try again**, the filter. Add tests: the Package name cell shows "—" for an empty value; the Commands list has no column "Game".

---

## Phase 4: User Story 2 - Create and edit on a form page (Priority: P1)

**Goal**: The form pages with the back arrow, the 720 px card form, and the footer bar.

**Independent Test**: contract C-05 to C-07, C-10, C-12.

- [ ] T011 [US2] Change the form part of `src/pages/GamesPage.tsx` per plan.md ("Games page", form page): `PageHeader` with back and the title, `ui-form`, `FooterBar` with Delete (edit mode) / Cancel / Save, `loadedName`. Remove the "← Back to list" button, the `<h3>Edit Game</h3>`, and `FormActions`. Change the success texts to "Game created." and "Game updated.".
- [ ] T012 [US2] Change `src/components/commands/CommandForm.tsx` per plan.md ("Commands page and `CommandForm`"): the props `onDelete` and `deleting`, `FooterBar` instead of `FormActions`, and the add-step `Button`. Change `RecordedStepList.tsx` and `VisualStepPicker.tsx` in `src/components/commands/VisualStepPicker/` to the shared `Button` (same texts, `disabled`, and extra class names).
- [ ] T013 [US2] Change the form part of `src/pages/CommandsPage.tsx`: `PageHeader` with back and the title ("New command" or `loadedName`), `ui-form` around `CommandForm`, `onDelete` only in edit mode, and no separate Delete `div` and no `<h3>Edit Command</h3>` and no "← Back to list". Change the success texts to "Command created." and "Command updated.".
- [ ] T014 [P] [US2] Update the form tests (`GamesPage.spec.tsx`, the `CommandsPage.*.spec.tsx` files, `src/components/commands/__tests__/CommandForm.*.test.tsx`, the `VisualStepPicker` tests) for the new names and the footer bar. Add tests: the create titles "New game" / "New command"; the edit title is the loaded name and does not change when the name field changes; Delete shows only in edit mode; Save is the last button in the footer; the back arrow with unsaved changes asks first (keep the current prompt test, with the new control); an empty name shows the error with `aria-invalid="true"`.

---

## Phase 5: User Story 3 - Delete with the shared dialog (Priority: P2)

**Goal**: The row and footer Delete use the shared dialog and the delete hook.

**Independent Test**: contract C-08, C-09, C-11.

- [ ] T015 [US3] In `src/pages/GamesPage.tsx` and `src/pages/CommandsPage.tsx`: use `useDeleteFlow` and `ConfirmDialog` per plan.md ("Delete flow") for the row menu Delete and the footer Delete. `onDeleted`: close the form if it shows the deleted item, reload the list, show "Game deleted." / "Command deleted.". `onError`: the list message area on the list page, the form error on the edit page. Remove `ConfirmDeleteModal` and its state (`deleteOpen`, `deleteMessage`, `deleteReferences`) from both pages.
- [ ] T016 [P] [US3] Add unit tests to the Games and Commands page tests: row menu Delete → dialog title, message, Cancel focus; confirm → one delete call, the list reloads, the message; a 409 with references → the dialog shows "Referenced by:" and the names, Delete is off, Cancel closes; another error → the message area shows it; footer Delete on the edit page → after confirm the list shows with the message. Change the current delete tests to the new dialog texts (no behavior check removed).

---

## Phase 6: Browser tests

- [ ] T017 Create `tests/e2e/helpers/authoringMock.ts`: routes for `GET/POST /api/games`, `GET/PUT/DELETE /api/games/{id}`, `GET/POST /api/commands`, `GET/PUT/DELETE /api/commands/{id}`, and the other calls that the Commands form makes on open (search the services; return empty lists). Read the real paths from `src/services/games.ts` and `src/services/commands.ts`. A flag makes the delete of one id return 409 with a message and references.
- [ ] T018 Create `tests/e2e/authoring-commands-games-layout.spec.ts` (project `chromium`, 1280 px) for contract C-01 to C-14 (the mock of T017 has one game with a name of 200 characters; C-13 sets the viewport to 1024 x 768). Open the pages with `/?area=authoring&tab=Games` and `/?area=authoring&tab=Commands`.

---

## Phase 7: Polish

- [ ] T019 Remove unused CSS per plan.md ("CSS removal"). Search each class first (T002 list); keep a rule that other code still uses.
- [ ] T020 Search again for the T002 list in `src/` and `tests/e2e/`. Each hit that stays must be in a file outside this feature (for example Sequences or Triggers). Record the hits that stay and why.
- [ ] T021 [P] Update `docs/ui-guidelines.md`: section 13 (`ConfirmDialog` `confirmDisabled`; the `ui-form` card style for form pages; the filter bar and message area classes; `useDeleteFlow` and `DeleteReferences` as Authoring helpers) and the "Last reviewed" line (feature 206). Add the line for 206 to `specs/STATUS.md`. Check `docs/architecture.md` for `ConfirmDeleteModal`, `FormActions`, and the Commands and Games pages, and update it.
- [ ] T022 Run `npm --prefix "C:\src\GameBot\src\web-ui" run lint`, `npm --prefix "C:\src\GameBot\src\web-ui" run build`, and `npm --prefix "C:\src\GameBot\src\web-ui" test` (the full suite, with the coverage limits). All must pass.
- [ ] T023 Run all Playwright tests per quickstart.md step 3 (both projects, all files). All must pass. If a test fails only with a page-load timeout, run it again once and record it.
- [ ] T024 Visual check (do not skip): with the preview server and the mock of T017, take screenshots at 1280 x 800 of the Games list, the Commands list, the Games edit page, the Commands edit page (top and scrolled to the bottom), and the delete dialog with the 409 references, to `C:\Users\anton\AppData\Local\Temp\gb-ui-mock\cg-*.png`. Open each screenshot and compare it with mockup A of `docs/ui-guidelines/2-form-detail-editor-logs.png` and the list of `docs/ui-guidelines/1-list-pattern.png`: one title, the back arrow, cards, labels above the fields, the footer bar at the bottom with Save last, no old buttons, space between the parts. Fix each defect and repeat. Record the result.
- [ ] T025 Measure the time from a row click to the edit page header with the mock (Playwright, 5 runs). Record the median; the goal is less than 100 ms after the `get` answer.

---

## Dependencies & Execution Order

- Phase 1 → Phase 2 → Phase 3 → Phase 4 → Phase 5 → Phase 6 → Phase 7.
- T008 and T011 change the same file (`GamesPage.tsx`); T009 and T013 the same (`CommandsPage.tsx`). Do them in order.
- T012 comes before T013 (the page passes the new props).
- T015 needs T003 and T005.

## Parallel Example

- Phase 2: T006 in parallel with T004.
- T010, T014, T016 can run in parallel with the next implementation task of another file.

## Implementation Strategy

1. Shared parts (Phase 2).
2. Lists (US1), then form pages (US2), then delete (US3).
3. Browser tests, cleanup, documents, visual check, measurement.
