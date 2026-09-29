---

description: "Task list for feature 115: resolve a placeholder in a step parameterBindings value against the sequence scope"
---

# Tasks: Resolve a placeholder in a step parameterBindings value against the sequence scope

**Input**: Design documents from `specs/115-binding-placeholder-scope/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/binding-resolution.md, quickstart.md

**Tests**: The spec requests tests (FR-011), and Constitution Principle II requires a failing test that reproduces issue #246 before the fix. Each story phase starts with its tests. Write them first and make sure that they fail on the current code (except the control tests of US2, which must pass before and after the change).

**Organization**: The tasks are grouped by user story. Each story can be tested independently.

**Language rule**: Write all new text (code comments, XML docs, log and error messages, docs, CHANGELOG) in ASD-STE100 Simplified Technical English (Constitution Principle VI).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: The task can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: The user story of the task (US1, US2, US3)
- Each description has the exact file path

## Path Conventions

- Domain: `src/GameBot.Domain/`
- Service host: `src/GameBot.Service/`
- Unit tests: `tests/unit/` (project `tests/unit/GameBot.UnitTests.csproj`)
- Integration tests: `tests/integration/` (project `tests/integration/GameBot.IntegrationTests.csproj`)

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Get a green baseline before the change.

- [ ] T001 Build the solution and run the current parameter tests to get a green baseline: `dotnet build "C:\src\GameBot\GameBot.sln"` and `dotnet test "C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj" --filter "FullyQualifiedName~ParameterScope|FullyQualifiedName~CommandStepResolver"`. Record any failure that exists before the change. Do not continue on a red build (Constitution NON-NEGOTIABLE gate).
- [ ] T002 [P] Read the current code at the call sites to confirm the facts of research.md: `ParameterScope.Child`, `TryResolve`, and the `ParameterValue` record struct (line ~33) in `src/GameBot.Domain/Parameters/ParameterScope.cs`; the `scope.Child(ParameterScopeLayers.Command, originalStep.ParameterBindings, null)` call in `ExecuteSingleStepAsync` of `src/GameBot.Domain/Services/SequenceRunner.cs`; the `commandScope.Child(...)` call for a nested command step in `ExecuteCommandRecursiveAsync` of `src/GameBot.Service/Services/CommandExecutor.cs`; `RecordUsage` and `TryOverlayValue` in `src/GameBot.Domain/Parameters/CommandStepResolver.cs`; and the test harness pattern (`[Collection("ConfigIsolation")]`, `TestEnvironment.PrepareCleanDataDir()`) in `tests/integration/Commands/ParametrizedReferenceImageIntegrationTests.cs`.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The domain scope change that all stories use (plan D1 and D2).

**CRITICAL**: No user story work can start before this phase is complete.

### Tests for the foundation (write first, see them fail)

- [ ] T003 Create `tests/unit/Parameters/ParameterScopeBindingTests.cs` (xUnit + FluentAssertions) with tests for `ParameterScope.TryBindChild(layerName, bindings, out child, out error)`. Build an outer scope with `ParameterScope.FromQueue(...)`, then `Child(ParameterScopeLayers.Entry, ...)` and `Child(ParameterScopeLayers.Sequence, ..., declarations)` as needed. Cases:
  1. Whole placeholder: entry `novaOptionImage=pns-alliance-nav-button`, binding `novaOptionImage`=`{{novaOptionImage}}` → `child.TryResolve("novaOptionImage")` gives text `pns-alliance-nav-button` and `OriginLayer` `entry` (FR-001, FR-003).
  2. Same-name binding resolves outward, never against itself (FR-002).
  3. Binding `a`=`{{b}}` gets the value of `b`; a second binding `b`=`other` of the same step is not visible to `a` (FR-002).
  4. Placeholder that resolves to a declared default → `OriginLayer` `default`.
  5. `{{iteration}}` under `WithIteration(3)` → text `3`, layer `loop`.
  6. Mixed value `nova-{{option}}` with entry `option=b` → text `nova-b`, layer `command`, `Sources` = one item (`option`, `b`, `entry`) (FR-003a).
  7. Literal values `pns-todo-radar`, `${x}`, and `""` stay literal with layer `command` and `Sources` null (FR-005, FR-007).
  8. A binding with a `null` value is skipped, so the name inherits the outer value and layer (FR-006).
  9. Unresolved `{{missing}}` on binding `novaOptionImage` → returns `false`, `child` null, `error` = `ParameterResolutionError("missing", "parameterBindings.novaOptionImage", ParameterResolutionReasons.Unresolved)` (FR-004).
  10. Leading or trailing space around a placeholder (` {{x}}`) goes to the mixed path and keeps the space (research R3).
  11. With no placeholder in any binding value, the result resolves the same as `Child(layerName, bindings, null)`.
  12. The default of the called command is not used (spec Edge Cases, plan D2): the outer scope has no value and no declaration with a default for `novaOptionImage`, and the binding is `novaOptionImage`=`{{novaOptionImage}}`. Make a separate scope that stands for the called command: `Child(ParameterScopeLayers.Command, ..., declarations)` with a declaration of `novaOptionImage` that has the default `pns-todo-radar`. `TryBindChild` on the outer scope returns `false` and gives the error `ParameterResolutionError("novaOptionImage", "parameterBindings.novaOptionImage", ParameterResolutionReasons.Unresolved)`. The value `pns-todo-radar` is not used (FR-004).

### Implementation for the foundation

- [ ] T004 In `src/GameBot.Domain/Parameters/ParameterScope.cs`, add the optional third member `IReadOnlyList<ResolvedParameter>? Sources = null` to the `ParameterValue` record struct. Current two-argument constructions must compile without change. Add an XML doc: "Set only for a binding value with text around one or more placeholders. One item for each placeholder name, with its value and origin layer."
- [ ] T005 In `src/GameBot.Domain/Parameters/ParameterScope.cs`, change `_values` from `Dictionary<string, string>` to `Dictionary<string, ParameterValue>`. `Child`, `FromQueue`, and `WithIteration` store `new ParameterValue(text, LayerName)`. `TryResolve` returns the stored `ParameterValue` as it is. Update each other reader of `_values` in the class (for example `ToSubstitutionContext` or an enumeration of names) to read `.Text`. Behavior must not change (depends on T004).
- [ ] T006 In `src/GameBot.Domain/Parameters/ParameterScope.cs`, add `public bool TryBindChild(string layerName, IEnumerable<ParameterBinding>? bindings, [NotNullWhen(true)] out ParameterScope? child, [NotNullWhen(false)] out ParameterResolutionError? error)` per plan D2: skip a binding with a null name or null value; no placeholder (`TemplateSubstitutor.ContainsPlaceholder` false) → `ParameterValue(value, layerName)`; exactly one whole placeholder (`value == "{{key}}"`, no trim) → store `this.TryResolve(key)` result as it is; text around placeholders → `TemplateSubstitutor.TrySubstitute(value, this.ToSubstitutionContext())` (make the map one time, only when the first mixed value occurs) and store `ParameterValue(result, layerName, sources)`; an unresolved key → `error = new ParameterResolutionError(key, $"parameterBindings.{binding.Name}", ParameterResolutionReasons.Unresolved)`, `child = null`, return `false`. All lookups use `this`, never the new layer. Keep the method under 50 lines and add an XML doc with inputs, outputs, and the error mode (STE). Depends on T005. Run T003: all cases must pass.
- [ ] T007 Run `dotnet test "C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj" --filter "FullyQualifiedName~ParameterScope"`. All current `ParameterScopeTests` and the new `ParameterScopeBindingTests` must pass (SC-004).

**Checkpoint**: `TryBindChild` exists and is tested. The call sites do not use it yet.

---

## Phase 3: User Story 1 - A binding placeholder gets the value of the sequence scope (Priority: P1) MVP

**Goal**: A `{{name}}` binding value on a sequence step and on a nested command step resolves against the outer scope. The command receives the true value and origin layer, and the log shows them (FR-001, FR-002, FR-003, FR-003a, FR-004a, FR-008).

**Independent Test**: Run the issue #246 reproduction through a queue with a device test double. The `parameters` log item shows `novaOptionImage=pns-alliance-nav-button` with `originLayer` `entry`, and the tap step uses that image id.

### Tests for User Story 1 (write first, see them fail)

- [ ] T008 [P] [US1] Create `tests/unit/Sequences/SequenceRunnerBindingScopeTests.cs`. Use the current `SequenceRunner` test setup pattern of `tests/unit/Sequences` (a capture of the scope that the command dispatcher receives). Tests: (a) a step with binding `novaOptionImage`=`{{novaOptionImage}}` and an entry value `pns-alliance-nav-button` → the dispatcher scope resolves `novaOptionImage` to `pns-alliance-nav-button` with layer `entry` (FR-001, US1-1); (b) a step guard `imageVisible` with `imageId` `{{novaOptionImage}}` and the same binding on the same step → the guard and the command get the same value (US1-4); (c) a loop body step with binding `n`=`{{iteration}}` → the command gets the iteration number with layer `loop`; (d) a dry run gives the same resolution as a real run.
- [ ] T009 [P] [US1] In `tests/unit/Parameters/CommandStepResolverTests.cs`, add a test: a command scope where `novaOptionImage` has a `ParameterValue` with text `nova-b`, layer `command`, and `Sources` [(`option`, `b`, `entry`)], and a field template `{{novaOptionImage}}` → the used list has `novaOptionImage`/`nova-b`/`command` followed by `option`/`b`/`entry` (FR-008, plan D5). Add a second assert that a value with `Sources` null gives the same used list as before. Add a third assert (no duplicate, plan D5): the step also has a field template `{{option}}` on a different field that the resolver reads first, so the used list has `option` before `novaOptionImage` → the used list has exactly one `option` item.
- [ ] T010 [P] [US1] Create `tests/integration/Commands/BindingPlaceholderScopeIntegrationTests.cs` with `[Collection("ConfigIsolation")]` and `TestEnvironment.PrepareCleanDataDir()` (the pattern of `ParametrizedReferenceImageIntegrationTests.cs`). Test 1 (issue #246 reproduction, SC-001, US1-2, US1-3): create command `ZZZ.NovaParamTap` with required text parameter `novaOptionImage` and one `PrimitiveTap` step with `fieldTemplates` `{"primitiveTap.detectionTarget.referenceImageId": "{{novaOptionImage}}"}`; create a sequence that declares `novaOptionImage` (required, no default) with one step that runs the command with `parameterBindings: [{"name":"novaOptionImage","value":"{{novaOptionImage}}"}]`; run it through a queue template entry with `novaOptionImage = pns-alliance-nav-button`; assert the command log item `details[kind=parameters]` has `resolvedParameters` item `novaOptionImage` / `pns-alliance-nav-button` / `entry`, and that no log item has the value `{{novaOptionImage}}`. Assert the resolved value in the log, not the tap status (the tap status can change between hosts). Do not upload an image. Test 2 (FR-004a): a command `ZZZ.Outer` declares the text parameter `novaOptionImage` (required, no default) and has a nested `Command` step that calls `ZZZ.NovaParamTap` with binding `novaOptionImage`=`{{novaOptionImage}}`. The value gets to `ZZZ.Outer` on this run path: a sequence declares `novaOptionImage` (required, no default) and has one step that runs `ZZZ.Outer` with no `parameterBindings`; a queue template entry supplies `novaOptionImage = pns-alliance-nav-button` (the `entry` layer); the queue runs the sequence. Thus `ZZZ.Outer` inherits the entry value (the control of US2-1), and the nested binding resolves against the scope of `ZZZ.Outer`. Assert that the `details[kind=parameters]` log item of the nested command `ZZZ.NovaParamTap` has the `resolvedParameters` item `novaOptionImage` / `pns-alliance-nav-button` / `entry` (the whole placeholder keeps the origin layer, FR-003), and that no log item has the value `{{novaOptionImage}}`. Test 3 (FR-003a, contract C4): the sequence declares the text parameter `option` (required, no default), so that the save accepts the placeholder `{{option}}`. The sequence does not declare `novaOptionImage`, because the binding supplies it. The command is `ZZZ.NovaParamTap` of Test 1. The step binding is `novaOptionImage`=`nova-{{option}}`, and the queue template entry supplies `option=b`. Assert that `resolvedParameters` has exactly two items, `novaOptionImage`/`nova-b`/`command` and then `option`/`b`/`entry`, and the log item message is `Step 0 resolved 2 parameter(s): novaOptionImage=nova-b, option=b` (the count includes the source item).
- [ ] T011 [US1] Run T008, T009, and T010 on the current code and confirm that they fail for the reason of issue #246 (the value is the placeholder text, or the layer is `command`). Record the failure output for the PR description.

### Implementation for User Story 1

- [ ] T012 [US1] In `src/GameBot.Domain/Services/SequenceRunner.cs`, `ExecuteSingleStepAsync`, command path: before `LogCommandStart` and before the `try` block, replace `scope.Child(ParameterScopeLayers.Command, originalStep.ParameterBindings, null)`. When `originalStep.ParameterBindings` is null or empty, use `commandScope = scope`. Else call `scope.TryBindChild(ParameterScopeLayers.Command, originalStep.ParameterBindings, out var bound, out var bindingError)` and use `bound` on success. Use `scope` (the step scope, the same one that `EvaluateStepGuardAsync` uses) as the receiver (plan D3). Also write the full `false` branch in this task (do not write a temporary empty branch): `var message = bindingError.ToMessage(stepKey);` then `result.AddStep(step.CommandId, appliedDelay, "Failed", conditionType, conditionResult: <"true" when the step has a condition, else null>, actionOutcome: "failed", message: message, stepId: step.StepId)`, `result.Fail(message)`, `stepOutcomes[stepKey] = "failed"`, and return `true` (early stop). Do not call the command dispatcher or `executeCommandAsync`. The dry-run path takes the same branch. Follow the shape of the current step guard failure for an unresolved `imageVisible` placeholder (FR-004).
- [ ] T013 [US1] In `src/GameBot.Service/Services/CommandExecutor.cs`, `ExecuteCommandRecursiveAsync`: for a step of type `Command` with bindings, replace `commandScope.Child(ParameterScopeLayers.Command, step.ParameterBindings, null)` with `commandScope.TryBindChild(...)`. On success, use the bound child scope for the recursive call. Also write the full `false` branch in this task (do not write a temporary empty branch): add `new PrimitiveTapStepOutcome(step.Order, "skipped_parameter_unresolved", error.ToMessage(<step order label>), null, null, StepType: step.Type.ToString())` to the step outcomes and `continue` without the recursive call. The next step of the calling command runs. Use the same label and outcome shape as the current unresolved placeholder path in a command step field (plan D4, FR-004a).
- [ ] T014 [US1] In `src/GameBot.Domain/Parameters/CommandStepResolver.cs`, change `RecordUsage` and `TryOverlayValue`: after the `ResolvedParameter` of the used name, when the `ParameterValue` has `Sources`, add each source item. Skip a source item when the used list already has an item with the same name (ordinal compare), so that a source item never gives a duplicate (plan D5, contract C4). Do not change the current behavior for a name that a field uses directly. When `Sources` is null, the used list does not change (plan D5, FR-008, SC-004). The third assert of T009 must pass.
- [ ] T015 [US1] Run `dotnet test "C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj" --filter "FullyQualifiedName~SequenceRunnerBindingScope|FullyQualifiedName~CommandStepResolver|FullyQualifiedName~ParameterScope"` and `dotnet test "C:\src\GameBot\tests\integration\GameBot.IntegrationTests.csproj" --filter "FullyQualifiedName~BindingPlaceholderScope|FullyQualifiedName~ParametrizedReferenceImage"`. T008, T009, and T010 must pass.

**Checkpoint**: The issue #246 reproduction gives the entry value with `originLayer` `entry`. US1 is complete and testable alone.

---

## Phase 4: User Story 2 - The current binding forms do not change (Priority: P1)

**Goal**: A step with no `parameterBindings`, a literal binding value, a `null` binding value, and a `${name}` value behave as before (FR-005, FR-006, FR-007, SC-003).

**Independent Test**: Run the two controls of the issue. The results are the same as before this feature.

### Tests for User Story 2 (these must pass before AND after the change)

- [ ] T016 [US2] In `tests/unit/Sequences/SequenceRunnerBindingScopeTests.cs`, add control tests: (a) a step with no `parameterBindings` and an entry value → the dispatcher scope is the step scope, and `novaOptionImage` resolves with layer `entry` (US2-1); (b) a literal binding `pns-todo-radar` → the value `pns-todo-radar` with layer `command` (US2-2); (c) a binding with a `null` value → the outer value and its layer (US2-3); (d) a binding `${novaOptionImage}` → the literal text `${novaOptionImage}` with layer `command` (FR-007). Depends on T008 (same file).
- [ ] T017 [US2] In `tests/integration/Commands/BindingPlaceholderScopeIntegrationTests.cs`, add queue-run control tests (SC-003): a step with no bindings → the log shows `novaOptionImage` / entry value / `entry`; a step with the literal binding `pns-todo-radar` → the log shows `novaOptionImage` / `pns-todo-radar` / `command`. Depends on T010 (same file).
- [ ] T018 [US2] In `tests/integration/Commands/BindingPlaceholderScopeIntegrationTests.cs`, add invariant tests for FR-009 and FR-010. These tests must pass before AND after the change. (a) FR-009: save a sequence with a step binding `novaOptionImage`=`{{undeclaredName}}`, where the sequence does not declare `undeclaredName` → the save is refused with the error code `unresolvable_parameter_reference`, with the same status code as before this feature. (b) FR-010: run the sequence of T010 Test 1 (binding `{{novaOptionImage}}`, no image upload, so the tap outcome is `not_executed`) and a copy with the literal binding `pns-alliance-nav-button` → the two runs have the same final sequence status. Compare the two runs; do not assert a fixed status, because the tap status can change between hosts. FR-010 is also covered by the current tests of the `not_executed` outcome and of the layer precedence, which must pass without change (T019, T030). Depends on T017 (same file).
- [ ] T019 [US2] Run the filters of T015 and confirm that T016, T017, and T018 pass on the new code. Also run the current sequence scope tests and parameter contract tests: `dotnet test "C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj" --filter "FullyQualifiedName~Parameter|FullyQualifiedName~Sequence"`. All must pass without a change to a current test (SC-004, FR-010).

**Checkpoint**: The controls give the same results as before. US1 and US2 work together.

---

## Phase 5: User Story 3 - An unresolved binding placeholder fails clearly (Priority: P2)

**Goal**: An unresolved binding placeholder fails the step with the current `ParameterResolutionError` plus a remediation hint. The command does not run, and no device input occurs (FR-004, FR-004a, SC-002, Constitution Principle III).

**Independent Test**: Run a sequence that declares `novaOptionImage` as `required: false` with no default. Its binding value is `{{novaOptionImage}}`, and no entry or run request supplies `novaOptionImage`. The step fails with the message that names `novaOptionImage` and `parameterBindings.<bindingName>`, and the message ends with the hint. The command does not run.

**Note**: T012 and T013 (US1) already contain the full `false` branches. Thus the US3 tests test that behavior, and they fail first only on the hint text until T023 is done.

The expected message in this phase (plan D7, contract C3):

```text
Step '<stepKey>': parameter 'novaOptionImage' used by field 'parameterBindings.novaOptionImage' could not be resolved from any scope. Supply a value for 'novaOptionImage' in the queue template entry or in the run request, give 'novaOptionImage' a default value in the sequence or in the calling command, bind a literal value, or bind a value in the calling command.
```

### Tests for User Story 3 (write first, see them fail)

- [ ] T020 [US3] In `tests/unit/Sequences/SequenceRunnerBindingScopeTests.cs`, add tests: (a) binding `novaOptionImage`=`{{novaOptionImage}}`, the sequence declares `novaOptionImage` as not required with no default, and no layer supplies a value → the step result has status `Failed`, `actionOutcome` `failed`, and the expected message above; the run fails with the same message; the dispatcher is not called; a later step does not run (US3-1); (b) the same case as a dry run gives the same failure. Depends on T016 (same file).
- [ ] T021 [US3] In `tests/integration/Commands/BindingPlaceholderScopeIntegrationTests.cs`, add tests: (a) a sequence that declares `novaOptionImage` as `required: false` with no default, run ad hoc with `POST /api/sequences/{id}/execute` and no `parameters` body → the step fails with the expected message above, and no execution log item has `{{novaOptionImage}}` as a resolved value (US3-2, SC-002). Do not use a required parameter: the run start refuses it with `missing_required_parameters`, and the step never runs. (b) a nested `Command` step whose binding `{{missing}}` does not resolve in the calling command scope → the nested step outcome is `skipped_parameter_unresolved` with the message (with the hint, which ends with "or bind a value in the calling command."), the nested command does not run, and the next step of the calling command runs (FR-004a, contract C3). The setup of (b) relies on a known gap: the command save does not check a binding value of a nested command step (`ParameterReferenceScanner.ScanCommandStep` does not scan `parameterBindings`), so the save of the calling command accepts `{{missing}}`, but the calling command does not declare `missing` (spec Assumptions). This feature does not close that gap. If a later feature adds that save check, change the setup to declare `missing` in the calling command as `required: false` with no default. Depends on T018 (same file).
- [ ] T022 [P] [US3] Create `tests/unit/Parameters/ParameterResolutionErrorTests.cs` (or add to the current test file of `ParameterResolutionError`, if one exists). Tests: (a) `new ParameterResolutionError("novaOptionImage", "parameterBindings.novaOptionImage", ParameterResolutionReasons.Unresolved).ToMessage("s1")` gives the expected message above, with the hint; (b) an unresolved error with the field path `primitiveTap.detectionTarget.referenceImageId` gives the current message with no hint; (c) a `not_a_number` error gives the current message with no hint (FR-004, Principle III, SC-004).

### Implementation for User Story 3

- [ ] T023 [US3] In `src/GameBot.Domain/Parameters/ParameterResolutionError.cs`, change `ToMessage` per plan D7: when `Reason` is `ParameterResolutionReasons.Unresolved` and `FieldPath` starts with `parameterBindings.` (ordinal compare), add the sentence ` Supply a value for '<ParameterName>' in the queue template entry or in the run request, give '<ParameterName>' a default value in the sequence or in the calling command, bind a literal value, or bind a value in the calling command.` after the current text. Both `<ParameterName>` items are the value of `ParameterName`. Do not change the other messages or the record shape. Update the XML doc (STE). Run T022: all cases must pass.
- [ ] T024 [US3] Run the unit and integration filters of T015, plus `FullyQualifiedName~ParameterResolutionError`. T020, T021, and T022 must pass, and all tests of US1 and US2 must still pass. Also update T010 Test 2 or any US1 assert on a binding error message, if one exists, to the message with the hint.

**Checkpoint**: All three user stories work and are tested.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, performance note, and the full test gate.

- [ ] T025 [P] In `src/GameBot.Domain/Parameters/ParameterBinding.cs`, update the XML doc of `Value` (STE): "A `{{name}}` placeholder in the value resolves against the scope outside the binding when the step runs. Only `{{name}}` is a placeholder. `${name}` is literal text. A `null` value inherits the outer value." (FR-007, plan D6).
- [ ] T026 [P] In `tests/unit/Performance/ParameterScopeBench.cs`, add one binding case: a scope of 5 layers and 20 names, and a `TryBindChild` call with one whole placeholder, one mixed value, and one literal. Assert or report that one call takes less than 50 µs on average (plan Performance Goals). Record the result for the PR perf note.
- [ ] T027 [P] In `docs/architecture.md`, Parameter section: add one bullet with the rule of plan D2 (a binding value resolves against the outer scope; whole placeholder keeps the origin layer; mixed text gets the layer `command` plus source items; unresolved gives `ParameterResolutionError` with field path `parameterBindings.<bindingName>` and a remediation hint; sequence step fails, nested command step gives `skipped_parameter_unresolved` and the calling command continues; only `{{name}}` is a placeholder). Refresh the "Last reviewed" date to 2026-09-29.
- [ ] T028 [P] In `CHANGELOG.md`, add a "Fixed" entry (STE): "A `{{name}}` placeholder in a step `parameterBindings` value now resolves against the sequence scope at run time. Before, the command received the placeholder as literal text (#246)."
- [ ] T029 [P] In `specs/STATUS.md`, add or update the row for feature 115 (`115-binding-placeholder-scope`, issue #246, B-029). In `specs/115-binding-placeholder-scope/spec.md`, change the `Status` line from `Draft` to `Implemented`.
- [ ] T030 Run the full gate: `dotnet build` of the solution with zero new warnings, then `dotnet test "C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj"`, `dotnet test "C:\src\GameBot\tests\integration\GameBot.IntegrationTests.csproj"`, and the contract test project under `C:\src\GameBot\tests\contract\`. All tests must pass (SC-004, Constitution NON-NEGOTIABLE gate). A known flaky test (for example `MaskedTemplateMatchTests` or a `QueueTemplateLink` test) gets one rerun before it counts as a failure.
- [ ] T031 Run the automated steps of `specs/115-binding-placeholder-scope/quickstart.md` section 1 and confirm the result. Check that each new code comment and message obeys STE (Principle VI).

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependency. Start immediately.
- **Foundational (Phase 2)**: Depends on Phase 1. BLOCKS all user stories. Order: T003 → T004 → T005 → T006 → T007.
- **US1 (Phase 3)**: Depends on Phase 2.
- **US2 (Phase 4)**: Depends on Phase 2. Its tests share files with US1 (T016 after T008; T017 after T010; T018 after T017), and they check the call sites that T012 and T013 change. Thus run it after US1.
- **US3 (Phase 5)**: Depends on T012 and T013 (US1), because they contain the `false` branches that the US3 tests check. Its tests share files with US2 (T020 after T016, T021 after T018). T023 adds the remediation hint.
- **Polish (Phase 6)**: Depends on all user stories.

### User Story Dependencies

- **US1 (P1)**: After Phase 2. No dependency on another story.
- **US2 (P1)**: After Phase 2 for the domain tests; after US1 for the call-site tests. It is a regression check of US1.
- **US3 (P2)**: After US1 (same call sites). It is independently testable with its own failure case.

### Within Each User Story

- Tests first. They must fail on the current code (US2 control and invariant tests must pass on both; US3 tests fail first on the hint text).
- Domain before call sites. Call sites before the log resolver check.
- Run the story filter before the next story.

### Parallel Opportunities

- T002 can run in parallel with T001.
- T008, T009, and T010 are in different files and can run in parallel.
- T016, T017, T018, T020, and T021 have no [P] mark, because each one depends on an earlier task in the same file. But after its same-file dependency is complete, the unit task and the integration task of a pair can run at the same time, because they are in different files: T016 with T017, and T020 with T021. T022 has the [P] mark (a new file) and can run in parallel with T020 and T021.
- T012 (Domain) and T013 (Service) are in different files, but T013 depends on the same domain method, so run them after T006. They can run in parallel with each other. T014 is in a third file and can also run in parallel with them.
- T025, T026, T027, T028, and T029 are in different files and can run in parallel.

---

## Parallel Example: User Story 1

```text
# Write the US1 tests together:
Task: "SequenceRunner binding scope tests in tests/unit/Sequences/SequenceRunnerBindingScopeTests.cs"          (T008)
Task: "Sources in the used list test in tests/unit/Parameters/CommandStepResolverTests.cs"                    (T009)
Task: "Queue-run reproduction of #246 in tests/integration/Commands/BindingPlaceholderScopeIntegrationTests.cs" (T010)

# Then change the three code sites together:
Task: "TryBindChild at the sequence step in src/GameBot.Domain/Services/SequenceRunner.cs"          (T012)
Task: "TryBindChild at the nested command step in src/GameBot.Service/Services/CommandExecutor.cs"  (T013)
Task: "Add Sources to the used list in src/GameBot.Domain/Parameters/CommandStepResolver.cs"        (T014)
```

## Parallel Example: Polish

```text
Task: "XML doc of ParameterBinding.Value"   (T025)
Task: "Binding case in ParameterScopeBench"  (T026)
Task: "docs/architecture.md bullet"          (T027)
Task: "CHANGELOG.md Fixed entry"             (T028)
Task: "specs/STATUS.md row and spec Status"  (T029)
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1 (baseline) and Phase 2 (`TryBindChild`).
2. Complete Phase 3 (US1): the issue #246 reproduction passes.
3. STOP and VALIDATE: the queue run shows `novaOptionImage=pns-alliance-nav-button` with `originLayer` `entry`.

### Incremental Delivery

1. Setup + Foundational → the scope method is ready and tested.
2. US1 → the defect is fixed at both call sites (MVP).
3. US2 → the controls are proven unchanged.
4. US3 → an unresolved binding fails clearly, with a remediation hint and no silent literal value.
5. Polish → docs, perf note, full test gate.

---

## Notes

- [P] tasks = different files, no dependency on an incomplete task. A task that depends on an earlier task in the same file has no [P] mark.
- Task count: 31 in total. Phase 1: 2 (T001-T002). Phase 2: 5 (T003-T007). US1: 8 (T008-T015). US2: 4 (T016-T019). US3: 5 (T020-T024). Polish: 7 (T025-T031).
- The `false` branches of `TryBindChild` at the two call sites are complete in T012 and T013. No task writes a temporary empty branch.
- Do not change `ParameterScope.Child` behavior: the entry layer and the ad-hoc run layer keep their literal values.
- Do not change the save rules (`unresolvable_parameter_reference`), the layer precedence, the stored JSON, the API shapes, or `Program.cs` (FR-009, FR-010).
- Do not add a `${name}` syntax (FR-007).
- New integration tests go in `tests/integration`, not in the contract project (the contract tests share one bin data directory).
- Edit UTF-8 files with the Edit/Write tools, not `Get-Content`/`Set-Content`.
