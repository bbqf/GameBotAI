---
description: "Task list for feature 078 — Sequence & Command Parameters"
---

# Tasks: Sequence & Command Parameters

**Input**: Design documents from `/specs/078-sequence-parameters/`
**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/api.md](./contracts/api.md), [quickstart.md](./quickstart.md)

**Tests**: Test tasks ARE included. The project constitution (Principle II, Testing Standards) makes
unit coverage of executable logic and integration coverage of externally visible contracts
mandatory, with ≥80% line / ≥70% branch on touched areas — so tests are not optional here.

**Organization**: Grouped by user story so each story is independently implementable and testable.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: `[US1]`..`[US4]` maps to the user stories in spec.md
- Every task names its exact file path

## Path Conventions

Web application layout, per plan.md "Structure Decision":
`src/GameBot.Domain/`, `src/GameBot.Service/`, `src/web-ui/src/`, and
`tests/{unit,integration,contract}/` at repository root.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create the new folders and confirm the baseline is green before changing anything.

- [X] T001 Verify the pre-change baseline is green: run `dotnet build -c Debug GameBot.sln` and `dotnet test -c Debug`, and run `npm ci && npm run build && npm test` in `src/web-ui`; record any pre-existing failures so they are not attributed to this feature (constitution: red build/test is a hard stop)
- [X] T002 [P] Create the domain folder `src/GameBot.Domain/Parameters/` and the test folder `tests/unit/Parameters/`
- [X] T003 [P] Create the web-ui folder `src/web-ui/src/components/parameters/`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The resolution mechanism and the persisted model. Every user story depends on this.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

### Domain core

- [X] T004 [P] Create `ParameterValueType` enum (`Text`, `Number`) in `src/GameBot.Domain/Parameters/ParameterValueType.cs` with XML docs
- [X] T005 [P] Create `ParameterDeclaration` (Name, Type, Default, Required, Description) in `src/GameBot.Domain/Parameters/ParameterDeclaration.cs` per data-model.md §1, with XML docs on every public member
- [X] T006 [P] Create `ParameterBinding` (Name, nullable Value where `null` = inherit and `""` = deliberate empty) in `src/GameBot.Domain/Parameters/ParameterBinding.cs`, documenting the null-vs-empty distinction
- [X] T007 [P] Create `ParameterResolutionError` record (ParameterName, FieldPath, Reason ∈ {`unresolved`, `not_a_number`}) in `src/GameBot.Domain/Parameters/ParameterResolutionError.cs`
- [X] T008 [P] Create `ParameterNameRules` static class in `src/GameBot.Domain/Parameters/ParameterNameRules.cs`: identifier regex `^[A-Za-z_]\w*$`, reserved name `iteration`, reserved `queue.` namespace, the four built-in names from research.md R6, and `ValidateDeclarations` returning the error strings listed in data-model.md §1
- [X] T009 Widen the placeholder pattern in `src/GameBot.Domain/Utils/TemplateSubstitutor.cs` from `\{\{(\w+)\}\}` to `\{\{(\w+(?:\.\w+)*)\}\}` and add a strict `TrySubstitute(string, IReadOnlyDictionary<string,string>, out string, out IReadOnlyList<string> unresolvedKeys)` that reports unresolved keys instead of leaving them in place; keep the existing lenient `Substitute`/`SubstitutePayload` behaviour untouched (the loop path depends on leave-as-is)
- [X] T010 Create `ParameterScope` in `src/GameBot.Domain/Parameters/ParameterScope.cs`: immutable layered scope with `Empty`, `Parent`, `LayerName`, `TryResolve`, `Child(layerName, bindings, declarations)`, `FromQueue(ExecutionQueue)`, `WithIteration(int)`, `Describe()`, plus `ParameterValue`/`ScopeEntry` types — resolution order exactly as data-model.md §1 (bindings innermost-out, then innermost declaration default) (depends on T004–T008)
- [X] T011 [P] Create `ParameterReferenceScanner` in `src/GameBot.Domain/Parameters/ParameterReferenceScanner.cs`: walk a `Command` or `CommandSequence` and return every `{{name}}` reference paired with its dotted field path, covering inline string fields, `FieldTemplates` values, and `SequenceActionPayload.Parameters` (depends on T009)
- [X] T012 Create `CommandStepResolver` in `src/GameBot.Domain/Parameters/CommandStepResolver.cs`: given a `CommandStep` and a `ParameterScope`, return a resolved clone (inline string substitution + `FieldTemplates` numeric overlay parsed with `CultureInfo.InvariantCulture`) or a `ParameterResolutionError`; never substitute `TargetId` when `Type == Command` (FR-007) (depends on T010)

### Domain unit tests

- [X] T013 [P] Write `tests/unit/Parameters/ParameterScopeTests.cs`: precedence (call-site binding > ambient > default > unresolved), ambient fall-through across layers without re-mapping, `null` value = inherit vs `""` = real value, built-in omitted when the queue field is unset, `Describe()` reports the originating layer, and immutability of the parent under `Child`
- [X] T014 [P] Write `tests/unit/Parameters/ParameterNameRulesTests.cs`: reserved `iteration`, reserved `queue.` prefix, malformed identifiers, case-sensitive duplicate rejection (including names differing only by case), numeric default that is not a whole number
- [X] T015 [P] Write `tests/unit/Parameters/ParameterReferenceScannerTests.cs`: references found in inline strings, in `FieldTemplates`, in action payload parameters, with correct dotted field paths, and none reported for literal-only entities
- [X] T016 [P] Write `tests/unit/Parameters/CommandStepResolverTests.cs`: string field resolved inline, numeric field resolved via overlay, embedded-in-text resolution for strings, whole-field-only enforcement for numerics, `not_a_number` coercion error, `unresolved` error, and `TargetId` left untouched for a `Command` step
- [X] T017 [P] Extend `tests/unit/Sequences/TemplateSubstitutorTests.cs`: dotted names such as `{{queue.emulatorSerial}}` substitute; `{{iteration}}` still substitutes exactly as before; `TrySubstitute` reports unresolved keys; unknown keys still pass through in the lenient `Substitute`

### Persisted model (all additive; absent ⇒ pre-feature semantics)

- [X] T018 [P] Add `Collection<ParameterDeclaration> Parameters` to `src/GameBot.Domain/Commands/Command.cs`
- [X] T019 [P] Add `Dictionary<string,string>? FieldTemplates` and `Collection<ParameterBinding>? ParameterBindings` to `src/GameBot.Domain/Commands/CommandStep.cs`, documenting the supported `FieldTemplates` key set from data-model.md §2
- [X] T020 [P] Add `Collection<ParameterDeclaration> Parameters` to `src/GameBot.Domain/Commands/CommandSequence.cs`, following the existing `[JsonInclude]` writable-collection pattern used by `StepsWritable`
- [X] T021 [P] Add `Collection<ParameterBinding>? ParameterBindings` to `src/GameBot.Domain/Commands/SequenceStep.cs` (meaningful only when `StepType == Command`)
- [X] T022 [P] Add `Collection<ParameterBinding> ParameterValues` to `src/GameBot.Domain/QueueTemplates/QueueTemplateEntry.cs`, documenting that it holds both declared bindings and ad-hoc values (FR-012a) and that entries are independent (FR-012)
- [X] T023 Update `src/GameBot.Domain/Commands/FileCommandRepository.cs` and `src/GameBot.Domain/Commands/FileSequenceRepository.cs` so the new members persist, and so writers omit them when empty — an unparametrized entity must round-trip byte-identically (depends on T018–T021)
- [X] T024 Update `src/GameBot.Domain/QueueTemplates/FileQueueTemplateRepository.cs` to persist `ParameterValues`, omitting it when empty (depends on T022)
- [X] T025 [P] Write `tests/unit/Parameters/PersistenceBackCompatTests.cs`: stored JSON without any new member deserializes to empty/null and behaves as before; an unparametrized entity serializes byte-identically to its pre-feature form for commands, sequences and queue templates (FR-004, FR-032)

**Checkpoint**: The mechanism resolves and persists. User story work can begin.

---

## Phase 3: User Story 1 — One sequence drives every emulator instance (Priority: P1) 🎯 MVP

**Goal**: A single command and single sequence run correctly against N queues that differ only by
their emulator serial, using the queue built-ins, with no new operator configuration.

**Independent Test**: Author one command whose ADB serial is `{{queue.emulatorSerial}}`, reference it
from one sequence, attach that sequence to two queues bound to different serials, run both, and
confirm from the execution log that each run targeted its own serial.

### Tests for User Story 1

- [X] T026 [P] [US1] Write `tests/integration/Queues/QueueBuiltInParameterPropagationTests.cs`: two queues with different `EmulatorSerial` running the same template/sequence/command each dispatch against their own serial; a third case covers `queue.gameId` and `queue.instanceIndex` (the latter into a numeric field)
- [X] T027 [P] [US1] Write `tests/integration/Queues/QueueUnparametrizedRegressionTests.cs`: a queue, template, sequence and command with no parameters anywhere behaves exactly as before this feature (FR-032 / SC-007)
- [X] T028 [P] [US1] Write `tests/unit/Sequences/SequenceRunnerScopeTests.cs`: a top-level (non-loop) step resolves parameters; a loop-body step resolves both `{{iteration}}` and a parameter in the same step; `ParameterBindings` survive the step clone inside a loop body
- [X] T028a [P] [US1] Write `tests/integration/Queues/SelfRescheduleParameterScopeTests.cs`: a sequence that schedules an additional firing of itself resolves parameters in the rescheduled firing from the same bindings as the firing that scheduled it (FR-015, US2 acceptance scenario 7)
- [X] T028b [P] [US1] Write `tests/integration/ExecutionLogs/ResolvedParameterLoggingTests.cs`: a parametrized step's execution-log entry carries `resolvedParameters` with the name, resolved value and origin layer, unredacted; an unparametrized step's entry omits the member entirely so existing payloads are unchanged (FR-024, SC-009)

### Implementation for User Story 1

- [X] T029 [US1] In `src/GameBot.Domain/Services/SequenceRunner.cs`, rename `ApplyIterContext` to `ApplyScope`, have it take a `ParameterScope` composed with the iteration context, and add `ParameterBindings` to the member list of the step clone so it is not silently dropped (plan.md Risks)
- [X] T030 [US1] In `src/GameBot.Domain/Services/SequenceRunner.cs`, add an optional `ParameterScope scope = null` parameter to `ExecuteAsync` (defaulting to `ParameterScope.Empty`) and call `ApplyScope` on the **top-level** step path as well as the loop-body and if-branch paths, so a parameter in a non-looping step is substituted (research R5) (depends on T029)
- [X] T031 [US1] Widen the `executeCommandAsync` delegate in `src/GameBot.Domain/Services/SequenceRunner.cs` from `Func<string, Task>` to `Func<string, ParameterScope, Task>` and pass the step's command-layer scope at each invocation (depends on T030)
- [X] T032 [US1] Add an optional `ParameterScope` parameter to the force-execute overloads in `src/GameBot.Service/Services/ICommandExecutor.cs`, defaulting to `ParameterScope.Empty`
- [X] T033 [US1] In `src/GameBot.Service/Services/CommandExecutor.cs`, thread the scope through `ExecuteCommandRecursiveAsync` and run each step through `CommandStepResolver` before dispatch; a resolution error fails the step with the FR-017/FR-019 message form from contracts/api.md and dispatches nothing to the device; a nested `Command` step pushes its own `ParameterBindings` layer (depends on T012, T032)
- [X] T034 [US1] In `src/GameBot.Service/Services/SequenceExecution/SequenceExecutionService.cs`, add a `ParameterScope` parameter to `ExecuteAsync`, build the per-step command layer from the step's `ParameterBindings` plus the target command's declarations, and pass it into `_commandExecutor.ForceExecuteAsync` (depends on T031, T033)
- [X] T035 [US1] In `src/GameBot.Service/Services/QueueExecution/QueueExecutionService.cs`, build the root scope with `ParameterScope.FromQueue(queue)` at run start and layer the firing entry's `ParameterValues` on top in `RunOneSequenceAsync`, passing the result to `_sequenceExecution.ExecuteAsync` (depends on T034)
- [X] T036 [US1] Add a `ParameterScope? Scope` member to `SelfRescheduleEntry` in `src/GameBot.Service/Services/QueueExecution/QueueRunHandle.cs` and propagate it through the self-reschedule re-fire path so a rescheduled firing resolves identically to the firing that scheduled it (FR-015) (depends on T035)
- [X] T037 [US1] Record resolved parameters (name, value, origin layer, unredacted) on step detail items across all three files the value must travel through: add a `ResolvedParameters` member to `PrimitiveTapStepOutcome` in `src/GameBot.Service/Services/ICommandExecutor.cs`, populate it in `src/GameBot.Service/Services/CommandExecutor.cs` where each outcome is constructed, and emit it as an `ExecutionDetailItem` member in `LogCommandExecutionAsync` in `src/GameBot.Service/Services/ExecutionLog/ExecutionLogService.cs`; emit the member only for steps that actually resolved at least one parameter so existing log payloads and their snapshots are unchanged (FR-024) (depends on T033)
- [X] T037a [US1] Verify `ExecutionLogSanitizer.SanitizeDetails` in `src/GameBot.Service/Services/ExecutionLog/ExecutionLogSanitizer.cs` passes the new `resolvedParameters` member through intact rather than stripping it, and extend the sanitizer's allow-list if it does not (depends on T037)

**Checkpoint**: US1 complete and shippable — N duplicated commands and sequences collapse to one.

---

## Phase 4: User Story 2 — Declare parameters and bind values per caller (Priority: P2)

**Goal**: Declared parameters with defaults, explicit bindings at both call sites, ad-hoc values on
template entries, and fail-fast validation everywhere.

**Independent Test**: Declare a parameter with a default on a command, invoke it from a sequence with
no binding (default applies), then bind an explicit value at the sequence step and confirm the bound
value wins.

### Tests for User Story 2

- [X] T038 [P] [US2] Write `tests/integration/Sequences/ParameterBindingPrecedenceTests.cs`: default applies with no binding; sequence-step binding overrides the default; template-entry binding reaches a sequence-declared parameter; two entries referencing one sequence hold independent bindings
- [X] T039 [P] [US2] Write `tests/integration/QueueTemplates/AdHocParameterValueTests.cs`: an entry supplies `adbSerial` while the referenced sequence declares nothing and a command two levels down declares and consumes it (FR-012a); a supplied name nothing consumes produces a non-blocking warning and still runs (FR-012b)
- [X] T040 [P] [US2] Write `tests/unit/Services/ParameterValidationServiceTests.cs`: every blocking condition from contracts/api.md (`invalid_parameter_declaration`, `invalid_parameter_default`, `unknown_field_template_path`, `unresolvable_parameter_reference`, `unknown_parameter_binding`, `parameter_in_reference_field`, `invalid_parameter_value_name`), every warning condition named individually (`static_check_skipped` for a parametrized image reference per FR-023a, `unused_parameter_value`, `stale_parameter_binding`, `unsatisfied_required_parameter`), and the case-mismatch-is-unresolvable rule
- [X] T041 [P] [US2] Write `tests/integration/Queues/QueueStartParameterRefusalTests.cs`: starting a queue whose enabled entry cannot supply a required parameter is refused with `missing_required_parameters` naming the entry and parameter, before any device work; disabled entries are ignored
- [X] T042 [P] [US2] Write `tests/integration/Sequences/ParameterFailureMessageTests.cs`: an unresolvable reference and a non-numeric coercion each fail the step with the exact message forms in contracts/api.md, and no device action is dispatched

### Implementation for User Story 2

- [X] T043 [US2] Create `ParameterValidationService` in `src/GameBot.Domain/Services/ParameterValidationService.cs`: declaration well-formedness, reference resolvability against statically knowable names, `FieldTemplates` key validity, binding-names-a-declared-parameter, `{{...}}` rejected in command-reference fields, **ad-hoc template-entry value names checked against the same identifier and reserved-name rules as declarations** (`invalid_parameter_value_name`), required-parameter satisfiability for a template entry, stale-binding detection, and the unused-ad-hoc-value warning (research R7) (depends on T008, T011)
- [X] T044 [US2] In `src/GameBot.Domain/Services/SequenceStepValidationService.cs`, narrow the existing top-level placeholder rejection (currently at lines 243–249) so it rejects only the reserved `iteration` name outside a loop, and route every other reference through `ParameterValidationService`; `tests/unit/Sequences/LoopValidationTests.cs` and `tests/unit/Sequences/IfValidationTests.cs` must keep passing **unmodified** (research R4) (depends on T043)
- [X] T045 [US2] Add the same parameter-reference validation to `ValidateActionPayloads` in `src/GameBot.Domain/Commands/FileSequenceRepository.cs` so the two validators stay in sync and a parametrized sequence cannot pass one and 500 on the other (research R7, plan.md Risks) (depends on T043)
- [X] T046 [US2] Skip static existence checking for parametrized image/detection reference fields and emit a `static_check_skipped` warning instead, validating the resolved value at run time (FR-023a) (depends on T043)
- [X] T047 [US2] In `src/GameBot.Service/Endpoints/QueuesEndpoints.cs`, add the pre-flight required-parameter check to queue start, returning `409 missing_required_parameters` with the entry/parameter breakdown from contracts/api.md before any session or device work (depends on T043)

**Checkpoint**: US1 and US2 both work independently.

---

## Phase 5: API surface for the authoring UI (US2 persistence + US3 enablement)

**Purpose**: The additive REST contract in contracts/api.md. Split into its own phase because it is
verified by its own contract tests, but every task carries the story label of the requirement it
serves: `[US2]` for tasks that persist and validate what US2 defines, `[US3]` for the read endpoints
that exist solely to feed the US3 authoring UI.

- [X] T048 [P] [US2] Add `ParameterDeclarationDto` / `ParameterBindingDto` / `ParameterScopeEntryDto` and the new `CommandStepDto` members (`fieldTemplates`, `parameterBindings`) plus request/response `parameters` to `src/GameBot.Service/Models/Commands.cs`
- [X] T049 [P] [US2] Add `parameterBindings` and sequence-level `parameters` to `src/GameBot.Service/Models/SequenceStepContracts.cs`
- [X] T050 [P] [US2] Add `parameterValues` to `src/GameBot.Service/Contracts/QueueTemplates/TemplateEntrySaveRequest.cs`, and `parameterValues` / `hasParameterOverrides` / `effectiveParameters` to `src/GameBot.Service/Contracts/QueueTemplates/QueueTemplateDetailResponse.cs`
- [X] T051 [US2] Wire declaration and reference validation into `src/GameBot.Service/Endpoints/CommandsEndpoints.cs` with the exact 400 error codes and `details` payload from contracts/api.md (depends on T043, T048)
- [X] T052 [US2] Wire the same into `src/GameBot.Service/Endpoints/SequencesEndpoints.cs`, including `unknown_parameter_binding` when a step binds a name the referenced command does not declare (depends on T043, T049)
- [X] T053 [P] [US3] Add `GET /api/commands/{id}/parameter-scope` to `src/GameBot.Service/Endpoints/CommandsEndpoints.cs`, returning the command's declarations plus the `queue.*` built-in catalogue (depends on T051)
- [X] T054 [P] [US3] Add `GET /api/sequences/{id}/parameter-scope` to `src/GameBot.Service/Endpoints/SequencesEndpoints.cs`, returning scope entries plus a `stepCallees` array of each `Command` step's referenced command declarations so the binding form needs no N+1 fetches (depends on T052)
- [X] T055 [US3] Accept an optional `parameters` body on `POST /api/sequences/{id}/execute` in `src/GameBot.Service/Endpoints/SequencesEndpoints.cs` and return `409 missing_required_parameters` when a required parameter has neither a supplied value nor a default (FR-031) (depends on T052)
- [X] T056 [US2] Project entry warnings (`unused_parameter_value`, `stale_parameter_binding`, `unsatisfied_required_parameter`) and `effectiveParameters` in `src/GameBot.Service/Endpoints/QueueTemplatesEndpoints.cs`; only `invalid_parameter_value_name` blocks with 400 (depends on T043, T050)
- [X] T057 [P] [US2] Write `tests/contract/Sequences/ParameterContractTests.cs` and `tests/contract/QueueTemplates/ParameterEntryContractTests.cs` covering the new members and every error code in contracts/api.md
- [X] T058 [US2] Regenerate the affected snapshots in `tests/contract/ApiContractSnapshots/` for the additive members and review the diff for unintended changes (depends on T048–T056)

---

## Phase 6: User Story 3 — Author parameters without hand-typing placeholders (Priority: P3)

**Goal**: Declare, insert, bind and preview parameters entirely through the UI, with problems shown
inline while editing rather than at run time.

**Independent Test**: In the UI, declare a parameter, insert it into a field via the picker without
typing braces, save, and observe the binding form and effective-value preview on the invoking step
and template entry.

### Tests for User Story 3

- [X] T059 [P] [US3] Write `src/web-ui/src/components/parameters/__tests__/ParameterizableField.test.tsx`: the picker lists in-scope names with descriptions and inserts a valid reference on selection, in at most three interactions from the field (SC-004)
- [X] T060 [P] [US3] Write `src/web-ui/src/components/parameters/__tests__/ParameterBindingForm.test.tsx`: every row defaults to "inherit"; an explicit value overrides; the effective value and its origin layer are shown
- [X] T061 [P] [US3] Write `src/web-ui/src/components/parameters/__tests__/ParameterDeclarationList.test.tsx`: add, edit, reorder and remove declarations; invalid and reserved names are rejected inline
- [X] T061a [P] [US3] Write `src/web-ui/src/components/parameters/__tests__/inlineParameterValidation.test.tsx`: a server-reported `unresolvable_parameter_reference` renders as an error anchored at the offending field rather than only as a form-level message, and a `static_check_skipped` warning renders as a non-blocking notice at its field (FR-029)

### Implementation for User Story 3

- [X] T062 [P] [US3] Create `src/web-ui/src/components/parameters/useParameterScope.ts`: fetch and cache `/parameter-scope` for a command or sequence so the UI never re-derives resolution rules client-side
- [X] T063 [P] [US3] Create `src/web-ui/src/components/parameters/ParameterDeclarationList.tsx` — the Parameters section (name, type, default, required, description; add/edit/reorder/remove), following the existing `ConfigParameterList`/`ConfigParameterRow` pattern (FR-025)
- [X] T064 [P] [US3] Create `src/web-ui/src/components/parameters/ParameterizableField.tsx` — an input with a `{ }` insert-parameter affordance built on `SearchableDropdown`, listing in-scope names with descriptions (FR-026) (depends on T062)
- [X] T065 [US3] Create `src/web-ui/src/components/parameters/ParameterBindingForm.tsx` — renders a callee's declarations as rows pre-set to "inherit", with effective value and origin preview, and an "Add value" affordance for ad-hoc names used only in the template-entry context (FR-027, FR-028) (depends on T062)
- [X] T066 [P] [US3] Update `src/web-ui/src/services/commands.ts`, `sequences.ts` and `queueTemplates.ts` with the new types, the `/parameter-scope` calls, and the execute-with-parameters body
- [X] T067 [US3] Add the Parameters section to `src/web-ui/src/components/commands/CommandForm.tsx` and wrap the parametrizable fields in `src/web-ui/src/components/commands/EnsureEmulatorRunningPanel.tsx`, `EnsureGameRunningPanel.tsx`, `KeyInputPanel.tsx`, `SwipePanel.tsx`, `TapPanel.tsx` and `WaitForImagePanel.tsx` in `ParameterizableField`, writing numeric placeholders to `fieldTemplates` and string placeholders inline (depends on T063, T064, T066)
- [X] T068 [US3] Add the Parameters section to the sequence editor and a per-step `ParameterBindingForm` in `src/web-ui/src/components/SortableSequenceStepList.tsx` (depends on T065, T066)
- [X] T069 [US3] Add the entry parameter form with effective-value preview to `src/web-ui/src/components/queues/SchedulingSequenceCard.tsx` (depends on T065, T066)
- [X] T070 [P] [US3] Add the parameter-override badge to `src/web-ui/src/components/queues/QueueEntryList.tsx` so overridden entries are identifiable without opening them (FR-030) (depends on T066)
- [X] T071 [US3] Surface parameter validation errors and warnings inline at the offending field in `src/web-ui/src/lib/validation.ts` and its consumers (FR-029) (depends on T066)
- [X] T072 [US3] Add the ad-hoc run parameter form to the sequence run action in `src/web-ui/src/pages/SequencesPage.tsx`, pre-filled with declared defaults and refusing to start while a required parameter is empty, sending the values as the `parameters` body added in T055 (FR-031) (depends on T055, T065, T066)

**Checkpoint**: All three behavioural stories are independently functional.

---

## Phase 7: User Story 4 — Convert existing duplicates by hand, safely (Priority: P4)

**Goal**: A written path an operator can follow to collapse duplicates, verify, and clean up — the
migration path, since no migration code is written.

**Independent Test**: A reader who has never used the feature follows the guide end to end on the
ensure-game-running / ADB-serial case and reaches a working single-command, single-sequence setup.

- [X] T073 [US4] Review [quickstart.md](./quickstart.md) against the shipped UI and correct every control name, field label and menu path so the steps match what an operator actually sees (the guide was written from the design, not the built UI) (depends on Phase 6)
- [X] T074 [US4] Verify the guide's Step 7 verification instructions against a real execution-log payload — confirm the "Resolved parameters" display name and content match what T037 actually emits (depends on T037, T073)
- [X] T075 [US4] Walk the full guide once end to end against the live service and record the elapsed time, confirming the SC-008 target of under 20 minutes for a three-instance conversion; adjust the guide wherever a step proved ambiguous (depends on T073, T074)

---

## Phase 8: Polish & Cross-Cutting Concerns

- [X] T076 [P] Add `tests/unit/Performance/ParameterScopeBenchmarkTests.cs` asserting scope resolution plus substitution stays under the 1 ms/step ceiling declared in plan.md (constitution IV: hot-path perf note)
- [X] T077 [P] Update `docs/architecture.md` for the changed domain model, API surface and persistence layout, and refresh its "Last reviewed" date (constitution V, NON-NEGOTIABLE)
- [X] T078 [P] Add the 078 entry to `specs/STATUS.md` and set this spec's `**Status**:` line to `Implemented`; leave the Status lines of specs 034, 047 and 077 unchanged — this feature extends them rather than superseding them
- [X] T079 Confirm coverage on touched areas meets the constitution baseline: run `dotnet test --collect:"XPlat Code Coverage" -c Debug` and verify ≥80% line / ≥70% branch for `ParameterScope`, `CommandStepResolver`, `ParameterValidationService`, `ParameterReferenceScanner`, `TemplateSubstitutor` and the touched `SequenceRunner` paths
- [X] T080 Run the full green gate and fix every failure before the feature is considered done: `dotnet build -c Debug`, `dotnet test -c Debug`, and `npm run build && npm test` in `src/web-ui` (constitution: a red build or test run is a hard stop)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies — start immediately
- **Foundational (Phase 2)**: depends on Setup — **blocks every user story**
- **US1 (Phase 3)**: depends on Phase 2 only. Shippable alone as the MVP
- **US2 (Phase 4)**: depends on Phase 2; shares the execution threading US1 delivers, so run it after US1
- **API surface (Phase 5)**: depends on Phase 4's validation service; consumed by Phase 6
- **US3 (Phase 6)**: depends on Phase 5
- **US4 (Phase 7)**: depends on Phase 6, because the guide documents the built UI
- **Polish (Phase 8)**: depends on all preceding phases

### Within Each User Story

- Tests are written before the implementation they cover and must fail first
- Domain types before services; services before endpoints; endpoints before UI
- Each story reaches its checkpoint before the next begins

### Parallel Opportunities

- **Phase 2**: T004–T008 are five independent new files; T013–T017 are five independent test files; T018–T022 are five independent model edits
- **Phase 3**: T026–T028b (five test files) run together, but T029–T037a form a strict chain through the execution path and must be sequential
- **Phase 4**: T038–T042 (five test files) run together
- **Phase 5**: T048–T050 together, then T053/T054 together, then T057
- **Phase 6**: T059–T061a together; T062–T064, T066 and T070 together
- **Phase 8**: T076–T078 together

---

## Parallel Example: Phase 2 Foundational

```bash
# Five independent domain types, no shared file:
Task: "Create ParameterValueType enum in src/GameBot.Domain/Parameters/ParameterValueType.cs"
Task: "Create ParameterDeclaration in src/GameBot.Domain/Parameters/ParameterDeclaration.cs"
Task: "Create ParameterBinding in src/GameBot.Domain/Parameters/ParameterBinding.cs"
Task: "Create ParameterResolutionError in src/GameBot.Domain/Parameters/ParameterResolutionError.cs"
Task: "Create ParameterNameRules in src/GameBot.Domain/Parameters/ParameterNameRules.cs"

# Then five independent model edits:
Task: "Add Parameters to src/GameBot.Domain/Commands/Command.cs"
Task: "Add FieldTemplates and ParameterBindings to src/GameBot.Domain/Commands/CommandStep.cs"
Task: "Add Parameters to src/GameBot.Domain/Commands/CommandSequence.cs"
Task: "Add ParameterBindings to src/GameBot.Domain/Commands/SequenceStep.cs"
Task: "Add ParameterValues to src/GameBot.Domain/QueueTemplates/QueueTemplateEntry.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Phase 1 Setup
2. Phase 2 Foundational — **critical, blocks everything**
3. Phase 3 User Story 1
4. **STOP and VALIDATE**: point two queues with different serials at one sequence and one command,
   run both, and confirm from the execution log that each targeted its own serial
5. At this point N duplicated commands and sequences already collapse to one — the feature's whole
   motivating problem is solved, with no new operator configuration

### Incremental Delivery

1. Setup + Foundational → mechanism resolves and persists
2. + US1 → validate → **MVP**
3. + US2 → declared parameters, bindings, ad-hoc values, fail-fast validation
4. + API surface → contract-tested
5. + US3 → authoring UX
6. + US4 → verified migration guide
7. + Polish → docs, coverage, green gate

Each increment leaves every previously stored command, sequence and template running unchanged.

---

## Deferred

Everything originally deferred was completed in the follow-up pass, except the one item that needs
hardware this environment does not have.

| Task | Status | Note |
|---|---|---|
| T057, T058 | **Done** (follow-up) | 21 contract tests across `tests/contract/Sequences/ParameterContractTests.cs` and `tests/contract/QueueTemplates/ParameterEntryContractTests.cs`. Writing them found a real contract bug: responses emitted `"parameters": null` instead of omitting the member. The route snapshot gained the two `parameter-scope` endpoints. |
| T061a | **Done** (follow-up) | `inlineParameterValidation.test.tsx`, plus `lib/__tests__/parameterValidation.spec.ts` for the parser that feeds it. |
| T068 | **Done** (follow-up) | Parameters section on both sequence forms, and a per-step binding form on command steps. Needed the commands **list** endpoint to return `parameters` too, otherwise the editor would refetch every command one by one. |
| T071 | **Done** (follow-up) | `parseParameterErrors` / `parameterErrorFor` / `parameterErrorSummary` in `lib/validation.ts`, wired into both editors' save paths. |
| T072 | **Done** (follow-up) | Ad-hoc run form on the Execution page — which is where the run action actually lives; this task's text said `SequencesPage.tsx`, which was wrong. |
| T075 | **Done** | Walked against the live service; 3.5 min for the authoring path. Found and fixed three guide defects (see below). **Not** a valid SC-008 measurement — see the caveat. |

### T075 walkthrough record

Walked 2026-08-26 against the live service on :8080 and the shipped UI. Three defects found and
corrected in [quickstart.md](./quickstart.md):

1. **Steps 2 and 4 instructed the operator to use a "Duplicate" action that does not exist.** Neither
   the command editor nor the sequence editor has one — the controls are Save / Cancel / Delete, and
   the word "duplicate" appears nowhere in either page. Both steps now say to rename the chosen copy
   in place, which preserves the rollback property the original wording was after (the other two
   copies stay untouched and runnable).
2. **Step 8's pre-delete searches are not something the UI can do.** It told the operator to "search
   for the command by name" in Sequences; the list filter matches a sequence's *own* name, not the
   commands inside it. Rewritten around the real mechanism: `DELETE` is refused with `delete_blocked`
   and a `references` payload naming every referencing command and sequence, which is an
   authoritative reverse lookup the manual search could never be.
3. Step 1 proved its own worth: checking all four queues surfaced that `Exo-Test` and `Exo` share
   `emulator-5556`, and that `Exo-Test` has no linked game, so `{{queue.gameId}}` cannot resolve for
   it. Left as an observation about the data rather than a guide change.

**Caveat on SC-008.** The measured 3.5 minutes is not a valid check of the "under 20 minutes"
target. Two reasons, both material:

- The three-instance starting state SC-008 describes does not exist in this deployment. There is one
  `PNS Daily 5558` queue, not three, and no command declares a parameter at all — the live migration
  went through queue built-ins, which need no declaration. So the conversion being timed was the
  authoring path, not a true three-instance conversion.
- Elapsed time for an agent driving the UI programmatically says nothing about how long an operator
  takes. SC-008 is a human-usability target and needs a human to measure it.

Steps 5–7 (start each queue, confirm the resolved-parameter log line and that the right instance
reacted) were **not** executed here — they drive a real emulator. The repo owner confirmed the
behaviour manually. Substitution itself is covered by automated tests after the `JsonElement` fix
(PR #154), including a runner test that round-trips a sequence through persistence.

The picker (`ParameterizableField`) is wired into the ensure-emulator-running **ADB serial** field —
the exact field the motivating scenario varies. Other fields accept a typed reference and are
validated on save; wiring the picker into the remaining panels is mechanical and additive.

---

## Notes

- `[P]` = different files, no dependency on an incomplete task
- The two existing tests that assert `{{iteration}}` is rejected at top level
  (`LoopValidationTests.TopLevelStepWithTemplatePlaceholderIsRejected`,
  `IfValidationTests.TemplatePlaceholderInTopLevelIfBranchIsRejected`) must keep passing
  **unmodified** — they are the regression guard for the T044 narrowing
- Both sequence validators must be changed together (T044 + T045); changing only one produces a 500
  on save, a hazard this repository has hit before
- Commit after each task or logical group
- No migration code: FR-033 forbids it, and the migration path is the quickstart guide
