# Tasks: Use default label in the parameter value form

**Input**: Design documents in `C:\src\GameBot\specs\176-use-default-label\`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/ui-contract.md, quickstart.md

**Tests**: Included. The plan needs changed and new tests.

**Format**: `- [ ] [TaskID] [P?] [Story?] Description with file path`. [P] means the task can run in parallel (different files, no open dependency). Tasks that edit the same file have no [P] mark and run in sequence. All tasks that edit `ParameterBindingForm.tsx` (T008, T009, T014) have no [P] mark.

**Scope**: Web UI only. Do not change C# code, the `INHERIT` sentinel, the class `parameter-binding-inherit`, state variable names, or the resolve rules (FR-006, FR-007).

**Commands**: Use absolute paths and no directory change. Web UI tests: `npm --prefix "C:\src\GameBot\src\web-ui" test`. Web UI build: `npm --prefix "C:\src\GameBot\src\web-ui" run build`. Backend tests: `dotnet test "<absolute path to the solution file under C:\src\GameBot>"`.

## Phase 1: Setup

- [ ] T001 Find all uses of the word "Inherit" for the box: search `C:\src\GameBot\src\web-ui\src` (components and tests) and `C:\src\GameBot\docs` for "Inherit" and "inherit". Make a list of the places that name the box label. Do not change the `INHERIT` sentinel or C# names.

---

## Phase 2: Foundational

- [ ] T002 Add the helper `defaultDisplayText(declaration)` in `C:\src\GameBot\src\web-ui\src\components\parameters\types.ts`. Rules: `null` or `undefined` default returns "No default"; empty string returns "(empty)"; choice returns the option label (fallback: raw value); image returns the image id; number and text return the raw value as text.
- [ ] T003 [P] Add the rule `.parameter-binding-default` in `C:\src\GameBot\src\web-ui\src\styles.css` near the other `.parameter-*` rules: muted color, `overflow-wrap: anywhere`, `min-width: 0`.

**Checkpoint**: Helper and style exist. User story work can start.

---

## Phase 3: User Story 1 - Understand the box label (Priority: P1) MVP

**Goal**: The box label is "Use default" and the accessible name is `Use default <name>` in all 3 places.

**Independent Test**: Render the form. The box has the label "Use default". The word "Inherit" does not show. The accessible name is `Use default x` for parameter `x`.

### Tests for User Story 1

- [ ] T004 [P] [US1] Update the queries and expected text in `C:\src\GameBot\src\web-ui\src\components\parameters\__tests__\ParameterBindingForm.test.tsx` from "Inherit" to "Use default" (visible label and `Use default <name>` accessible name). Add a test that the word "Inherit" does not show.
- [ ] T005 [P] [US1] Update the queries in `C:\src\GameBot\src\web-ui\src\components\queues\__tests__\QueueSchedulingAreas.parameters.test.tsx` from "Inherit" to "Use default".
- [ ] T006 [P] [US1] Update any other web-ui test found in T001 that queries the box label (for example the tests of the run request and the step binding). Confirm the author preview has no box.

### Implementation for User Story 1

- [ ] T007 [US1] Run the tests of T004 to T006 and confirm they fail before the change (label not yet changed). Use `npm --prefix "C:\src\GameBot\src\web-ui" test`.
- [ ] T008 [US1] In `C:\src\GameBot\src\web-ui\src\components\parameters\ParameterBindingForm.tsx`, change the visible label to "Use default" and the `aria-label` to `Use default ${name}`. Keep the class `parameter-binding-inherit` and the `INHERIT` sentinel. Keep the box visible when there is no default (FR-005).
- [ ] T009 [US1] Change the code comments that use "Inherit" for this box to "Use default" in `C:\src\GameBot\src\web-ui\src\components\parameters\ParameterBindingForm.tsx` (FR-013). Write the comments in STE. Do this after T008 (same file).
- [ ] T010 [P] [US1] Change the code comments that use "Inherit" for this box to "Use default" in `C:\src\GameBot\src\web-ui\src\components\parameters\ParameterFormPreview.tsx` (FR-013). Write the comments in STE. Comments only.

**Checkpoint**: US1 tests pass. The label shows "Use default" in the queue template entry, run request, and step binding.

---

## Phase 4: User Story 2 - See which default applies (Priority: P2)

**Goal**: Each row shows the default value, or "No default", next to the label.

**Independent Test**: Render one parameter with a default and one without. The text next to each label is correct. The box shows in both rows.

### Tests for User Story 2

- [ ] T011 [P] [US2] Add unit tests for `defaultDisplayText` in the new file `C:\src\GameBot\src\web-ui\src\components\parameters\__tests__\defaultDisplayText.test.ts`: choice with label, choice with unknown value (fallback), number, image id, empty string, null, undefined.
- [ ] T012 [US2] Add tests in `C:\src\GameBot\src\web-ui\src\components\parameters\__tests__\ParameterBindingForm.test.tsx` for: a text default is shown; "No default" is shown when the default is null or undefined; an empty-string default shows "(empty)" and not "No default"; a choice default shows the option label; a number default shows the number text; an image default shows the image id; the box is visible when there is no default; the accessible name does not include the default text. Do this after T004 (same file).
- [ ] T013 [US2] Run the tests of T011 and T012 and confirm they fail before T014. Use `npm --prefix "C:\src\GameBot\src\web-ui" test`.

### Implementation for User Story 2

- [ ] T014 [US2] In `C:\src\GameBot\src\web-ui\src\components\parameters\ParameterBindingForm.tsx`, add a `span` with class `parameter-binding-default` next to the "Use default" label. Show `defaultDisplayText(declaration)`. Keep the text out of the `aria-label`. Do this after T008 and T009 (same file).

**Checkpoint**: US1 and US2 tests pass.

---

## Phase 5: Polish and Cross-Cutting

- [ ] T015 [P] Add a CHANGELOG entry under Unreleased in `C:\src\GameBot\CHANGELOG.md`, in ASD-STE100 STE (FR-010).
- [ ] T016 [P] Recheck `C:\src\GameBot\docs\parameters.md` and `C:\src\GameBot\docs\architecture.md` with the list from T001. Change text only if it names the box label "Inherit" (FR-008). Expected: little or no change, because "inherit" is a verb for the resolve rules.
- [ ] T017 [P] Add row 176 to `C:\src\GameBot\specs\STATUS.md` (same format as row 175) and set the Status in `C:\src\GameBot\specs\176-use-default-label\spec.md` as the project convention needs (FR-012).
- [ ] T018 Run `npm --prefix "C:\src\GameBot\src\web-ui" test`. Fix any failure.
- [ ] T019 Run `npm --prefix "C:\src\GameBot\src\web-ui" run build`. Then run the backend tests as a regression check with `dotnet test` and the absolute path to the solution file. Resolve and run tests must pass with unchanged expected values (FR-007).
- [ ] T020 Do the phone check at the narrowest width in `C:\src\GameBot\docs\mobile-ui-principles.md`, with a long default. Confirm no clipped text, no broken row, and a 44 px touch target (FR-009). Get the SC-004 reviewer result. Write both results for the PR description. Follow `C:\src\GameBot\specs\176-use-default-label\quickstart.md`.
- [ ] T021 Before push: run `git fetch origin master`. Bump the version in `C:\src\GameBot\installer\versioning\version.override.json` as CLAUDE.md says (patch = patch on `origin/master` + 1; if `minor` or `major` changed, patch = "0"). Set `updatedBy` and `updatedAtUtc` (FR-011).

---

## Dependencies and Order

- Phase 1 first. Phase 2 (T002, T003) blocks Phase 3 and Phase 4.
- Tasks on `ParameterBindingForm.tsx` run in this order: T008, T009, T014. None has a [P] mark.
- Tasks on `ParameterBindingForm.test.tsx` run in this order: T004, T012.
- T014 needs T002 (helper) and T003 (style).
- Phase 5 after Phases 3 and 4. T021 is last.

## Parallel Examples

- Phase 2: T003 runs with T002 (different files).
- US1 tests: T004, T005, T006 run in parallel (different files). T010 runs in parallel with T008 (different files).
- US2: T011 runs in parallel with T012 (different files).
- Phase 5: T015, T016, T017 run in parallel.

## Implementation Strategy

- MVP: Phase 1, Phase 2, Phase 3 (US1). This gives the new label and accessible name.
- Then add US2 (default text), then Phase 5.
- Do not commit in this step.
