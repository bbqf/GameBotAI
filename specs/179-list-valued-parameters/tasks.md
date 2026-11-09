# Tasks: List-valued sequence parameters

**Input**: Design documents in `/specs/179-list-valued-parameters/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/parameter-list-contract.md

Format: `- [ ] T### [P?] [US?] Description with file path`. Tests are required (spec FR-017).

## Phase 1: Setup

- [ ] T001 Read `src/GameBot.Domain/Parameters/ParameterDeclaration.cs`, `ParameterBinding.cs`, `ParameterNameRules.cs`, and `ParameterScope.cs` to confirm the extension points in plan.md.

## Phase 2: Foundational (blocks all stories)

- [ ] T002 Add `Multiple` (bool), `MinItems` (int?), `MaxItems` (int?) to `src/GameBot.Domain/Parameters/ParameterDeclaration.cs`, omitted from JSON when null or false. Add `Values` (`IReadOnlyList<string>?`) to `src/GameBot.Domain/Parameters/ParameterBinding.cs`, omitted when null.
- [ ] T003 Create `src/GameBot.Domain/Parameters/ParameterListRules.cs` with: `Separator` constant `|`, `Normalize(IEnumerable<string>)` (unique, first occurrence, drop empty), `Join`, `Split`, `ReadAsList(declaration, binding)`, `Count(text)`, and `CheckLimits(declaration, count)`. Add XML docs.
- [ ] T004 [P] Add unit tests for `ParameterListRules` in `tests/unit/Parameters/ParameterListRulesTests.cs` (normalize order and duplicates, join, split, scalar read as list of one, limits).
- [ ] T005 Add `Multiple`, `MinItems`, `MaxItems` to `ParameterDeclarationDto` and `Values` to `ParameterBindingDto` in `src/GameBot.Service/Models/Commands.cs`, with XML docs in STE. Omit null members from JSON.
- [ ] T006 Map the new members both ways in `src/GameBot.Service/Endpoints/ParameterDtoMapper.cs`: declarations (`ToDomainDeclarations`, `ToResponseDeclarations`) and bindings (`ToDomainBindings` normalizes `values` and sets `Value` to the joined text; `ToResponseBindings` returns `values` and `value`). When both `value` and `values` come in, `values` wins. An empty `values` list sets `Value` to null, not the empty text. Add a test for this in `tests/contract/QueueTemplates/ParameterListEntryContractTests.cs` (T012).
- [ ] T007 Add `Values` through the entry DTOs. `TemplateEntrySaveRequest.cs` (line 54) and `QueueTemplateDetailResponse.cs` (line 62) already use `ParameterBindingDto`, so the new member comes with T005. For `src/GameBot.Service/Models/SequenceStepContracts.cs` (lines 89 and 106), check that the mapping passes `Values`. Check that the stores persist `Values` with the domain class. T001 gives the facts for this check.

**Checkpoint**: the new members round-trip through the domain and the DTOs.

## Phase 3: User Story 1 - Declare a multi-value parameter (P1)

**Goal**: The API accepts, validates, and returns the new declaration members.  
**Independent test**: Save and read a sequence with a multi-value image parameter; save invalid declarations and get errors.

- [ ] T008 [P] [US1] Add declaration tests in `tests/unit/Parameters/ParameterNameRulesMultipleTests.cs`: `multiple` on `text` and `number` refused; limits below 1; `minItems` > `maxItems`; `minItems` > allowed count; `minItems` or `maxItems` without `multiple`; `|` in an allowed image id or option value; a default with several items that are all allowed is accepted; a default with a not-allowed item or a count outside the limits is refused.
- [ ] T009 [US1] Implement the declaration rules in `src/GameBot.Domain/Parameters/ParameterNameRules.cs` (`ValidateDeclarations`) with actionable messages and the code `invalid_parameter_declaration`. Include the default rule: each item of a joined default is allowed, and the count is inside the limits.
- [ ] T010 [P] [US1] Add contract tests in `tests/contract/Sequences/ParameterListContractTests.cs` and `tests/contract/Commands/ParameterListContractTests.cs`: a declaration with `multiple` round-trips for kind `image` and kind `choice`; invalid declarations return 400; a declaration without the flag gives the same response as before.
- [ ] T011 [US1] Add the new members to the published schema. Swashbuckle reads the DTO properties, so confirm the members appear. Add descriptions in `src/GameBot.Service/Swagger/SwaggerConfig.cs` or a schema filter in the same folder (see `ParametrizedReferenceImageSchemaFilter.cs` for the pattern) if the DTO XML docs do not show. Add a Swagger test in `tests/contract/Swagger/` (use the existing Swagger contract test file) that asserts `multiple`, `minItems`, `maxItems`, and `values` exist.

## Phase 4: User Story 3 - Store and read list values (P1)

**Goal**: Lists persist in every layer; old scalars read as a list of one.  
**Independent test**: Save a template entry with a list; load an old scalar value and read a list of one.

- [ ] T012 [P] [US3] Add tests in `tests/contract/QueueTemplates/ParameterListEntryContractTests.cs`: entry with `values` round-trips in order with duplicates removed; entry with scalar `value` for a multi-value parameter is returned and read as a list of one; a queue built from the template holds the same list.
- [ ] T013 [P] [US3] Add a store test in `tests/integration/Queues/ParameterListPersistenceTests.cs`: an old JSON file without `values` loads; a new file with `values` loads and saves.
- [ ] T014 [US3] Make the endpoints keep `Values` through save, copy, and queue build: `src/GameBot.Service/Endpoints/QueueTemplatesEndpoints.cs`, `QueuesEndpoints.cs`, `SequencesEndpoints.cs`. Fix any place that copies a binding by `Name` and `Value` only.

## Phase 5: User Story 2 - Select several items in the value form (P1)

**Goal**: The operator selects one or more icons or options.  
**Independent test**: Select two icons, save, open again, see both in order.

- [ ] T015 [P] [US2] Add web UI tests: multi-select mode in `src/web-ui/src/components/parameters/__tests__/ImagePickerField.test.tsx` and `ChoiceField.test.tsx` (select several, order kept, duplicate not possible, deselect), `ParameterBindingForm.test.tsx` (writes `values`, reads a scalar as a list of one), `ParameterDeclarationCard.test.tsx` (flag and limits only for image and choice), and a phone check in `ParameterFormPhone.test.tsx`.
- [ ] T016 [US2] Extend `src/web-ui/src/components/parameters/types.ts`: `multiple`, `minItems`, `maxItems` on the declaration; `values` on the binding; helper `readValuesAsList`.
- [ ] T017 [US2] Add a multi-select mode to `fields/ImagePickerField.tsx` and `fields/ChoiceField.tsx`. Use toggle buttons with a touch target as in the single mode. Show the limits as help text and mark the selected items.
- [ ] T018 [US2] Route by `declaration.multiple` in `fields/ParameterValueField.tsx`, and make `ParameterBindingForm.tsx` read and write `binding.values` (a scalar `value` reads as a list of one). Keep "Use default" behavior.
- [ ] T019 [US2] Add the author controls in `ParameterDeclarationCard.tsx`: a "Select several" box for kind `image` and `choice`, and fields for the minimum and the maximum. Hide them for other kinds.
- [ ] T020 [US2] Check that API client types and the queue entry editor pass `values`: search `src/web-ui/src` for `parameterValues` and `ParameterBinding` and update the services and pages that copy bindings.

## Phase 6: User Story 4 - Validate list values (P2)

**Goal**: Wrong lists are refused or warned as the spec says.  
**Independent test**: Empty required list refused; wrong item warns; limits refuse.

- [ ] T021 [P] [US4] Add tests in `tests/unit/Parameters/ParameterValidationListTests.cs`: empty required list is an error `empty_parameter_list`; list below `minItems` or above `maxItems` is an error `parameter_list_out_of_limits`; item not allowed gives the existing `invalid_parameter_value` warning for each wrong item; a list of 2+ items for a non-multi-value parameter warns; `FindUnsatisfiedRequired` treats an empty list as no value.
- [ ] T022 [US4] Extend `src/GameBot.Domain/Parameters/ParameterValueRules.cs` with a list overload that checks each item with the single-value check and the limits.
- [ ] T023 [US4] Extend `src/GameBot.Domain/Services/ParameterValidationService.cs`: add codes `EmptyList` and `ListOutOfLimits` to `ParameterValidationCodes`; use the list overload in `AddBindingValueWarnings`; make `ValidateTemplateEntry` and `FindUnsatisfiedRequired` treat an empty `Values` list as no value; add `FindImageValueCandidates` support for each item of a list; call the image-exists check for each list item of an image parameter.
- [ ] T024 [US4] Make the endpoints return the new errors with the standard error body: `QueueTemplatesEndpoints.cs`, `QueuesEndpoints.cs`, `SequencesEndpoints.cs`. Add contract tests in `tests/contract/QueueTemplates/ParameterListEntryContractTests.cs` for the 400 responses.

## Phase 7: User Story 5 - Resolve list values (P2)

**Goal**: Bindings and `{{name}}` resolve a list in the defined way.  
**Independent test**: Joined text in a text field; failure for 2+ items in a one-value field.

- [ ] T025 [P] [US5] Add tests in `tests/unit/Parameters/ParameterScopeListTests.cs` and `ParameterResolutionListTests.cs`: joined text in a text field and in `ocrText.pattern`; one item in a one-value field resolves; two items in a one-value field fail with a message that names parameter, field, and count; pass-through binding between two multi-value parameters keeps the full list; a multi-value default reads as a list; a list bound to a non-multi-value parameter.
- [ ] T026 [US5] Add the reason `ListInSingleField` to `src/GameBot.Domain/Parameters/ParameterResolutionError.cs`. In `CommandStepResolver.cs`, for a whole-placeholder value that goes to an image-id field or a numeric field, find the declaration with `ParameterScope.TryFindDeclaration`; when it is multi-value and the text splits into 2 or more items, return the error.
- [ ] T027 [US5] Map the new reason to the step failure message in the places that print resolution errors: search `ParameterResolutionReasons` usages in `src/GameBot.Service/Services/CommandExecutor.cs`, `SequenceExecution/SequenceExecutionService.cs`, and `src/GameBot.Domain/Services/SequenceRunner.cs`. The message names the parameter, the field, and the count.
- [ ] T028 [US5] Keep `src/GameBot.Domain/Parameters/ParameterScope.cs` unchanged in its text model. Add a test in `tests/unit/Parameters/ParameterScopeListTests.cs` that proves a joined default (`a|b`) resolves as the same text. Add a test that the option label lookup gives no label for a joined text and does not throw.

## Phase 8: Polish and cross-cutting

- [ ] T029 [P] Update `docs/architecture.md` (domain model, API surface, resolution rules) and refresh its "Last reviewed" date.
- [ ] T030 [P] Update `specs/STATUS.md` with the row for 179 and set the `Status` line in `specs/179-list-valued-parameters/spec.md` to Implemented. Update the API docs that list parameter declaration members (search `docs/` for `allowedImages`).
- [ ] T031 [P] Add a `CHANGELOG.md` entry in STE for the feature and the version line.
- [ ] T032 Bump `patch` in `installer/versioning/version.override.json` as the constitution says (master patch plus 1, or 0 if `minor` or `major` changed). Set `updatedBy` and `updatedAtUtc`. Add the version line to the CHANGELOG entry from T031.
- [ ] T033 Run `dotnet build` and `dotnet test` for the solution and `npm test` and `npm run lint` in `src/web-ui`. Fix all failures. Check coverage for touched areas (80% line, 70% branch).
- [ ] T034 Run the steps of `quickstart.md` against a local service when possible and note the result in the PR.

## Dependencies

- The phase order is deliberate: US3 (storage) comes before US2 (web UI) because the form needs the stored shape.
- Phase 2 blocks all other phases.
- US1 (Phase 3) before US4 (the declaration rules are used by value rules).
- US3 (Phase 4) can run after Phase 2, in parallel with US1.
- US2 (Phase 5) needs T005 to T007 for the wire shape. The web UI work can run in parallel with the backend phases 3, 4, 6, 7.
- US4 (Phase 6) and US5 (Phase 7) need Phase 2 and T009.
- Phase 8 last.

## Parallel examples

- T004, T008, T010 can run together (different test files).
- T015 and T021 and T025 can run together before their implementation tasks.
- T029, T030, T031 can run together.

## Implementation strategy

MVP is Phases 1 to 5 (declare, store, select). Then add validation (Phase 6) and resolution (Phase 7). Write each test first and see it fail before the implementation task. Commit after each phase when the build and tests are green.
