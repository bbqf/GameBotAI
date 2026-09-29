# Research: Resolve a placeholder in a step parameterBindings value

**Feature**: 115-binding-placeholder-scope | **Date**: 2026-09-29

The plan had no NEEDS CLARIFICATION items. This file records the facts that the code review found and the design decisions.

## Verified facts of the current code

1. `ParameterScope.Child` (`src/GameBot.Domain/Parameters/ParameterScope.cs`) copies each non-null binding value into `_values` as stored text. `TryResolve` then returns it with `scope.LayerName`. For a step binding, the layer is `command`. This is the defect of issue #246.
2. `SequenceRunner.ExecuteSingleStepAsync` (`src/GameBot.Domain/Services/SequenceRunner.cs`, command path) calls `scope.Child(ParameterScopeLayers.Command, originalStep.ParameterBindings, null)` and gives the result to `commandDispatcher` or `executeCommandAsync`.
3. `CommandExecutor.ExecuteCommandRecursiveAsync` (`src/GameBot.Service/Services/CommandExecutor.cs`) calls `commandScope.Child(ParameterScopeLayers.Command, step.ParameterBindings, null)` for a nested command step. `commandScope` holds the declarations of the calling command.
4. `ApplyScope` in `SequenceRunner` copies `ParameterBindings` without a change. Thus the binding value reaches `Child` as stored.
5. `CommandStepResolver.TryResolve` substitutes in one pass with `TemplateSubstitutor.TrySubstitute`. A value `{{novaOptionImage}}` that comes from the scope is not substituted again. This gives the log line "novaOptionImage={{novaOptionImage}}" with `originLayer` `command`, and then `template_not_found`.
6. `TemplateSubstitutor` knows only `{{name}}` (regex `\{\{(\w+(?:\.\w+)*)\}\}`). `${name}` is not a placeholder anywhere.
7. `ParameterScope.Child` is also used for the `entry` layer (queue run, ad-hoc run, template detail preview) and for the `sequence` and `command` declaration layers. These uses have no step bindings.
8. The step guard (`EvaluateStepGuardAsync`) resolves an `imageVisible {{name}}` leaf against the same `scope` argument that the command path uses (feature 114).
9. An unresolved placeholder in a command step field gives `ParameterResolutionError(name, fieldPath, "unresolved")`. `CommandExecutor` records it as the step outcome `skipped_parameter_unresolved` with `ToMessage`.

## Decisions

### R1. Where the resolution goes

- **Decision**: Add `ParameterScope.TryBindChild`. It resolves the binding values against the receiver and makes the new layer. Both runtime call sites call it.
- **Rationale**: The scope owns the resolution walk and the origin layer. One method for the two sites prevents a difference between them (clarification 3). The method is in the domain project, which both call sites already use.
- **Alternatives considered**:
  - Change `Child` itself. Rejected: `Child` also makes the `entry` layer from queue template values and ad-hoc run values. A change there would add placeholder resolution to entry values, which the spec does not ask for. Also `Child` cannot report an error.
  - Substitute the binding values in `ApplyScope`. Rejected: `ApplyScope` uses a flat string map, so the origin layer is lost (FR-003). It is also lenient and leaves unknown names in place (FR-004). It does not cover the nested command site.
  - A second pass in `CommandStepResolver` over the resolved values. Rejected: the pass would run in the command scope, where the binding shadows the outer name (FR-002). The origin layer would stay `command`.

### R2. How the origin layer survives

- **Decision**: `_values` stores a `ParameterValue` (text and origin layer) for each name. A whole-placeholder binding stores the resolved `ParameterValue` of the outer scope as it is.
- **Rationale**: `TryResolve` then returns the true origin (`entry`, `default`, `loop`, and so on) without a change to its walk. All current layers store their own `LayerName`, so their results do not change.
- **Alternatives considered**: A side dictionary of origin overrides in the child layer. Rejected: two maps for one fact.

### R3. What is a "whole placeholder"

- **Decision**: The value is exactly `{{key}}` for its one extracted key. The service does not trim the value.
- **Rationale**: Leading or trailing spaces are text around the placeholder, so the result must keep them. The rule then goes to the mixed path (FR-003a) and gives the layer `command`. No data is lost.
- **Alternatives considered**: Trim first, as `TryOverlayValue` does for an overlay. Rejected: an overlay value must be a whole placeholder, so a trim there loses nothing. A binding value is free text, so a trim would change the value.

### R4. Log of the placeholder names in a mixed value

- **Decision**: `ParameterValue` gets an optional `Sources` list of `ResolvedParameter`. The mixed path fills it. `CommandStepResolver` adds the sources after the entry of the name that the field uses.
- **Rationale**: Clarification 2 asks that the log records each placeholder name that the value used, with its layer. The command log item `details[kind=parameters]` is the current place for this data (feature 078, FR-024). When `Sources` is null, the log payload does not change.
- **Alternatives considered**: A new log detail kind for bindings. Rejected: new contract shape for one case, and the operator reads the `parameters` item today.

### R5. Failure shape

- **Decision**: Sequence step: the step fails with `actionOutcome` `failed` and the message `ParameterResolutionError.ToMessage(stepKey)`, and the run stops. Nested command step: the step outcome is `skipped_parameter_unresolved` with the same message, the nested command does not run, and the calling command continues with its next step. For a binding error only, `ToMessage` adds a remediation hint sentence (plan D7, Principle III).
- **Rationale**: Clarification 4 asks for the current parameter resolution error. The sequence form is the same as the step guard failure for an unresolved `imageVisible` placeholder. The nested form is the same as an unresolved placeholder in a command step field.
- **Alternatives considered**: Throw from the command dispatcher. Rejected: the sequence runner then records the generic "command execution failed" text, which does not name the field path.

### R6. `${name}` handling

- **Decision**: No change. `${name}` is literal text. The XML doc of `ParameterBinding.Value` and `docs/architecture.md` state that only `{{name}}` is a placeholder.
- **Rationale**: Clarification 1 and FR-007. A save rejection of `${` would be a new rule for one site only.

### R7. Save rules

- **Decision**: No change. The save check `unresolvable_parameter_reference` for a binding placeholder of a sequence step stays as it is (FR-009). A binding value of a nested command step has no save check today (`ParameterReferenceScanner.ScanCommandStep` does not scan `parameterBindings`). This feature does not add one. The run-time result of FR-004a applies. A save check for a nested binding value is a follow-up item.
- **Rationale**: The save check already accepts only the names that the run scope can supply. The run-time error of R5 covers the gaps that a static check cannot see, for example a declared name that is not required, has no default, and gets no entry or run value. (A required name with no value never gets to the step: the run start refuses it with `missing_required_parameters`.)

### R8. Performance

- **Decision**: `TryBindChild` makes the substitution map only when a mixed value occurs. A whole placeholder uses `TryResolve` directly. A binding list with no placeholder does no extra work beyond one regex test for each value.
- **Rationale**: The dispatch path runs for each command step of each firing. The scope has few layers and few names, so the cost is small. `ParameterScopeBench` gets one binding case for the perf note.
