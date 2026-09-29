# Implementation Plan: Let a parameter choose the reference image

**Branch**: `ccr-843497ee-pjzcth` (spec number 114) | **Date**: 2026-09-28 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/114-parametrized-reference-image/spec.md` (GitHub issue #243)

## Summary

The service rejects the `fieldTemplates` key `primitiveTap.detectionTarget.referenceImageId`, because `CommandStepFieldPaths` holds only numeric paths (research R-001). An `imageVisible` condition cannot take a parameter: the sequence save looks for an image with the literal id `{{name}}`, and the runner does not substitute conditions (R-006, R-007). No save check looks at a template entry value that goes to an image field (R-008).

The plan adds two image keys to `CommandStepFieldPaths`, with a save rule "one whole placeholder" (new code `invalid_field_template_value`). `CommandStepResolver` lets the overlay value replace the inline image id. The reference scanner marks the image keys and each `imageVisible.imageId` in each condition position as "defeats the static check". A new `SequenceStepConditionResolver` substitutes the condition image ids against the scope of the step before each evaluation. `POST /api/queue-templates` checks each known value that goes to an image field (new code `unknown_image_reference`). Sequence write responses show the parameter warnings. Swagger, `specs/openapi.json`, `docs/architecture.md`, `CHANGELOG.md` and `specs/STATUS.md` tell the new rules (R-010).

## Technical Context

**Language/Version**: C# 13 (`LangVersion` preview) on .NET 9 (`net9.0`); web UI TypeScript (comment change only)  
**Primary Dependencies**: ASP.NET Core minimal APIs, Swashbuckle, xUnit, FluentAssertions  
**Storage**: File-backed JSON repositories for commands, sequences, queue templates and images. The stored format does not change.  
**Testing**: xUnit unit tests (`tests/unit`), contract tests with `WebApplicationFactory<Program>` (`tests/contract`), integration tests (`tests/integration`)  
**Target Platform**: Windows service (CI on `windows-latest`). The changed code has no platform dependency. An image id with no image gives the current missing-image result of the step type: a `WaitForImage` step gives `image_unavailable`, and its step detail shows the resolved id; a `PrimitiveTap` step gives `skipped_invalid_config` (`template_not_found` on Windows, `primitive_tap_detection_windows_only` on other hosts). For a tap step, the tests read the resolved value from the `parameters` log item.  
**Project Type**: Web service with a web UI (the web UI gets no new controls)  
**Performance Goals**: No measurable change on the run path. The condition resolver returns the same instance when no leaf has a placeholder. A template save reads each referenced sequence and its reachable commands one time for each entry, and checks each distinct image id one time.  
**Constraints**: Numeric `fieldTemplates` keys keep their behavior (FR-013). Inline placeholders keep their behavior (FR-004). Current tests pass with no change (SC-005), except a test that compares the old text "numeric field" (none found). Break conditions keep the error rule of feature 066 FR-002a and FR-010.  
**Scale/Scope**: About 10 production files in `GameBot.Domain` and `GameBot.Service`, 1 new Swagger filter, 4 new test files and 3 extended test files, docs.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

*NON-NEGOTIABLE*: All text in this plan and in the artifacts it produces (research, data model, contracts, quickstart, tasks, code comments, user-facing messages) MUST obey Simplified Technical English (Constitution Principle VI).

| Gate | Status | Note |
|------|--------|------|
| I. Code quality (analyzers, `-warnaserror`, CamelCase method names) | Pass | New code follows the style of feature 078. The condition resolver is a separate class, so `SequenceRunner` does not get larger methods. Public members get doc comments. |
| II. Testing (unit + contract + integration, tests first) | Pass | Unit tests for the key set, the value rule, the resolver overlay, the scanner, the condition resolver and the candidate search. Contract tests for each new code and warning. Integration test for the resolved id at run time. Each test fails before the change. |
| III. UX / API consistency | Pass | New codes use the current error body `{ error, message, details }`. Messages name the step or entry, the field and the parameter, and tell the fix. The sequence `warnings` member uses the command warning shape. |
| IV. Performance | Pass | Fast path on the run path. Template save cost is bounded by entries × reach (R-011). |
| V. Living docs | Pass | `docs/architecture.md` (parameters section, API list, "Last reviewed"), `CHANGELOG.md`, `specs/STATUS.md` (row 114), spec `Status` line. Feature 078 stays "Implemented": this feature adds to it and does not rework it. |
| VI. STE | Pass | All new text, messages and comments in STE. |
| Backward compatibility | Pass | Stored JSON does not change. Only the text of `unknown_field_template_path` changes. A template save that names a missing image through an image field now gets 400; before, it failed only at run time. |

Post-design re-check: Pass. No violations.

## Design

### A. Image keys in `fieldTemplates` (FR-001, FR-002, FR-012, FR-013)

- `src/GameBot.Domain/Commands/CommandStep.cs`: `CommandStepFieldPaths` gets `SupportedImagePaths` and `IsImagePath(path)`. `IsSupported` covers both sets. Doc comments of `CommandStep.FieldTemplates` and `CommandStepFieldPaths` no longer say "numeric only".
- `src/GameBot.Domain/Services/ParameterValidationService.cs`: new constants `ParameterValidationCodes.InvalidFieldTemplateValue` (`invalid_field_template_value`) and `UnknownImageReference` (`unknown_image_reference`). `ValidateFieldTemplateKeys` uses the new message for an unknown key and checks the whole-placeholder rule for image keys (R-002).
- `src/GameBot.Service/Models/Commands.cs`: comment on `CommandStepDto.FieldTemplates`.

### B. Resolution at dispatch (FR-003, FR-004, FR-008, FR-011)

- `src/GameBot.Domain/Parameters/CommandStepResolver.cs`: `TryDetection` uses the overlay value for `{prefix}.referenceImageId` when the key is present, and does not resolve the inline value then (R-003). `TryOverlayValue` gets a flag for a text field, so a malformed stored value gives the reason `unresolved` and not `not_a_number`. `CommandExecutor` needs no change: it already records `usedParameters`, and a resolved id with no image gets the current missing-image result of the step type (FR-009).

### C. Save scan (FR-005, FR-006)

- `src/GameBot.Domain/Parameters/ParameterReferenceScanner.cs`: `ParameterReference.SourceText`; the `fieldTemplates` loop sets `defeatsStaticCheck` for image keys; `ScanSequenceSteps` scans each `imageVisible` leaf in `Condition`, `If.Condition`, `Loop` (while and repeat-until) `Condition` and `BreakCondition`, also in composites (R-005).
- `src/GameBot.Service/Endpoints/SequencesEndpoints.cs`: `ValidatePerStepImageReferencesAsync` skips an `imageId` with a placeholder (R-006). `ToSequenceResponseAsync` gets an optional `warnings` argument. The create, update and patch routes pass `parameterCheck.Warnings` only for a per-step-shape body; an old-shape body and a domain-shape body pass `null`, so their responses do not change (R-009). The dry-run response does not change and has no `warnings` member.

### D. Condition resolution at run time (FR-005, FR-007, FR-008, FR-009)

- New `src/GameBot.Domain/Parameters/SequenceStepConditionResolver.cs` (R-007).
- `src/GameBot.Domain/Services/SequenceStepConditionEvaluator.cs`: new `ConditionEvaluationFailureKind.ParameterUnresolved`.
- `src/GameBot.Domain/Services/SequenceRunner.cs`: `EvaluateStepGuardAsync` and `EvaluateLoopConditionAsync` get a `ParameterScope` argument and resolve first. `SequenceStepConditionResolver.TryResolve` gets a `fieldPathPrefix` argument. The six call sites pass a scope and this prefix (data-model.md table): action-step guard and loop-step guard `condition` with the scope of the step (`scope`, or `iterScope` in a loop body); while `loop.condition` with `scope.WithIteration(iterations + 1)` (the iteration that is about to run, because the condition runs before `iterCtx` exists in `ExecuteWhileLoopAsync`); repeat-until `loop.condition` with `iterCtx` (the iteration that just ran); `If` `if.condition` with the scope of the step; break `breakCondition` with `iterScope`. `FailGuard` maps the new kind to the exact resolution message. The while, repeat-until and `If` paths keep their current message prefix (`Loop '<key>' condition evaluation failed: `, `Loop '<key>' exit condition evaluation failed: `, `If '<key>' condition evaluation failed: `) in front of the resolution message. The `If` and break paths compute their log description from the resolved condition.

### E. Template entry save check (FR-010)

- `src/GameBot.Domain/Services/ParameterValidationService.cs`: new record `ImageValueCandidate` and method `FindImageValueCandidates` (R-008). The method rebuilds each call path (sequence step, command, nested command steps) from the sequence and the `Command` objects. On each path, a non-null binding at a call site covers the name; else the entry value applies; else the default of the innermost declaration layer outward. It checks each field that the scanner marks `DefeatsStaticCheck`, also an inline `ensureGameRunning.readinessImage.referenceImageId` placeholder.
- `src/GameBot.Service/Endpoints/QueuesEndpoints.cs`: new `CollectReachableCommandsAsync`; `CollectReachableDeclarationsAsync` uses it.
- `src/GameBot.Service/Endpoints/QueueTemplatesEndpoints.cs`: the save route gets `ICommandRepository` and `IImageRepository`, runs the check after the value name check and before the save, and returns `400` with `ParameterDtoMapper.ToErrorBody`.

### F. Documentation (FR-014)

- New `src/GameBot.Service/Swagger/ParametrizedReferenceImageSchemaFilter.cs`, registered in `src/GameBot.Service/GameBotServiceSetup.cs` next to `PrimitiveTapHoldSchemaFilter`.
- `specs/openapi.json`: `CommandStepDto.fieldTemplates` member and descriptions; `ImageVisibleCondition(.Contract).imageId` descriptions; the template save description.
- `docs/architecture.md`, `CHANGELOG.md`, `specs/STATUS.md`, spec `Status` line.
- `src/web-ui/src/services/commands.ts`: comment on `fieldTemplates`.

### Test plan

| Test file | Kind | Covers |
|-----------|------|--------|
| `tests/unit/Parameters/CommandStepResolverTests.cs` (extend) | unit | overlay image value wins; inline placeholder unchanged; unresolved overlay fails; used value recorded |
| `tests/unit/Parameters/ParameterReferenceScannerTests.cs` (extend) | unit | image keys flagged; condition leaves in each position and in composites; `insideLoop` for loop and break conditions; `SourceText` |
| `tests/unit/Parameters/SequenceStepConditionResolverTests.cs` (new) | unit | fast path; leaf; composite; negate kept; unresolved and empty result |
| `tests/unit/Parameters/ParameterValidationImageTests.cs` (new) | unit | key set; whole-placeholder rule; new message text; numeric keys unchanged; `FindImageValueCandidates` (entry value, default order along the path, bindings at each call site, one command on two paths, readiness image, built-in skipped, mixed use, duplicates) |
| `tests/unit/Sequences/SequenceRunnerConditionScopeTests.cs` (new) | unit | step guard, loop guard, `If`, while, repeat-until and break resolve the id from the scope of each call site; `{{iteration}}` in a while condition and in a repeat-until condition; unresolved fails the step (break: "No break"); description shows the resolved id |
| `tests/contract/Sequences/ParametrizedReferenceImageContractTests.cs` (new) | contract | command save with both keys and warnings; `invalid_field_template_value`; `unknown_field_template_path` text; sequence save with placeholder and `warnings`; `unresolvable_parameter_reference`; literal id check unchanged |
| `tests/contract/QueueTemplates/TemplateImageReferenceContractTests.cs` (new) | contract | `unknown_image_reference` for an entry value and for a default; success for an existing image; built-in not checked; a step binding overrides the entry value |
| `tests/contract/Sequences/ParametrizedReferenceImageOpenApiTests.cs` (new) | contract | Swagger descriptions of `fieldTemplates`, `imageId` and the template save |
| `tests/integration/Commands/ParametrizedReferenceImageIntegrationTests.cs` (new) | integration | a run with two values gives the `parameters` log item each time, and the resolved image id on the `WaitForImage` step; a missing image gives the current result of each step type with no device input |

## Project Structure

### Documentation (this feature)

```text
specs/114-parametrized-reference-image/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── api.md           # API contract of the changes
├── checklists/
│   └── requirements.md  # Spec quality checklist
└── tasks.md             # Phase 2 output (/speckit-tasks)
```

### Source Code (repository root)

```text
src/GameBot.Domain/
├── Commands/CommandStep.cs                          # CommandStepFieldPaths: image keys
├── Parameters/CommandStepResolver.cs                # overlay value for referenceImageId
├── Parameters/ParameterReferenceScanner.cs          # SourceText, image keys, condition leaves
├── Parameters/SequenceStepConditionResolver.cs      # new
├── Services/ParameterValidationService.cs           # new codes, value rule, FindImageValueCandidates
├── Services/SequenceStepConditionEvaluator.cs       # ParameterUnresolved kind
└── Services/SequenceRunner.cs                       # resolve conditions against scope

src/GameBot.Service/
├── Endpoints/SequencesEndpoints.cs                  # skip placeholder image ids; warnings in responses
├── Endpoints/QueueTemplatesEndpoints.cs             # unknown_image_reference check
├── Endpoints/QueuesEndpoints.cs                     # CollectReachableCommandsAsync
├── Models/Commands.cs                               # comment
├── Swagger/ParametrizedReferenceImageSchemaFilter.cs  # new
└── GameBotServiceSetup.cs                           # register filter

src/web-ui/src/services/commands.ts                  # comment

tests/unit/Parameters/…                              # see test plan
tests/unit/Sequences/SequenceRunnerConditionScopeTests.cs
tests/contract/Sequences/ParametrizedReferenceImage*Tests.cs
tests/contract/QueueTemplates/TemplateImageReferenceContractTests.cs
tests/integration/Commands/ParametrizedReferenceImageIntegrationTests.cs

docs/architecture.md
specs/openapi.json
CHANGELOG.md
specs/STATUS.md
```

**Structure Decision**: The change stays in the parameter code of feature 078 (`GameBot.Domain/Parameters`, `ParameterValidationService`) and in the three endpoint classes that already call it. The condition resolver is a new class beside `CommandStepResolver`, so the two resolvers have the same form. No new project.

## Complexity Tracking

No violations. No entries.
