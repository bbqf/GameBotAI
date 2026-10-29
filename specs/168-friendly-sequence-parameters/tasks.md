---

description: "Task list for feature 168: friendly sequence parameters"
---

# Tasks: Friendly Sequence Parameters

**Input**: Design documents from `/specs/168-friendly-sequence-parameters/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/parameter-declaration.md, quickstart.md

**Tests**: Included. The constitution (Principle II) requires unit and contract tests for new logic and a coverage of 80% line and 70% branch for touched files.

**Organization**: Tasks are grouped by user story. Each story can be built and tested alone after Phase 2.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: The task can run in parallel (different files, no open dependency).
- **[Story]**: The user story of the task (US1 to US5).
- All paths are relative to `C:\src\GameBot`. Use absolute paths in PowerShell commands.
- Method names use CamelCase with no underscores (constitution).
- All new text (comments, messages, docs) follows STE (constitution, Principle VI). Each user-facing message names the problem and one way to fix it (Principle III).

## Path Conventions

- Domain: `src/GameBot.Domain/`
- Service: `src/GameBot.Service/`
- Web UI: `src/web-ui/src/`
- Tests: `tests/unit/Parameters/`, `tests/contract/`, `tests/integration/`, Jest tests in `__tests__` folders next to the UI code

---

## Phase 1: Setup

**Purpose**: Confirm a clean baseline before any change.

- [X] T001 Run `dotnet test C:\src\GameBot\GameBot.sln`, and `npm test` and `npm run lint` in `C:\src\GameBot\src\web-ui`. Write the result in the PR notes. Do not continue if a test fails (constitution release blocker).
- [X] T002 [P] Write a baseline regression test in `tests/integration/Queues/PreFeatureParametersRegressionTests.cs` (SC-005, FR-018): a command and a sequence JSON as saved before this feature (declarations with name, type, default, required, description only) load, and a queue run gives the same resolved values as before. Add a test for FR-019: a required choice or image parameter with no value and no default gives a save warning and a refused queue start. Run it now; it must pass.

---

## Phase 2: Foundational (blocks all user stories)

**Purpose**: The shared model, wire format, value rules, and UI shell. No story can start before this phase ends.

### Domain and wire format

- [X] T003 [P] Create the enum `ParameterInputKind` (Text, Number, Choice, Image) in `src/GameBot.Domain/Parameters/ParameterInputKind.cs`, with XML docs.
- [X] T004 [P] Create the type `ParameterOption` (`Label`, `Value`) in `src/GameBot.Domain/Parameters/ParameterOption.cs`, with XML docs.
- [X] T005 Add the optional members `Label`, `HelpText`, `Kind`, `Options`, `AllowedImages`, `Min`, `Max`, `Unit`, `Pattern` to `src/GameBot.Domain/Parameters/ParameterDeclaration.cs` (data-model.md). Keep every member optional and null-safe. Depends on T003, T004.
- [X] T006 Add the members and `ParameterOptionDto` to `ParameterDeclarationDto` in `src/GameBot.Service/Models/Commands.cs`. Omit empty members from responses.
- [X] T007 Map the new members in both directions in `src/GameBot.Service/Endpoints/ParameterDtoMapper.cs` (`ToDomainDeclarations`, `ToResponseDeclarations`). Treat an unknown `kind` text as an error value that the rules (T010) report. Depends on T005, T006.
- [X] T008 [P] Write a round-trip test in `tests/unit/Parameters/ParameterDeclarationFriendlyFieldsTests.cs`: a declaration with all new members is saved and loaded with the same values, and a declaration with none of them loads as before. Check the JSON persistence of `src/GameBot.Domain/Commands/FileSequenceRepository.cs` and the command repository. Fix them if they drop a member.

### Rules

- [X] T009 Write failing tests for the declaration rules (data-model.md, "Validation rules (declaration)") in `tests/unit/Parameters/ParameterNameRulesFriendlyTests.cs`. Cover: member not valid for the kind, option count 1 to 50, duplicate option label (no case) and value, option value type, `min` above `max`, `pattern` marker count, `pattern` on a non-number, empty `allowedImages`, more than 50 images, duplicate images, default not fitting the kind, label and help text length, `{{` allowed inside longer text in an option value and in a pattern, an option value that is exactly one placeholder refused (FR-027), `kind` = text with `type` = number refused, and that each message has a problem and a hint.
- [X] T010 Implement the rules of T009 in `src/GameBot.Domain/Parameters/ParameterNameRules.cs` (`ValidateDeclarations`). Keep the old rules and error texts. Map the codes in `AddDeclarationIssues` of `src/GameBot.Domain/Services/ParameterValidationService.cs`. Depends on T005, T009.
- [X] T011 [P] Write failing tests for the value rules in `tests/unit/Parameters/ParameterValueRulesTests.cs`: whole number, range, option membership, allowed image membership, a `{{placeholder}}` value that is not checked (FR-028), a null value (inherit) that is not checked, and the message text (problem and hint).
- [X] T012 Create `src/GameBot.Domain/Parameters/ParameterValueRules.cs` with `Check(declaration, value)` that returns a message with a hint, or null. Depends on T005, T011.
- [X] T013 Check binding values against declarations at save and return the **warning** `invalid_parameter_value` (contracts/parameter-declaration.md, FR-012): (a) the queue-template entry save in `src/GameBot.Service/Endpoints/QueueTemplatesEndpoints.cs` (use `ValidateTemplateEntry` or the equivalent check path, with the sequence and its reachable command declarations; read the save path first, because `ValidateTemplateEntry` has no endpoint caller today); (b) the sequence step bindings in `ParameterValidationService.ValidateSequence`; (c) the command step bindings (a command step that calls another command) in `ParameterValidationService.ValidateCommand`. Add a `commandLookup` parameter to `ValidateCommand`, like the one in `ValidateSequence`, and update both callers in `src/GameBot.Service/Endpoints/CommandsEndpoints.cs` (the create and the update path). Include the check that a stored image ID exists (use the image repository like the feature 114 check). The save works with the warning. Add tests in `tests/unit/Parameters/ParameterValidationFriendlyValueTests.cs` and `tests/contract/QueueTemplates/FriendlyValueContractTests.cs`. Depends on T012.
- [X] T014 [P] Write failing tests in `tests/unit/Parameters/ParameterTextInNumericFieldTests.cs`: a text-typed parameter (a text choice, or a number with a pattern) in a numeric field of a command step gives the error `text_parameter_in_numeric_field` (FR-026). The message names the field and the parameter and gives a hint. A number parameter without a pattern, and a number choice, are accepted.
- [X] T015 Implement the check of T014 in `ParameterValidationService.ValidateCommand` (`src/GameBot.Domain/Services/ParameterValidationService.cs`). Use the numeric field list of `CommandStepFieldPaths` and the references from `ParameterReferenceScanner`. Return the error from the command save in `src/GameBot.Service/Endpoints/CommandsEndpoints.cs`. Depends on T010, T014.

### UI shell

- [X] T016 [P] Add the new types and helpers to `src/web-ui/src/components/parameters/types.ts`: `ParameterInputKind`, `ParameterOption`, the new declaration members, `effectiveKind(declaration)`, and `displayLabel(declaration)`.
- [X] T017 [P] Create `src/web-ui/src/components/parameters/parameterValueRules.ts` with the same checks and message texts as T012. The service stays the authority. Add `src/web-ui/src/components/parameters/__tests__/parameterValueRules.test.ts`.
- [X] T018 Create the field dispatcher `src/web-ui/src/components/parameters/fields/ParameterValueField.tsx`. It picks the control by `effectiveKind`. For text and number it renders the controls that `ParameterBindingForm.tsx` uses today. Use it in `ParameterBindingForm.tsx` for the value input, and keep the Inherit control, the effective-value preview, and the ad-hoc rows unchanged. Show the label, the help text, and the inline message from T017. Do not show the technical name for a declaration that has a label. Do not accept a new invalid value. Depends on T016, T017.
- [X] T019 Create `src/web-ui/src/components/parameters/ParameterPassThrough.tsx` (FR-028): the control "Use a parameter of this sequence" with a list of the sequence parameters of the same type. It stores `{{name}}`. Add a prop to `ParameterBindingForm.tsx` for the sequence declarations, and show the control only at a step form. Add `__tests__/ParameterPassThrough.test.tsx`: the list has only parameters of the same type; the stored value is `{{name}}`; no range or list check applies to it; the control is keyboard-operable and named. Depends on T018.
- [X] T020 Create `src/web-ui/src/components/parameters/ParameterDeclarationCard.tsx`: one card with label, help text, name, type, kind, default, required, and a slot for the settings of the kind. Use it in `ParameterDeclarationList.tsx` in place of the plain row. Keep the move and remove buttons and the existing name checks. Update `src/web-ui/src/components/parameters/__tests__/ParameterDeclarationList.test.tsx` for the new markup. Depends on T016.
- [X] T021 Fix every caller and test that T018 to T020 break: `src/web-ui/src/components/commands/CommandForm.tsx`, `src/web-ui/src/pages/SequencesPage.tsx`, `src/web-ui/src/components/queues/SchedulingSequenceCard.tsx`, `src/web-ui/src/pages/Execution.tsx`, `src/web-ui/src/components/queues/QueueSchedulingAreas.tsx`. Pass the sequence declarations to the step form (T019). Depends on T018, T019, T020.
- [X] T022 Use the shared value form in `src/web-ui/src/components/stepthrough/ValuesForm.tsx` (FR-022). Update `src/web-ui/src/components/stepthrough/__tests__/` tests. Depends on T018.
- [X] T023 [P] Add the styles for the card, the field components, and the form in `src/web-ui/src/styles.css`. The current parameter classes have no styles. Use the existing theme tokens in `src/web-ui/src/theme`.

**Checkpoint**: A declaration with the new members saves, loads, and validates. The form and the card render the old kinds as before. Run `dotnet test` and `npm test`. All tests pass, including T002.

---

## Phase 3: User Story 1 - Pick an image from a list (Priority: P1) 🎯 MVP

**Goal**: An author lists allowed images. A person selects one by its preview. The sequence gets the image ID.

**Independent Test**: quickstart.md, Check 1.

### Tests for User Story 1

- [X] T024 [P] [US1] Write a contract test in `tests/contract/Sequences/FriendlyImageParameterContractTests.cs`: a sequence with an image parameter saves and loads; an empty `allowedImages` is refused; a missing image ID gives the warning `unknown_allowed_image`; an entry value outside `allowedImages` gives the warning `invalid_parameter_value` and the save works.
- [X] T025 [P] [US1] Write an integration test in `tests/integration/Queues/FriendlyImageParameterRunTests.cs`: a queue entry that selects an allowed image runs, and the step gets the image ID. A deleted allowed image makes the step fail at run time and does not refuse the run start (FR-016).
- [X] T026 [P] [US1] Write Jest tests in `src/web-ui/src/components/parameters/__tests__/ImagePickerField.test.tsx` and `AllowedImagesEditor.test.tsx`: only allowed images show; selection works with the keyboard; each tile has a text name; the search box shows for more than 12 images; "image not found" shows for a removed image; the author dialog groups images by owner.

### Implementation for User Story 1

- [X] T027 [US1] Add the warning `unknown_allowed_image` to `ParameterValidationCodes` in `src/GameBot.Domain/Services/ParameterValidationService.cs`. Add the image existence check for `allowedImages` in the command save (`src/GameBot.Service/Endpoints/CommandsEndpoints.cs`) and the sequence save (`src/GameBot.Service/Endpoints/SequencesEndpoints.cs`). Return the warning in the response.
- [X] T028 [US1] Make the gamepack install (feature 143) handle the new declaration members in `src/GameBot.Domain/Gamepacks/`: (a) `GamepackReferences.cs` maps each ID in `allowedImages` when it maps image IDs; (b) `GamepackValidator.cs` checks each ID in `allowedImages` as an image reference; (c) install, export, and backup restore keep all new declaration members (round trip). Add tests in `tests/unit/Gamepacks/`. Change the code only where a test fails.
- [X] T029 [P] [US1] In `src/web-ui/src/services/images.ts`, add `listImageItems()` that returns `{ id, owner }` from `GET /api/images` (the `items` member). Keep `listImages()` as it is.
- [X] T030 [P] [US1] Create `src/web-ui/src/components/parameters/useImageThumbnail.ts`: load a thumbnail with `getImageBlob` when the tile scrolls into view; cache the blob URL; release it on unmount.
- [X] T031 [US1] Create `src/web-ui/src/components/parameters/fields/ImagePickerField.tsx`: a grid of the allowed images with preview and name, a large preview of the selection, search for more than 12 images, the "image not found" state with a hint, and keyboard use (arrow keys, Enter). Register it in `ParameterValueField.tsx`. Depends on T018, T030.
- [X] T032 [US1] Create `src/web-ui/src/components/parameters/AllowedImagesEditor.tsx`: the list of allowed images with thumbnails and remove buttons, and a "Choose images" dialog that lists all stored images grouped by owner, with search and check boxes (1 to 50). Show it in the image settings slot of `ParameterDeclarationCard.tsx`. Reuse `SearchableDropdown.tsx` or `components/images/ImageSelectorDropdown.tsx` code where it fits. Depends on T020, T029, T030.

**Checkpoint**: Story 1 works alone. An image parameter can be declared, selected, saved, and run. Run `dotnet test` and `npm test`. All tests pass.

---

## Phase 4: User Story 2 - Pick a readable option (Priority: P1)

**Goal**: An author lists options with labels and technical values. A person sees the labels. The sequence gets the technical value. The log shows both.

**Independent Test**: quickstart.md, Check 2 and Check 5a.

### Tests for User Story 2

- [X] T033 [P] [US2] Write a contract test in `tests/contract/Sequences/FriendlyChoiceParameterContractTests.cs`: save and load of options (1 option is valid); duplicate label refused; a number list with a non-number value refused; more than 50 options refused; an entry value that is not an option value gives the warning `invalid_parameter_value`.
- [X] T034 [P] [US2] Write an integration test in `tests/integration/Queues/FriendlyChoiceParameterRunTests.cs`: a number choice fills a numeric field; the execution log entry has the value and the option `label`; a masked name has no label (FR-017); an option value with `{{` inside longer text reaches the step unchanged and is not resolved (FR-027); a step binding `{{seqParam}}` passes the sequence value to the command choice parameter (FR-028).
- [X] T035 [P] [US2] Write Jest tests in `src/web-ui/src/components/parameters/__tests__/ChoiceField.test.tsx` and `OptionsEditor.test.tsx`: labels only; buttons for 6 options or fewer, drop-down for more; search for more than 12; "option no longer exists" state; keyboard use; the editor refuses duplicate labels with a clear message and a hint; one option is selected by default.

### Implementation for User Story 2

- [X] T036 [US2] Add an optional `Label` member to `ResolvedParameter` in `src/GameBot.Domain/Parameters/CommandStepResolver.cs`. Add a public `TryFindDeclaration` method to `src/GameBot.Domain/Parameters/ParameterScope.cs`. In `AddUsage`, set the label to the option label when the declaration is a choice with a matching option. Skip the label for names that match the secret-name rule.
- [X] T037 [US2] Carry `label` into the execution log entry (`src/GameBot.Service/Services/ICommandExecutor.cs` and the log model). Show it in the execution logs page (search `originLayer` in `src/web-ui/src/` to find the place), next to the technical value. Depends on T036.
- [X] T038 [US2] Make `TryBindChild` in `src/GameBot.Domain/Parameters/ParameterScope.cs` treat a binding value that equals an option value of the callee declaration as plain text (FR-027). Pass the callee declarations to `TryBindChild` from its callers. Keep the behavior for all other values (feature 115). Depends on T036.
- [X] T039 [P] [US2] Create `src/web-ui/src/components/parameters/fields/ChoiceField.tsx` (buttons or drop-down, search, "option no longer exists" state, the pass-through control from T019 at a step form). Register it in `ParameterValueField.tsx`. Use `src/web-ui/src/components/SearchableDropdown.tsx` for the long list. Depends on T018, T019.
- [X] T040 [US2] Create `src/web-ui/src/components/parameters/OptionsEditor.tsx`: add, remove, and move options; label and value fields; the value type follows the parameter type; the 50 limit; duplicate checks. Show it in the choice settings slot of `ParameterDeclarationCard.tsx`. Depends on T020.

**Checkpoint**: Stories 1 and 2 work alone and together. Run `dotnet test` and `npm test`. All tests pass.

---

## Phase 5: User Story 3 - Number with a technical pattern (Priority: P2)

**Goal**: A person enters a number. The sequence gets the number inside the technical pattern.

**Independent Test**: quickstart.md, Check 3.

### Tests for User Story 3

- [X] T041 [P] [US3] Write unit tests in `tests/unit/Parameters/ParameterScopePatternTests.cs`: the pattern applies to a binding value and to a default; a value marked `Mapped` is not mapped again (whole-placeholder binding, feature 115); a name with no pattern is unchanged; a pattern parameter inside a loop; a pattern with `{{` is not resolved again; `ToSubstitutionContext` and `Describe` show the mapped value.
- [X] T042 [P] [US3] Write a contract test in `tests/contract/Sequences/FriendlyNumberParameterContractTests.cs`: a range mismatch gives the warning `invalid_parameter_value`; a pattern with no or two `{value}` markers is refused at declaration save.
- [X] T043 [P] [US3] Write an integration test in `tests/integration/Queues/FriendlyNumberParameterRunTests.cs`: the number 5 and the pattern `emulator-{value}` give `emulator-5` in a text field of a command step.
- [X] T044 [P] [US3] Write Jest tests in `src/web-ui/src/components/parameters/__tests__/NumberField.test.tsx`: unit label, range hint, inline range message with a hint, no acceptance of a new value out of range, a stored value that is out of range (changed range) shown with a message and a hint and not changed (FR-013), and a screen reader name.
- [X] T045 [P] [US3] Write two tests in `tests/unit/Parameters/ParameterScopeResolvePerfTests.cs`. (a) A benchmark with `[Trait("Category", "Benchmark")]` for `TryResolve` with and without a pattern. The test checks the absolute limit of SC-008: less than 1 µs for one name. Check that the CI test filter skips this trait, and add the filter if it does not. Run it by hand and save the numbers for the perf note; the limit is 1 µs (SC-008). (b) A deterministic test that counts the declaration lookups and string allocations of `TryResolve` for a name without a pattern (no extra work compared with before).

### Implementation for User Story 3

- [X] T046 [US3] Add `Mapped` (default false) to `ParameterValue` in `src/GameBot.Domain/Parameters/ParameterScope.cs`. In `TryResolve`, find the innermost declaration of the name (`TryFindDeclaration`, T036). If it has a `Pattern` and the value is not `Mapped`, put the value in the `{value}` marker and return the result with `Mapped` = true. Keep `TryBindChild` correct for whole-placeholder and mixed values (a mixed value is a new text, so mark it `Mapped`). Depends on T036, T041.
- [X] T047 [P] [US3] Create `src/web-ui/src/components/parameters/fields/NumberField.tsx` (include the stale out-of-range state). Register it in `ParameterValueField.tsx`. Depends on T018.
- [X] T048 [US3] Create `src/web-ui/src/components/parameters/NumberSettings.tsx`: min, max, unit, and pattern fields; a live line "Technical result: emulator-5" for the author only. Show it in the number settings slot of `ParameterDeclarationCard.tsx`. Depends on T020.

**Checkpoint**: Stories 1 to 3 work alone and together. Run `dotnet test` and `npm test`. All tests pass.

---

## Phase 6: User Story 4 - A clearer authoring editor (Priority: P2)

**Goal**: The editor shows a live preview of the person's form and suggests names.

**Independent Test**: quickstart.md, Check 4.

### Tests for User Story 4

- [X] T049 [P] [US4] Write Jest tests in `src/web-ui/src/components/parameters/__tests__/ParameterDeclarationCard.test.tsx` and `suggestParameterName.test.ts`: the name is suggested from the label when the name is empty; the suggestion is a valid identifier and unique; the author can change it; only the settings of the chosen kind show; the preview matches the value form; moving a card changes the preview order.

### Implementation for User Story 4

- [X] T050 [P] [US4] Create `src/web-ui/src/components/parameters/suggestParameterName.ts` (label to camelCase identifier, avoid reserved names and duplicates, use `validateParameterName`).
- [X] T051 [US4] Add the name suggestion to `ParameterDeclarationCard.tsx`. Stop suggesting once the author edits the name. Depends on T050.
- [X] T052 [US4] Create `src/web-ui/src/components/parameters/ParameterFormPreview.tsx`. It renders the declarations with `ParameterBindingForm` in preview mode (no save, no Inherit state, defaults shown). Show it beside the cards in `ParameterDeclarationList.tsx`. Depends on T018, T020.

**Checkpoint**: Stories 1 to 4 work alone and together. Run `dotnet test` and `npm test`. All tests pass.

---

## Phase 7: User Story 5 - Value form at phone width (Priority: P3)

**Goal**: The value form works at 375 px. The editor shows a notice on a phone. No phone screen is added.

**Independent Test**: quickstart.md, Check 5.

- [X] T053 [P] [US5] Write Jest tests in `src/web-ui/src/components/parameters/__tests__/ParameterFormPhone.test.tsx` (mock `useLayoutMode` to `phone`): all four kinds render; controls have a touch size class; the declaration editor shows "Use a larger screen" and no card controls.
- [X] T054 [US5] Add the phone rules in `src/web-ui/src/styles.css`: one column, touch-size controls (at least 44 px), no horizontal scroll at 375 px for the image grid, the button group, and the number field. Follow `docs/mobile-ui-principles.md`.
- [X] T055 [US5] In `ParameterDeclarationList.tsx`, use `useLayoutMode()` to show "Use a larger screen" in place of the editor on a phone. Depends on T054.

**Checkpoint**: All five stories work. Run `dotnet test` and `npm test`. All tests pass.

---

## Phase 8: Polish and cross-cutting

- [X] T056 [P] Describe every new member in the OpenAPI document (FR-023): `src/GameBot.Service/Swagger/SwaggerConfig.cs` and the model XML docs. Add a test `tests/contract/Sequences/FriendlyParametersOpenApiTests.cs` like `CommandParametersOpenApiTests.cs`.
- [X] T057 [P] Add an accessibility check for the four field types and the pass-through control (FR-025) in the Jest tests: role, accessible name, and keyboard use.
- [X] T058 [P] Update `docs/architecture.md` (domain model, API surface, execution log, and "Last reviewed"). Update `docs/mobile-ui-principles.md` (the value form at phone width; the editor is desktop-only). Update `docs/gamepacks.md` if T028 changed the rules.
- [X] T059 [P] Add a CHANGELOG entry in `CHANGELOG.md` (user-visible change; mention the version bump).
- [X] T060 Set the status of `specs/168-friendly-sequence-parameters/spec.md` to Implemented. Set the status of `specs/078-sequence-parameters/spec.md` to "Implemented (iterated by 168)". Use `scripts/set-spec-status.ps1`. Keep `specs/STATUS.md` consistent.
- [X] T061 Write the perf note for the PR text (constitution, Principle IV): the benchmark numbers from T045 for `TryResolve`, with and without a pattern.
- [X] T062 Check all new and changed text for STE (comments, messages, docs, commit and PR text) and that each user-facing message has a hint (Principle III).
- [X] T063 Run `dotnet test C:\src\GameBot\GameBot.sln`, `npm test`, `npm run lint`, and `npm run build` in `C:\src\GameBot\src\web-ui`. Check the coverage of touched files (80% line, 70% branch). Fix every failure.
- [ ] T064 Run the checks in `quickstart.md` by hand against a running service, including the timed checks for SC-001, SC-002, and SC-004. Record the result in the PR notes. SC-003 and SC-006 are post-release measures.
- [X] T065 Before the push for the PR, bump `patch` in `installer/versioning/version.override.json` as CLAUDE.md says: run `git fetch origin master`, compare with `origin/master`, set `patch` to the master `patch` plus 1 (or 0 if `minor` or `major` changed), and set `updatedBy` and `updatedAtUtc`.

---

## Dependencies and execution order

### Phase dependencies

- **Phase 1** has no dependency.
- **Phase 2** depends on Phase 1 and blocks all stories.
- **Phases 3 to 7** depend on Phase 2. They can run in parallel, or in this order: US1, US2, US3, US4, US5.
- **Phase 8** depends on the stories you want to ship.

### Task dependencies inside Phase 2

- T005 needs T003, T004. T007 needs T005, T006. T010 needs T005, T009. T012 needs T005, T011. T013 needs T012. T015 needs T010, T014.
- T018 needs T016, T017. T019 needs T018. T020 needs T016. T021 needs T018, T019, T020. T022 needs T018.

### Story dependencies

- US1, US2, US3 need only Phase 2. Each adds one field component and one settings component.
- US3 task T046 needs T036 (in US2). Do T036 first, or move it with T046 if you ship US3 before US2.
- US4 works with any settings component that exists.
- US5 needs T018, T020, and the field components of the stories you ship.
- Shared files: `ParameterValueField.tsx` (T031, T039, T047) and `ParameterDeclarationCard.tsx` (T032, T040, T048, T051). `ParameterScope.cs` (T036, T038, T046). Do not run the tasks of one file at the same time.

### Parallel opportunities

- Phase 2: T003 and T004; T008, T011, T014, T016, T017, T023.
- Each story: all test tasks are parallel. Service work and UI work of one story are parallel.
- After Phase 2, one person can take US1 and another US2, if they agree on the shared files above.

## Parallel example: User Story 1

```text
Task: "Contract test in tests/contract/Sequences/FriendlyImageParameterContractTests.cs"   (T024)
Task: "Integration test in tests/integration/Queues/FriendlyImageParameterRunTests.cs"     (T025)
Task: "Jest tests for ImagePickerField and AllowedImagesEditor"                           (T026)
Task: "listImageItems in src/web-ui/src/services/images.ts"                               (T029)
Task: "useImageThumbnail in src/web-ui/src/components/parameters/useImageThumbnail.ts"    (T030)
```

## Implementation strategy

### MVP first (User Story 1)

1. Phase 1 and Phase 2.
2. Phase 3 (US1).
3. Stop and check quickstart Check 1. Check that old data still runs (Check 6 and T002).

### Incremental delivery

1. Add US2 (choice, log label, pass-through). Check.
2. Add US3 (number pattern). This is the only story that changes the name resolution (T046). Run the benchmark (T045) and the full test suite after it.
3. Add US4 and US5.
4. Do Phase 8. Bump the version last (T065).

## Notes

- Write each test task first. Make sure it fails. Then write the code.
- The service is the authority for every rule. The UI check is for speed only (T017).
- A run never refuses to start because of a stale value (FR-016). Do not add a run-start check.
- A value that does not match its declaration gives a warning at save, not an error (FR-012).
- Run the tests of the touched area before each commit, and read the result (constitution, Principle II). Commit after each task or small group of tasks. Do not commit with a failing test.
