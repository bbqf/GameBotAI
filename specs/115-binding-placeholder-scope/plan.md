# Implementation Plan: Resolve a placeholder in a step parameterBindings value against the sequence scope

**Branch**: `115-binding-placeholder-scope` | **Date**: 2026-09-29 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/115-binding-placeholder-scope/spec.md` (GitHub issue #246, B-029)

## Summary

At run time, the service puts each `parameterBindings` value into the command scope as stored text. `ParameterScope.Child` does not replace a `{{name}}` placeholder in the value. Thus the command reads `{{novaOptionImage}}` as its value, with the layer `command`.

The fix adds one new scope method, `ParameterScope.TryBindChild`. This method resolves each binding value against the receiver (the outer scope) before it makes the new `command` layer. A whole-placeholder value keeps the value and the origin layer of the layer that supplied it. A value with text around a placeholder gets each placeholder replaced and the layer `command`, and the scope records the placeholder names that the value used. An unresolved name gives a `ParameterResolutionError` with the field path `parameterBindings.<bindingName>` and the reason `unresolved`.

Two call sites change to use the new method: the command path of `SequenceRunner.ExecuteSingleStepAsync` (sequence step bindings) and `CommandExecutor.ExecuteCommandRecursiveAsync` (nested command step bindings). `ParameterScope.Child` does not change, so the entry layer and the ad-hoc run layer keep their literal values.

## Technical Context

**Language/Version**: C# 13 on .NET 9 (`net9.0`)
**Primary Dependencies**: ASP.NET Core minimal API (host only), xUnit, FluentAssertions, `WebApplicationFactory<Program>` for integration tests
**Storage**: File-based JSON under `data/`. This feature changes no stored shape.
**Testing**: `dotnet test` on `tests\unit\GameBot.UnitTests.csproj` and `tests\integration\GameBot.IntegrationTests.csproj`. The contract tests must stay green.
**Target Platform**: Windows service (GameBot.Service) that drives Android emulators over ADB
**Project Type**: Web service (backend) with a React web UI. The web UI does not change.
**Performance Goals**: The binding resolution runs once for each command step dispatch. It must add no measurable time: less than 50 µs for a scope of 5 layers and 20 names. A step with no placeholder in its bindings keeps the current cost (no substitution map).
**Constraints**: No change to the stored JSON, the API request or response shapes, the save rules, or the layer precedence. `Program.cs` does not change.
**Scale/Scope**: 1 domain class changes (`ParameterScope`), 1 record struct gets an optional member (`ParameterValue`), 2 call sites change, 1 resolver records the extra names (`CommandStepResolver`), 1 error record adds a hint for the binding case (`ParameterResolutionError.ToMessage`). Docs: `docs/architecture.md`, `CHANGELOG.md`, `specs/STATUS.md`.

There are no NEEDS CLARIFICATION items. `research.md` records the decisions.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

*NON-NEGOTIABLE*: All text in this plan and in the artifacts it produces (research, data model, contracts, quickstart, tasks, code comments, user-facing messages) MUST obey Simplified Technical English (Constitution Principle VI).

| Principle | Gate | Status |
|-----------|------|--------|
| I. Code Quality | New method is short (under 50 LOC) and has an XML doc comment with inputs, outputs, and the error mode. No new dependency. Method names use CamelCase, no underscores. | PASS |
| II. Testing | A failing test that reproduces issue #246 comes before the fix (unit test on `TryBindChild` and an integration test of the queue run). Unit tests cover FR-001 to FR-006. Integration tests cover the execution log contract (FR-008). | PASS (planned) |
| III. UX Consistency | The unresolved case uses the current `ParameterResolutionError.ToMessage` text and the current step failure shape. For a binding error only (`Reason` `unresolved` and `FieldPath` starts with `parameterBindings.`), `ToMessage` adds one remediation hint sentence (D7). The messages of all other errors do not change. No new error format. | PASS |
| IV. Performance | Goal declared above. The fast path (no placeholder in any binding value) does no substitution. A perf note goes in the PR. `tests\unit\Performance\ParameterScopeBench.cs` gets one binding case. | PASS |
| V. Living Documentation | `docs/architecture.md` (Parameter section and "Last reviewed" date), `CHANGELOG.md`, `specs/STATUS.md`, and the spec `Status` line change in the same PR. Feature 078 spec is not superseded: this is a defect fix inside its model. | PASS (planned) |
| VI. STE | All artifacts of this plan use STE. Code comments and the new doc text must use STE. | PASS |

No violation. The Complexity Tracking table is empty.

**Post-design re-check (after Phase 1)**: PASS. The design adds one method and one optional record member. It adds no project, no layer, and no API member.

## Project Structure

### Documentation (this feature)

```text
specs/115-binding-placeholder-scope/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── binding-resolution.md   # Runtime contract of a binding value
└── tasks.md             # Phase 2 output (/speckit-tasks, not made by this command)
```

### Source Code (repository root)

```text
src/GameBot.Domain/
├── Parameters/
│   ├── ParameterScope.cs          # CHANGE: _values holds ParameterValue; new TryBindChild; ParameterValue gets optional Sources
│   ├── CommandStepResolver.cs     # CHANGE: when a used value has Sources, add them to the used list (FR-008, clarification 2)
│   ├── ParameterResolutionError.cs # CHANGE: ToMessage adds a remediation hint for an unresolved parameterBindings.* field path only (FR-004, D7)
│   └── ParameterBinding.cs        # CHANGE: XML doc of Value states that only {{name}} is a placeholder (FR-007)
└── Services/
    └── SequenceRunner.cs          # CHANGE: command path uses scope.TryBindChild; an error fails the step before dispatch

src/GameBot.Service/
└── Services/
    └── CommandExecutor.cs         # CHANGE: nested command step uses commandScope.TryBindChild; an error gives skipped_parameter_unresolved

tests/unit/
├── Parameters/
│   ├── ParameterScopeBindingTests.cs        # NEW: TryBindChild rules (FR-001..FR-007)
│   ├── ParameterResolutionErrorTests.cs     # NEW or ADD: hint only for the binding case; other messages unchanged
│   └── CommandStepResolverTests.cs          # ADD: Sources of a mixed binding value reach the used list
├── Sequences/
│   └── SequenceRunnerBindingScopeTests.cs   # NEW: runner resolves bindings, fails on unresolved, dry run, guard parity
└── Performance/
    └── ParameterScopeBench.cs               # ADD: one binding case

tests/integration/
└── Commands/
    └── BindingPlaceholderScopeIntegrationTests.cs  # NEW: issue #246 reproduction through a queue run; nested command; unresolved; controls

docs/architecture.md        # CHANGE: Parameter section and "Last reviewed"
CHANGELOG.md                # CHANGE: Fixed entry for #246
specs/STATUS.md             # CHANGE: row for 115
```

**Structure Decision**: The fix stays in the current layered layout. The resolution rule goes in `GameBot.Domain/Parameters` next to `ParameterScope`, because the scope already owns the resolution walk and the origin layer. The two runtime call sites (`GameBot.Domain/Services/SequenceRunner.cs` and `GameBot.Service/Services/CommandExecutor.cs`) call the one domain method, so the two sites cannot become different.

## Design details

### D1. `ParameterScope` stores a `ParameterValue` for each bound name

- `_values` changes from `Dictionary<string, string>` to `Dictionary<string, ParameterValue>`.
- `Child`, `FromQueue`, and `WithIteration` store `new ParameterValue(text, LayerName)`. Their behavior does not change.
- `TryResolve` returns the stored `ParameterValue` as it is. For all current layers, this is the same value as before (`Text`, `scope.LayerName`).
- `ParameterValue` gets a third optional member: `IReadOnlyList<ResolvedParameter>? Sources = null`. Current code that makes a `ParameterValue` with two arguments does not change.

### D2. New method `ParameterScope.TryBindChild`

```csharp
public bool TryBindChild(
    string layerName,
    IEnumerable<ParameterBinding>? bindings,
    [NotNullWhen(true)] out ParameterScope? child,
    [NotNullWhen(false)] out ParameterResolutionError? error)
```

For each binding with a non-null name and a non-null value, in list order:

1. No `{{name}}` placeholder in the value (`TemplateSubstitutor.ContainsPlaceholder` is false): store `ParameterValue(value, layerName)`. This includes `${name}`, which stays literal (FR-005, FR-007).
2. The value is exactly one placeholder (`ExtractKeys` gives one key and `value == "{{key}}"`, with no trim): call `this.TryResolve(key)`. On success, store the resolved `ParameterValue` as it is (text, origin layer, sources). This gives `entry`, `sequence`, `queue`, `loop`, `command`, or `default` (FR-003).
3. The value has text around one or more placeholders: call `TemplateSubstitutor.TrySubstitute(value, this.ToSubstitutionContext())`. On success, store `ParameterValue(result, layerName, sources)`. `sources` holds one `ResolvedParameter` for each key, from `this.TryResolve(key)` (FR-003a, clarification 2). The method makes the substitution map one time, only when the first mixed value occurs.
4. A key does not resolve: set `error = new ParameterResolutionError(key, $"parameterBindings.{binding.Name}", ParameterResolutionReasons.Unresolved)`, set `child = null`, and return `false` (FR-004).

All lookups use `this` (the outer scope), never the new layer. Thus a binding never resolves against itself or against a different binding of the same step (FR-002). The declarations of the called command are not in `this`. Thus, when a placeholder is unresolved in `this` and the called command declares a default for the bound name, the method returns `false`, and the service does not use that default (spec Edge Cases). To use that default, the author removes the binding or sets its value to `null`. A binding with a `null` value is skipped, the same as in `Child` (FR-006). When no binding value has a placeholder, the result is equal to `Child(layerName, bindings, null)`.

### D3. `SequenceRunner` command path (sequence step bindings)

In `ExecuteSingleStepAsync`, before `LogCommandStart` and before the `try` block, replace the `scope.Child(...)` call:

- When `originalStep.ParameterBindings` is empty or null, `commandScope = scope` (no change).
- Else call `scope.TryBindChild(ParameterScopeLayers.Command, originalStep.ParameterBindings, out var bound, out var bindingError)`.
- On `false`: `result.AddStep(step.CommandId, appliedDelay, "Failed", conditionType, conditionResult: "true" when a condition exists, actionOutcome: "failed", message: bindingError.ToMessage(stepKey), stepId: step.StepId)`, then `result.Fail(message)`, set `stepOutcomes[stepKey] = "failed"`, and return `true` (early stop). The command dispatcher is not called, so no device input occurs (FR-004). A dry run takes the same path. The message includes the remediation hint of D7.

`scope` here is the step scope. It is the same scope that the step guard (`EvaluateStepGuardAsync`) uses for an `imageVisible {{name}}` leaf, and in a loop body it includes the `loop` layer. Thus FR-001 and acceptance scenario US1-4 hold.

### D4. `CommandExecutor` nested command step

In `ExecuteCommandRecursiveAsync`, for a step of type `Command` with bindings, call `commandScope.TryBindChild(...)`. On `false`, add `PrimitiveTapStepOutcome(step.Order, "skipped_parameter_unresolved", error.ToMessage(order), null, null, StepType: step.Type.ToString())` and `continue`, without the recursive call. The nested command does not run, and the next step of the calling command runs. This is the current result of an unresolved placeholder in a command step (FR-004a, clarification 4). It is not the "step fails" result of a sequence step. The message includes the remediation hint of D7. `commandScope` holds the declarations of the calling command, so a binding placeholder can also resolve to a default of the calling command.

### D5. `CommandStepResolver` records the sources of a mixed binding

`RecordUsage` and `TryOverlayValue` add one `ResolvedParameter` for the name that the field uses. When the `ParameterValue` has `Sources`, they also add each source entry after it. Thus the `details[kind=parameters]` item shows the composed value with the layer `command` and each placeholder name that it used, with its layer (FR-008). When `Sources` is null, the log payload does not change (SC-004).

No duplicate source item: before the resolver adds a source item, it looks for an item with the same name in the used list (ordinal compare). When the used list already has that name (for example, a different field of the step uses the name `option` directly), the resolver does not add the source item. Thus a source item never gives a second item for a name, and K does not count that name two times. This rule applies only to source items. The current behavior for a name that a field uses directly does not change.

The count K in the log message "Step N resolved K parameter(s)" is the number of items in the used list. `ExecutionLogService` already counts the list, so it does not change. For a mixed value, K includes the source items. Example: binding `novaOptionImage` = `nova-{{option}}` with entry `option = b` gives "Step 0 resolved 2 parameter(s): novaOptionImage=nova-b, option=b" (contract C4).

### D6. Documentation

- `ParameterBinding.Value` XML doc: a `{{name}}` placeholder in the value resolves against the scope outside the binding when the step runs. Only `{{name}}` is a placeholder. `${name}` is literal text.
- `docs/architecture.md`, Parameter section: add one bullet with the rule of D2 and the failure shape. Refresh "Last reviewed".
- `CHANGELOG.md`: a "Fixed" entry for #246.

### D7. Remediation hint for an unresolved binding (Principle III)

`ParameterResolutionError.ToMessage(stepLabel)` adds one sentence after the current unresolved text when `Reason` is `ParameterResolutionReasons.Unresolved` and `FieldPath` starts with `parameterBindings.`:

```text
Supply a value for '<name>' in the queue template entry or in the run request, give '<name>' a default value in the sequence or in the calling command, bind a literal value, or bind a value in the calling command.
```

The hint names the parameter and the place of the default: a declaration in the sequence (sequence step) or in the calling command (nested command step). A default that the called command declares is not in the scope of the resolution (D2), so the hint does not tell the operator to use it.

The hint is the same for a sequence step and for a nested command step. The last part ("bind a value in the calling command") is for a nested command step: there, the usual fix is a binding or a declaration in the calling command.

The `not_a_number` message and the unresolved message for all other field paths do not change. Thus the current tests and the API contract of a command step field stay the same (SC-004). The record shape does not change.

## Test plan

Principle II: write the reproduction tests first and see them fail on the current code.

| Test | Level | Covers |
|------|-------|--------|
| `TryBindChild` whole placeholder gets the entry value and `originLayer` `entry` | unit | FR-001, FR-003, US1-1 |
| Same-name binding (`x` = `{{x}}`) resolves outward, not against itself | unit | FR-002 |
| Binding `a` = `{{b}}` gets the value of `b`; a second binding of the same step is not visible | unit | FR-002, edge case |
| Placeholder resolves to a declared default: `originLayer` `default` | unit | edge case |
| `{{iteration}}` under `WithIteration(3)` gives `3` with layer `loop` | unit | edge case |
| `nova-{{option}}` gives `nova-b`, layer `command`, `Sources` = [`option`/`entry`] | unit | FR-003a |
| Literal `pns-todo-radar`, `${x}`, and empty string stay literal with layer `command` | unit | FR-005, FR-007 |
| `null` value is skipped (inherit) | unit | FR-006 |
| Unresolved `{{missing}}` gives the error `missing` / `parameterBindings.novaOptionImage` / `unresolved` and no child | unit | FR-004 |
| Unresolved `{{novaOptionImage}}` where only the called command declares a default for `novaOptionImage`: the step fails and the default is not used | unit | FR-004, edge case |
| `CommandStepResolver` adds `Sources` entries to the used list, and skips a source entry whose name is already in the list | unit | FR-008, D5 |
| `ParameterResolutionError.ToMessage` adds the hint for an unresolved `parameterBindings.*` field path only; other messages unchanged | unit | FR-004, Principle III |
| Save of a binding `{{undeclared}}` still gives `unresolvable_parameter_reference` | integration | FR-009 |
| A `not_executed` tap outcome with a resolved binding keeps the current final sequence status | integration | FR-010 |
| Mixed binding: log message count is 2 ("Step 0 resolved 2 parameter(s)") | integration | FR-003a, contract C4 |
| Nested unresolved binding: `skipped_parameter_unresolved`, nested command does not run, next caller step runs | integration | FR-004a |
| `SequenceRunner`: dispatcher receives a scope where `novaOptionImage` resolves to the entry value with layer `entry` | unit | FR-001, US1 |
| `SequenceRunner`: unresolved binding fails the step, the run fails, the dispatcher is not called, message names the parameter and the field path | unit | FR-004, US3-1 |
| `SequenceRunner`: dry run gives the same resolution and the same failure | unit | edge case |
| `SequenceRunner`: step guard `imageVisible {{novaOptionImage}}` and binding `{{novaOptionImage}}` use the same value | unit | US1-4 |
| `SequenceRunner`: no bindings and literal binding controls unchanged | unit | FR-005, FR-006, US2 |
| Queue run of issue #246 reproduction: log item `parameters` shows `novaOptionImage=pns-alliance-nav-button` with `"entry"`, and the tap step uses that image id | integration | SC-001, US1-2, US1-3 |
| Nested command binding `{{novaOptionImage}}` in `ZZZ.Outer` gets the caller value: a queue entry supplies the value, a sequence step with no bindings runs `ZZZ.Outer`, and the nested command log shows the value with `originLayer` `entry` | integration | FR-004a |
| Unresolved binding in an ad-hoc run (`novaOptionImage` declared `required: false`, no default, no value): no command log entry with the text `{{novaOptionImage}}` as a value; the sequence step fails with the error and the hint | integration | US3-2, SC-002 |
| Controls: no bindings (layer `entry`) and literal binding (layer `command`) | integration | SC-003 |
| All current `ParameterScopeTests`, `CommandStepResolverTests`, sequence scope tests, and parameter contract tests pass without change | all | SC-004 |

Test notes (known harness facts):

- Integration tests use `[Collection("ConfigIsolation")]` and `TestEnvironment.PrepareCleanDataDir()`, as `ParametrizedReferenceImageIntegrationTests` does. Contract tests share one bin data directory, so the new tests do not go in the contract project.
- The tests use no image upload. They assert the resolved value in the execution log, not the tap status, because the tap status can be different on different hosts.

## Complexity Tracking

No violation of the constitution. This table is empty.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
