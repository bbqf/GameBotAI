# Tasks: Move the Sequences list, form page, and delete to the shared UI components

**Input**: Design documents from `specs/207-sequences-shared-ui/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/sequences-layout.md, quickstart.md

**Tests**: The spec asks for tests (FR-021, FR-022).

**Paths**: Source paths are relative to `src/web-ui/` unless they start with `docs/` or `specs/`.

**Rules for each task**: Use only theme tokens in new CSS. Put each hover rule inside `@media (hover: hover)`. Write all comments and UI text in STE. Do not remove a behavior check from a test; change only names, ids, selectors, texts, and layout checks. Edit UTF-8 files with the Edit or Write tool, not with PowerShell `Set-Content`. The step rows, the loop and if blocks, and their inline editors do not change (part 2b).

## Format: `[ID] [P?] [Story] Description`

---

## Phase 1: Setup

- [ ] T001 Run `npm --prefix "C:\src\GameBot\src\web-ui" test`, `npm --prefix "C:\src\GameBot\src\web-ui" run lint`, and `npm --prefix "C:\src\GameBot\src\web-ui" run build` before any change. Record the results in the implementation notes.
- [ ] T002 Search `src/` and `tests/e2e/` for the parts that this feature removes or renames: `sequences-table`, `sequences-row`, `sequence-steps-separator`, `sequence-badge`, `link-button`, `actions-header`, "Create Sequence", "No sequences yet", "No sequences found.", "Back to list", "Edit Sequence", "Sequence created successfully.", "Sequence updated successfully.", "Sequence deleted successfully.", "Confirm Delete", `sequence-edit-` (the edit ids). Keep the list in the implementation notes; T017 and T018 use it.
- [ ] T003 Compare the create form and the edit form of `src/pages/SequencesPage.tsx` line by line (plan.md, "Merge of the two forms"). Write each difference (ids, `disabled` rules, handlers, texts, comments) in the implementation notes, with the decision: keep by mode, same in both, or a bug (name it).

---

## Phase 2: Foundational

- [ ] T004 Add `.ui-form--wide { max-width: 880px; }` and `.ui-muted { color: var(--gb-color-text-muted); }` (no such class exists now) to `src/components/ui/ui.css`. Add the kind `'sequence'` to `src/components/authoring/useDeleteFlow.ts` with the texts of plan.md ("Delete"), and a test for its default 409 text in `src/components/authoring/__tests__/useDeleteFlow.test.tsx`. If the "Step through" button needs `aria-describedby`, add an optional `ariaDescribedBy` prop to `src/components/ui/Button.tsx` with a test.
- [ ] T005 Run the unit tests and lint.

---

## Phase 3: User Story 1 - Find a sequence in the shared list (Priority: P1)

**Goal**: The list page on `PageHeader`, the message area, the filter bar, and `ListView`.

**Independent Test**: contract S-01 to S-04, S-12.

- [ ] T006 [US1] Change the list part of `src/pages/SequencesPage.tsx` per plan.md ("Page layout", list page). Remove the `<h2>`, the hint "No sequences yet…", the old table, and the link button.
- [ ] T007 [P] [US1] Update the list tests in `src/pages/__tests__/SequencesPage.spec.tsx`, `SequencesPage.excludeNotifications.spec.tsx`, `SequencesPage.ignoreSmartScheduling.spec.tsx`, and the other tests from the T002 list that use the list: "Create sequence", the row open by a row click or "Edit <name>", "No sequences.", the error with **Try again**, the filter. Change the badge checks to the Options column texts. Add tests: both options → "No success notifications, Ignores smart scheduling"; none → "—"; the Steps value.

---

## Phase 4: User Story 2 - Create and edit on one form page (Priority: P1)

**Goal**: One form with the back arrow, "Step through" in the header, the 880 px card form, and the footer bar.

**Independent Test**: contract S-05 to S-07, S-10.

- [ ] T008 [US2] Merge the create form and the edit form of `src/pages/SequencesPage.tsx` into one render path per plan.md ("Merge of the two forms") and the T003 notes. Keep the payload rules of data-model.md exactly. Change the success texts to "Sequence created." and "Sequence updated.".
- [ ] T009 [US2] Change the form page layout per plan.md ("Page layout", form page): `PageHeader` with back, the title ("New sequence", `loadedName`, or "Loading…"), and "Step through" in the header; the reason in the message area (`id="step-through-reason"`); `ui-form ui-form--wide`; `FooterBar`. Remove "← Back to list", `<h3>Edit Sequence</h3>`, the `hr` elements, and `FormActions`.
- [ ] T010 [US2] Change the add-step toolbar buttons to the shared `Button` (secondary) per plan.md (same texts, `testId` for each current `data-testid`, same `disabled`).
- [ ] T011 [P] [US2] Update the form tests (all `SequencesPage*.spec.tsx`, `SequencesPageWaitForImageUntil.test.tsx`, `SequencesPageWaitForStill.test.tsx`, `CommandsAndSequencesPrimitiveAuthoring.spec.tsx`, `SequencesPage.stepThrough.spec.tsx`): the edit ids become the create ids; "← Back to list" becomes the "Back" button; "Edit Sequence" checks become the title checks. Add tests: (a) create with no parameters and options off → the create call has no `parameters`, `excludeFromSuccessNotifications`, `ignoreSmartScheduling`, `onTimeLimit` members; (b) create with the options on and "end" → `true`, `true`, `'end'`; (c) update always sends the 4 members (also with empty parameters and options off); (d) the version conflict shows the text and loads the sequence again (the `get` call count rises by 1); (e) the titles "New sequence" and the loaded name (the title does not change while the name field changes); (f) Delete only in edit mode, Save last; (g) "Step through" is in the header and its reason has the id `step-through-reason`.

---

## Phase 5: User Story 3 - Delete a sequence with the shared dialog (Priority: P2)

**Goal**: Row and footer Delete with the shared dialog.

**Independent Test**: contract S-08, S-09.

- [ ] T012 [US3] Use `useDeleteFlow` (kind `sequence`), `DeleteReferences`, and `ConfirmDialog` in `src/pages/SequencesPage.tsx` per plan.md ("Delete"). Remove `ConfirmDeleteModal`, `deleteOpen`, `deleteMessage`, and `deleteReferences` from the page.
- [ ] T013 [P] [US3] Add unit tests: row menu Delete → dialog title and message, focus on Cancel; confirm → one delete call, the list reloads, "Sequence deleted."; 409 with references → "Referenced by:" and the names, Delete off, Cancel closes; another error → the message area; footer Delete on the edit page → the list with "Sequence deleted.". Change the current delete tests to the new dialog texts.

---

## Phase 6: Browser tests

- [ ] T014 Extend `tests/e2e/helpers/authoringMock.ts` with sequences: `GET/POST /api/sequences`, `GET/PUT/DELETE /api/sequences/{id}` (read the real paths in `src/services/sequences.ts`), the calls that the form makes on open (commands list, images; return empty lists where possible), 3 default sequences (one with both options, one with a name of 200 characters, one with no option), and a flag that makes the delete of one id return 409 with references.
- [ ] T015 Create `tests/e2e/authoring-sequences-layout.spec.ts` (project `chromium`, 1280 px) for contract S-01 to S-12. Open the page with `/?area=authoring&tab=Sequences`.
- [ ] T016 Run the new file and the files of feature 206 (`authoring-commands-games-layout.spec.ts`) to check that the helper change does not break them.

---

## Phase 7: Polish

- [ ] T017 Remove unused CSS per plan.md ("CSS"). Search each class first (T002 list); keep a rule that other code still uses.
- [ ] T018 Search again for the T002 list in `src/` and `tests/e2e/`. Each hit that stays must be outside this page (for example Triggers or Queues). Record the hits that stay and why. Check the line count of `SequencesPage.tsx` against the start (spec SC-003: at least 250 lines less) and record it.
- [ ] T019 [P] Update `docs/ui-guidelines.md`: section 13 (`ui-form--wide` for a form with the step editor, max. 880 px) and the "Last reviewed" line (feature 207). Add the line for 207 to `specs/STATUS.md`.
- [ ] T020 Run `npm --prefix "C:\src\GameBot\src\web-ui" run lint`, `npm --prefix "C:\src\GameBot\src\web-ui" run build`, and `npm --prefix "C:\src\GameBot\src\web-ui" test` (the full suite, with the coverage limits). All must pass.
- [ ] T021 Run all Playwright tests per quickstart.md step 3 (both projects, all files). All must pass. If a test fails only with a page-load timeout, run it again once and record it.
- [ ] T022 Visual check (do not skip): with the preview server and the mock of T014, take screenshots at 1280 x 800 of the Sequences list, the create page, the edit page (top, the Steps card with the add-step toolbar, and scrolled to the bottom), and the delete dialog with the 409 references, to `C:\Users\anton\AppData\Local\Temp\gb-ui-mock\sq-*.png`. Open each screenshot and compare it with `docs/ui-guidelines/2-form-detail-editor-logs.png` (A) and the Commands pages of feature 206: one title, the back arrow, "Step through" in the header, cards, the footer bar at the bottom with Save last and on the right also in create mode, no old buttons, space between the parts. Fix each defect and repeat. Record the result.
- [ ] T023 Measure the time from a row click to the edit page header with the mock (Playwright, 5 runs). Record the median; the goal is less than 100 ms after the `get` answer.

---

## Dependencies & Execution Order

- Phase 1 → Phase 2 → Phase 3 → Phase 4 → Phase 5 → Phase 6 → Phase 7.
- T003 comes before T008. T006, T008, T009, T010, and T012 change the same file; do them in order.
- T012 needs T004.

## Parallel Example

- T007, T011, and T013 (tests) can run in parallel with the next implementation task.
- T019 in parallel with T017.

## Implementation Strategy

1. Notes (T002, T003) and the shared changes.
2. List (US1), then the merged form (US2), then delete (US3).
3. Browser tests, cleanup, documents, visual check, measurement.
