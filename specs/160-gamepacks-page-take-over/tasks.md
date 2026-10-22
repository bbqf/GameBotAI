# Tasks: Gamepacks Page Take-Over Option

**Input**: Design documents from `/specs/160-gamepacks-page-take-over/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/catalog-install-ui.md, quickstart.md

**Tests**: The spec requires tests (see "Testing and Verification Notes"). The test runner is Jest with React Testing Library, not Vitest.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: The task can run in parallel (different files, no dependency on an open task).
- **[Story]**: The user story of the task (US1, US2).
- All paths are from the repository root `C:\src\GameBot`.

## Phase 1: Setup

**Purpose**: Find the files and create the missing test file.

- [ ] T001 Read `src/web-ui/src/pages/GamepacksPage.tsx`, `src/web-ui/src/services/gamepackCatalog.ts` and `src/web-ui/src/pages/__tests__/GamepacksPage.test.tsx`. Note the Dry run check box, the Install and Update buttons, the dry run notice, and the existing mocks of the service.
- [ ] T002 Create `src/web-ui/src/services/__tests__/gamepackCatalog.test.ts` if it is absent (Jest APIs only). Add a first test that checks the existing request of `installFromCatalog`. List this file in the PR text as a new file.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The service sends `takeOver` and returns `takenOver`. All user stories need this.

- [ ] T003 Change `installFromCatalog` in `src/web-ui/src/services/gamepackCatalog.ts` to the signature `installFromCatalog(name, version, dryRun, takeOver)`. Send the body `{ name, version, dryRun, takeOver }`. Add the optional field `takenOver?: number` to `CatalogInstallResult`. Keep the error handling (HTTP 422 gives `CatalogApiError`).
- [ ] T004 Update every call of `installFromCatalog` in `src/web-ui/src/pages/GamepacksPage.tsx` to pass a temporary `false` for `takeOver`, so the build passes. T008 replaces this temporary value.
- [ ] T005 [P] Add a service test in `src/web-ui/src/services/__tests__/gamepackCatalog.test.ts`: `installFromCatalog` sends `{ name, version, dryRun, takeOver }` for takeOver true and false, and keeps `takenOver` in the result.
- [ ] T006 Run `npm test` in `src/web-ui` for the service test and `npm run build`. Fix errors before you continue.

**Checkpoint**: The service is ready. The page can use `takeOver`.

---

## Phase 3: User Story 1 - Update from the catalog with take-over (Priority: P1) MVP

**Goal**: The operator turns on take-over, runs the dry run, sees `Take over N.`, and presses Update.

**Independent Test**: A catalog gamepack with N same-named user objects. The dry run shows N conflicts. With take-over on, the dry run shows `Take over N.`. Update sends `takeOver` true and succeeds.

### Tests for User Story 1

Write these tests first in `src/web-ui/src/pages/__tests__/GamepacksPage.test.tsx`. They must fail before T008 to T012.

- [ ] T007 [P] [US1] Add Jest tests in `src/web-ui/src/pages/__tests__/GamepacksPage.test.tsx` for these cases:
  - The take-over check box (label `Take over objects of the user with the same name`) and its hint show at load, and the box is off (SC-002).
  - FR-005: the box is off, the dry run finds conflicts, the page lists the conflicts, and the box is still available.
  - A6: the dry run request sends `takeOver` false when the box is off, and true when it is on.
  - The notice shows `Take over N.` for a dry run made with take-over on, valid or not.
  - Edge case: the dry run with take-over on returns `takenOver` 0. The notice shows `Take over 0.` and the Update button is enabled.
  - The notice has no `Take over` count for a dry run made with take-over off.
  - With take-over on and no valid matching dry run, the Update button is disabled and the row shows `Run the dry run with take-over first.`
  - A real Update after a valid matching dry run sends `takeOver` true. With the box off, with or without a dry run, it sends `takeOver` false.
  - A5: a change of the take-over box clears the notice and disables the Update button and the Install button (take-over on).
  - A change of the Dry run box keeps the last dry run result.
  - Edge case: a failed dry run request shows the error and the operator can try again.

### Implementation for User Story 1

- [ ] T008 [US1] In `src/web-ui/src/pages/GamepacksPage.tsx`, add the page state `takeOver` (default false) and `lastDryRun` (`{ key, takeOver, valid, takenOver }` or null, as in data-model.md). Replace the temporary `false` from T004 with the real `takeOver` value in the dry run call and in the install call (A10). Save `lastDryRun` after each dry run response.
- [ ] T009 [US1] In `src/web-ui/src/pages/GamepacksPage.tsx`, render the take-over check box next to the Dry run check box. Use the class `gamepacks-check`. Use the label `Take over objects of the user with the same name` and the hint `The gamepack owns these objects and replaces them. The old content goes to a backup.` The box is off at load and is not kept between visits.
- [ ] T010 [US1] In `src/web-ui/src/pages/GamepacksPage.tsx`, add the rule for the buttons. With take-over on and Dry run off, enable the Install or Update button only when `lastDryRun` exists, is valid, and has the same `key` and `takeOver` as the current version and box. With Dry run on, the button stays enabled. With take-over off, keep the current behaviour. In the row of a disabled version, show the hint `Run the dry run with take-over first.`
- [ ] T011 [US1] In `src/web-ui/src/pages/GamepacksPage.tsx`, show `Take over N.` in the dry run notice for each dry run made with take-over on, valid or not. Keep the conflict and problem lines visible. With take-over off, show no count. Do not change the failure explanation text (FR-010).
- [ ] T012 [US1] In `src/web-ui/src/pages/GamepacksPage.tsx`, make a change of the take-over box set `lastDryRun` to null and clear the dry run notice. Make a change of the Dry run box keep `lastDryRun`. The page must not start a dry run by itself.
- [ ] T013 [US1] Run `npm test` and `npm run build` in `src/web-ui`. All tests of T007 must pass.

**Checkpoint**: The catalog update works with take-over. This is the MVP.

---

## Phase 4: User Story 2 - Install from the catalog with take-over (Priority: P1)

**Goal**: The Install button of a not-installed gamepack has the same take-over behaviour as the Update button.

**Independent Test**: A catalog gamepack that is not installed and conflicts with user objects. Do the checks of User Story 1 with the Install button.

### Tests for User Story 2

- [ ] T014 [P] [US2] Add Jest tests in `src/web-ui/src/pages/__tests__/GamepacksPage.test.tsx` for these cases:
  - The take-over box applies to the Install button of a not-installed gamepack, and it is off.
  - With take-over on and a valid matching dry run, Install sends `takeOver` true.
  - A valid dry run of version A with take-over on does not enable the Install or Update button of version B. The row of B shows `Run the dry run with take-over first.`

### Implementation for User Story 2

- [ ] T015 [US2] In `src/web-ui/src/pages/GamepacksPage.tsx`, check that the rules of T008 to T012 apply to the Install button and to the Update button of every catalog version. Use one shared code path for both buttons. Fix any difference. A dry run result is valid only for its own `name@version`.
- [ ] T016 [US2] Run `npm test` and `npm run build` in `src/web-ui`. All tests of T014 must pass.

**Checkpoint**: Install and Update behave the same.

---

## Phase 5: Polish and Cross-Cutting Concerns

- [ ] T017 [P] Update `docs/gamepacks.md`. Describe the take-over check box, the dry run rule, the text `Take over N.`, and the gap: the page has no file install, so the file part of the issue criterion 6 is not applicable (FR-007, FR-011).
- [ ] T018 [P] Run a grep for "Gamepacks" in `docs/architecture.md`. If a match is affected by this change, update the "Last reviewed" line in that file. Else make no change.
- [ ] T019 Do the manual phone check from `specs/160-gamepacks-page-take-over/quickstart.md`: 375 px width, touch target of 44 px, no horizontal scroll (FR-012). If long text overflows, fix `src/web-ui/src/phone.css`. Record the result in the PR text.
- [ ] T020 Run the full `npm test` and `npm run build` in `src/web-ui`. Run the quickstart manual check on the desktop.
- [ ] T021 Prepare the PR text in STE. State the gap (the page has no file install; the file install is out of scope). State the result of the phone check from T019. Include `Closes #336`.
- [ ] T022 Set the Status line of `specs/160-gamepacks-page-take-over/spec.md` to `Implemented`. Add the row for feature 160 in `specs/STATUS.md`.

---

## Dependencies and Execution Order

- Phase 1 first. Phase 2 needs Phase 1. Phase 2 blocks all stories.
- US1 (Phase 3) needs Phase 2. US2 (Phase 4) needs US1, because it checks the shared code path in the same page file.
- Phase 5 needs Phase 3 and Phase 4. T022 is the last task.
- T004 uses a temporary `false`. T008 replaces it.
- Tasks T008 to T012 edit the same file, so they run in order.

## Parallel Example

- T005 (service test) and T004 (page call update) edit different files.
- T007 and T014 edit the same test file, so write them one after the other. They can be drafted before the code.
- T017 and T018 edit different files and can run together.

## Implementation Strategy

- MVP: Phase 1, Phase 2, and Phase 3 (the catalog update with take-over).
- Then Phase 4 to prove that Install behaves the same.
- Then Phase 5 for docs, the phone check, the PR text, and the status.
