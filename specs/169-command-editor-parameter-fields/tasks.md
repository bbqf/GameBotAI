# Tasks: Command Editor Parameter Fields

**Input**: Design documents from `/specs/169-command-editor-parameter-fields/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/ui-contract.md, quickstart.md

**Tests**: Included. The constitution (Principle II) and the plan require unit, component, round-trip, drift, and service check tests.

**Organization**: Tasks follow the phase order of plan.md (A to E). Web UI only. The service, the API, and the format of `FieldTemplates` do not change (FR-010). No file of feature 168 changes.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an open task)
- **[Story]**: User story (US1, US2, US3)
- All text, labels, and hints follow ASD-STE100 (constitution Principle VI)

## Path Conventions

- Web UI: `src/web-ui/src/`
- Tests: `src/web-ui/src/**/__tests__/`, `tests/unit/Parameters/`, `tests/integration/Commands/`
- Path source: `CommandStepFieldPaths` in `src/GameBot.Domain/Commands/CommandStep.cs` (read only)

## Scope rules for this task list

- Only 12 fields get a control: Swipe 5 (`startX`, `startY`, `endX`, `endY`, `durationMs`), Tap 3 numeric (`confidence`, `offsetX`, `offsetY`) and 1 image, WaitForImage 1 numeric (`timeoutMs`) and 1 image, EnsureEmulatorRunning 1 (`instanceIndex`). The 4 `ensureGameRunning` numeric paths get no control.
- Task T008 builds the complete control: plain, parameter, and "Parameter not found" states, the queue values group, the empty state, the hints, the ARIA names, keyboard support, and the `error` prop. No later task builds these again.
- Each panel task (Phase B and C) also wires the `parameters`, `fieldTemplates`, and `fieldErrors` props to each control of that panel. The error display tasks in Phase D do not touch the panels.
- Type change rule (one rule): on a type change, drop each template key that does not belong to the new step type and keep the other keys. A step of the same type keeps all keys, including keys with no control.

---

## Phase A: Shared base

**Purpose**: Path mirror, fit function, lossless `fieldTemplates` round trip, type change rule, drift test, the complete shared control, shared panel test helper.

**CRITICAL**: No story phase starts before Phase A is complete.

- [ ] T001 Run the web UI tests in `src/web-ui` once to record the baseline (all pass before the change). Read `CommandStepFieldPaths` in `src/GameBot.Domain/Commands/CommandStep.cs`. Write down the 16 mirrored paths with kind, and the 12 that get a control.
- [ ] T002 Add `fieldTemplates?: Record<string, string>` to `StepEntry` in `src/web-ui/src/components/commands/CommandForm.tsx`. A new step starts with no templates.
- [ ] T003 [P] Create `src/web-ui/src/components/parameters/fieldTemplatePaths.ts` with the mirror of all 16 paths (with kind) and the functions `isImagePath`, `isParametrizablePath`, `parameterFit(declaration, path)`, `templateParameterName(template)`, and `dropForeignTemplateKeys(templates, newStepType)` (the type change rule) per data-model.md and research R1 and R5.
- [ ] T004 [P] Write unit tests in `src/web-ui/src/components/parameters/__tests__/fieldTemplatePaths.test.ts`: fit rules for number, text, pattern, image kind, and choice (number and text options) on numeric and image paths; the exact hint texts of ui-contract.md; `templateParameterName` for a whole placeholder, a partial placeholder, and no placeholder; `dropForeignTemplateKeys` drops keys of other step types and keeps all other keys, and keeps all keys when the type is the same.
- [ ] T005 [P] Write the drift test `tests/unit/Parameters/FieldTemplatePathMirrorTests.cs`. It reads `fieldTemplatePaths.ts` and compares paths and kinds with `CommandStepFieldPaths` in `src/GameBot.Domain/Commands/CommandStep.cs`. It fails when the lists differ (research R2).
- [ ] T006 Update `stepsFromDto` and `stepsToDto` in `src/web-ui/src/pages/CommandsPage.tsx` to copy `fieldTemplates` for every step type. Send the map only when it has one entry or more (research R1).
- [ ] T007 [P] Write a round-trip test in `src/web-ui/src/pages/__tests__/CommandsPage.fieldTemplates.spec.tsx`: a command with templates, including an `ensureGameRunning` path that has no control, loads and saves with the templates unchanged (SC-005, FR-007, FR-008). Test that an empty map is not sent.
- [ ] T008 Create `src/web-ui/src/components/parameters/ParameterSourceControl.tsx` per data-model.md and ui-contract.md. This one task builds the whole control: (a) plain, parameter, and missing states, with the chip text "Parameter not found" for a name that is not a parameter and not a queue value, and a remove button that works in each state; (b) the open button and the listbox of the command parameters; (c) the separate group "Queue values" (use `buildEditorScope` and `QUEUE_BUILT_INS`) with no type hint; (d) the empty state text "This command has no parameters. Add one in the Parameters section." with no template written; (e) type hints from `parameterFit`, linked to the row with `aria-describedby`, row stays selectable (FR-006); (f) the `error` prop shown with `role="alert"` and linked with `aria-describedby`; (g) ARIA names and keyboard support (Tab, Enter, Space, arrows, Escape returns focus) per ui-contract.md (FR-011).
- [ ] T009 [P] Write component tests in `src/web-ui/src/components/parameters/__tests__/ParameterSourceControl.test.tsx`: plain to parameter to plain; replace by another parameter; empty state and no template written; hint on a row that does not fit and the row stays selectable; choice with number options has no hint, choice with text options has the hint; queue values group has no hint; "Parameter not found" chip, remove works, no block; accessible names of the open button, the listbox, the options, and the remove button (FR-011); keyboard open, move, select, and Escape with focus return; the `error` text shows with `role="alert"`; render-time check: the control opens with 50 parameters in under 100 ms (assert on the render time, with a wide margin for slow CI, or add a reasoned note in the test file if a timing test is not stable) (G1).
- [ ] T010 Pass `parameters` (declared parameters of the command) from `CommandForm.tsx` to the step panels, and read `fieldTemplates` from the confirm value of each panel back into `StepEntry`. Apply `dropForeignTemplateKeys` when the step type changes. Add the props `parameters`, `fieldTemplates`, and `fieldErrors` to the panel prop types only (the panel files get the wiring in Phase B and C).
- [ ] T011 [P] Add a test for the type change rule in `src/web-ui/src/components/commands/__tests__/CommandForm.fieldTemplates.test.tsx`: after a change from Swipe to Tap, the `swipe.` keys are gone and the other keys stay (also a key with no control); a change that keeps the same type keeps all keys.
- [ ] T012 Create the shared panel test helper `src/web-ui/src/components/commands/__tests__/parameterPanelTestHelpers.tsx`. It exports: a sample parameter list (number `delay`, text `label`, image `buttonToTap`, choice with number options, choice with text options), a `renderPanelWithParameters` function, and the functions `selectParameter(fieldLabel, name)`, `removeParameter(fieldLabel)`, `expectPlainInput(fieldLabel)`, and `expectFieldError(fieldLabel, message)`. All panel tests (T013, T014, T015, T016, T022, T023) use this one helper.

**Checkpoint**: Round trip is lossless. The control is complete and works alone. The panels can receive the new props.

---

## Phase B: User Story 1 - Use a parameter in a numeric field (P1) MVP

**Goal**: A person sets a numeric step field to a parameter and saves. The command has `{{name}}` in `fieldTemplates`.

**Independent Test**: Open a command with a number parameter `delay` and a Swipe step. Select `delay` for the duration. Save. The saved command has `swipe.durationMs` = `{{delay}}`. Remove the parameter: the plain input returns and the key is gone.

### Tests for User Story 1

- [ ] T013 [P] [US1] Write `src/web-ui/src/components/commands/__tests__/SwipePanel.parameters.test.tsx` with the helper of T012: select a parameter for `durationMs` and for a coordinate; the plain input is hidden; the confirm value has the template; remove restores the plain input and the kept plain value; a field with a template passes validation; the saved plain value is 0 for an empty required whole-number field; a `fieldErrors` entry for a path shows at the control of that path only.
- [ ] T014 [P] [US1] Write the same kind of tests for Tap numeric fields (`confidence`, `offsetX`, `offsetY`) in `src/web-ui/src/components/commands/__tests__/TapPanel.parameters.test.tsx`, with the helper of T012. Include the `fieldErrors` check.
- [ ] T015 [P] [US1] Write the same kind of tests for WaitForImage `timeoutMs` in `src/web-ui/src/components/commands/__tests__/WaitForImagePanel.parameters.test.tsx`, with the helper of T012. Include the `fieldErrors` check.
- [ ] T016 [P] [US1] Write the same kind of tests for EnsureEmulatorRunning `instanceIndex` in `src/web-ui/src/components/commands/__tests__/EnsureEmulatorRunningPanel.parameters.test.tsx`, with the helper of T012. Include the `fieldErrors` check.

### Implementation for User Story 1

- [ ] T017 [US1] Wrap the 5 numeric fields of `src/web-ui/src/components/commands/SwipePanel.tsx` with `ParameterSourceControl`. Wire the `parameters`, `fieldTemplates`, and `fieldErrors` props: pass `parameters`, the template of the path, and `fieldErrors[path]` as `error` to each control. Keep the plain value in local state while a template is active. On confirm send the kept plain value, or `0` for an empty required whole-number field (research R6). Treat a field with a template as valid. Keep template keys that the panel has no control for.
- [ ] T018 [P] [US1] Wrap `confidence`, `offsetX`, and `offsetY` in `src/web-ui/src/components/commands/TapPanel.tsx` with `ParameterSourceControl`, with the same wiring and rules as T017 (including the `error` prop).
- [ ] T019 [P] [US1] Wrap `timeoutMs` in `src/web-ui/src/components/commands/WaitForImagePanel.tsx` with `ParameterSourceControl`, with the same wiring and rules as T017 (including the `error` prop).
- [ ] T020 [P] [US1] Wrap `instanceIndex` in `src/web-ui/src/components/commands/EnsureEmulatorRunningPanel.tsx` with `ParameterSourceControl`, with the same wiring and rules as T017 (including the `error` prop).

**Checkpoint**: US1 works alone. SC-001 holds (timed in T035).

---

## Phase C: User Story 2 - Use an image parameter in the image field (P1)

**Goal**: A person sets the image of a tap step (and of a WaitForImage step) to an image parameter.

**Independent Test**: Open a command with an image parameter `buttonToTap` and a tap step with an image target. Select the parameter in the image field. Save. The saved command has `primitiveTap.detectionTarget.referenceImageId` = `{{buttonToTap}}`. Remove it: the image picker returns.

- [ ] T021 [US2] FIRST TASK OF THIS PHASE. Check what the real service does with an empty inline image together with an image template (research R6, plan Phase C). Add a service integration test in `tests/integration/Commands/ParametrizedReferenceImageIntegrationTests.cs` (or a new file in the same folder). It saves a tap step with an empty inline image and `primitiveTap.detectionTarget.referenceImageId` = `{{buttonToTap}}`, and asserts the real behavior (accepted or refused). Write the result in a code comment in the test. Keep the result for the PR description (T036). All image UI tasks in this phase wait for this result.
- [ ] T022 [P] [US2] Write tests with the helper of T012 in `src/web-ui/src/components/commands/__tests__/TapPanel.imageParameter.test.tsx`: the image picker is hidden when a template is set; the chip shows the name; the confirm value has the template; remove restores the picker and the last chosen image; a `fieldErrors` entry for the image path shows at the image control. If T021 shows a refusal: with no stored image selected, the control shows the text that asks the person to select a stored image first.
- [ ] T023 [P] [US2] Write the same tests for the image field of `WaitForImagePanel` in `src/web-ui/src/components/commands/__tests__/WaitForImagePanel.imageParameter.test.tsx`.
- [ ] T024 [US2] Wrap the image picker of `src/web-ui/src/components/commands/TapPanel.tsx` (path `primitiveTap.detectionTarget.referenceImageId`) with `ParameterSourceControl`, with the same wiring as T017 (`parameters`, template, and `fieldErrors[path]` as `error`). Keep the last chosen image in local state. Treat a field with a template as valid. Use the result of T021: if the service accepts an empty inline image, the control may save it. If the service refuses, the control asks the person to select a stored image first, then the parameter (spec US2 scenario 4).
- [ ] T025 [P] [US2] Wrap the image picker of `src/web-ui/src/components/commands/WaitForImagePanel.tsx` with `ParameterSourceControl`, with the same rules and wiring as T024.

**Checkpoint**: US1 and US2 both work. SC-002 holds.

---

## Phase D: User Story 3 - Service error at the right step and field (P2)

**Goal**: The hints, the missing chip, the queue group, and the empty state come from T008 and are tested in T009. This phase adds only the mapping of a service error to the right step and field, and a page-level check of the US3 scenarios.

**Independent Test**: Open a command with a text parameter and a Swipe step. The text parameter shows the hint in the duration control. Select it and save. The UI shows `text_parameter_in_numeric_field` with the field and the parameter name, at the field of the right step.

### Tests for User Story 3

- [ ] T026 [P] [US3] Write unit tests in `src/web-ui/src/pages/__tests__/parameterErrorMapping.test.ts` for the mapping function of T028: parse `Step {order}:` from the message; read the path and parameter name from the issue details; a message with no prefix, a prefix with no matching step, or a path with no control gives a form-summary-only result.
- [ ] T027 [P] [US3] Write a page test in `src/web-ui/src/pages/__tests__/CommandsPage.parameterError.spec.tsx`: the service returns `text_parameter_in_numeric_field` for a command with two steps of the same type (two Swipe steps). Only the step that the message names shows the error at its field (`role="alert"`). The other step shows no error. The form summary shows the message, which names the field and the parameter (FR-009, SC-004). A message that names an unknown step shows in the form summary only.
### Implementation for User Story 3

- [ ] T028 [US3] Map the service error to the right step and field. Add the mapping function in `src/web-ui/src/pages/parameterErrorMapping.ts` (parse `Step {order}:` from the message, read path and parameter name from the details). In `src/web-ui/src/pages/CommandsPage.tsx` call it from `toSaveErrors`, keep the form summary, and pass the result by step order to `CommandForm`. In `src/web-ui/src/components/commands/CommandForm.tsx` pass `fieldErrors` of a step to the panel of that step only. Do not change the panel files (the panels already pass `fieldErrors` to each control in Phase B and C).
- [ ] T029 [US3] Write a page-level test in `src/web-ui/src/pages/__tests__/CommandsPage.parameterFields.spec.tsx` for the US3 scenarios with the real control: a text parameter shows the hint in the Swipe duration control and can be selected; a command with no parameters shows the empty state; a template for a deleted parameter shows "Parameter not found" and the save request is still sent.
- [ ] T030 [US3] Run the tests of T026, T027, and T029 and fix failures in `src/web-ui/src/pages/parameterErrorMapping.ts` and `src/web-ui/src/pages/CommandsPage.tsx`.

**Checkpoint**: All stories work. SC-004 holds.

---

## Phase E: Closing

- [ ] T031 [P] Update `docs/architecture.md` (web UI section) if it lists editor controls. Refresh "Last reviewed" (constitution Principle V).
- [ ] T032 [P] Add a CHANGELOG entry in `CHANGELOG.md` in STE.
- [ ] T033 [P] Set the `Status` of spec 169 to `Implemented` in `specs/169-command-editor-parameter-fields/spec.md` and update `specs/STATUS.md`.
- [ ] T034 Run the web UI tests, the type check, the lint, and the drift test (`FieldTemplatePathMirrorTests`). Fix all failures.
- [ ] T035 Run the manual steps in `specs/169-command-editor-parameter-fields/quickstart.md` in the browser. (a) Use a timer for the Swipe flow of Check 1 and record that it takes under 1 minute (SC-001). (b) Run the Check 1 and Check 5a Part B flows of feature 168 in the UI alone, with no `demo-data.ps1` (SC-003); confirm that `git diff origin/master -- specs/168-friendly-sequence-parameters` is empty. (c) Do the whole flow with the keyboard only, with no mouse (FR-011). (d) Check that the phone view still shows "Use a larger screen for this area." (FR-012).
- [ ] T036 Write the result of T021 (accepted or refused) in the PR description, with the text of the code comment in the test.
- [ ] T037 Bump the version in `installer/versioning/version.override.json` per CLAUDE.md before the push: run `git fetch origin master`, compare with `origin/master`, set `patch` (to `"0"` if `minor` or `major` changed, else `origin/master` patch plus 1), set `updatedBy` and `updatedAtUtc`. Do nothing if the file already has this value.

---

## Dependencies and Execution Order

- Phase A blocks all stories. In Phase A: T002 before T006 and T010. T003 before T004, T005, T008, T010. T008 before T009, T010, and T012.
- Phase B needs Phase A (T010 and T012). Phase C needs Phase A. T021 runs first in Phase C and blocks T022 to T025.
- Phases B and C touch the same files (`TapPanel.tsx`, `WaitForImagePanel.tsx`), so do Phase B first, then Phase C.
- Phase D needs T010, and the panels of Phase B and C for the end-to-end behavior. T028 comes after T026 and T027. T029 needs T008 and T017.
- Phase E needs all stories. T036 needs T021. T037 is last.

## Parallel Opportunities

- Phase A: T003 then T004 and T005 together; T007, T009, and T011 together.
- Phase B: T013 to T016 together; T018, T019, T020 together after T017.
- Phase C: T022 and T023 together after T021; T025 parallel to T024 (different files).
- Phase D: T026 and T027 together.
- Phase E: T031, T032, T033 together.

## Implementation Strategy

- MVP: Phases A and B (US1). Stop and check the Swipe duration flow.
- Then Phase C (image, T021 first), then Phase D (error mapping), then Phase E.
- Commit after each phase. Do not push before T037.
