# Implementation Plan: Parameter Condition

**Branch**: `193-parameter-condition` | **Date**: 2026-11-19 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/193-parameter-condition/spec.md`

## Summary

Add the sequence condition kind `parameter` with the fields `name`, `operator` (`equals`, `notEquals`, `greaterThan`, `lessThan`), `value` (string or number), and `negate`. At run time, the condition reads the run value of a declared sequence parameter from the parameter scope. A `number` parameter compares as a whole number. A `text` parameter (kinds `text`, `choice`, `image`) compares as text, ordinal, case-insensitive. The save and the `dryRun` refuse an unknown name, a bad operator, no value, a value that is not a JSON string or a JSON number, a value that is not a whole number for a `number` parameter, and a multi-value parameter. The condition works in every condition slot and as a child of `all`, `any`, and `none`.

The technical approach (see [research.md](research.md)):

1. Domain: `ParameterStepCondition` and `ParameterConditionValue` (with a JSON converter that keeps the JSON kind), `ParameterConditionRules` (operators, shape checks, compare), `ParameterScope.TryResolveEntered`, the resolver case in `SequenceStepConditionResolver`, the evaluator case and the `Describe` text in `SequenceStepConditionEvaluator`, `SequenceRunner.ToFailureKind` for `not_a_number`, the shape check in `CompositeConditionValidator` and `FileSequenceRepository`, and the declaration check `AddParameterConditionIssues` in `ParameterValidationService` with two new codes.
2. Service: `ParameterConditionContract`, the code that maps the contract to the domain and back, and the value-kind request check in `SequencesEndpoints`, the OpenAPI registration in `ConditionalFlowSchemaDocumentFilter`, and the new `ParameterConditionSchemaFilter`.
3. Web UI: the type in `sequenceFlow.ts`, the controls in `ConditionFields.tsx`, the form map code in `SequencesPage.tsx`, and the rules in `validation.ts`.
4. Docs: `docs/architecture.md`, `CHANGELOG.md`, `specs/STATUS.md`, the spec status, and the version bump.

## Technical Context

**Language/Version**: C# 13 on .NET 9 (service, domain); TypeScript 5.6 with React 18 (web UI)  
**Primary Dependencies**: ASP.NET Core minimal APIs, Swashbuckle (OpenAPI), System.Text.Json; Vite, Jest, and Testing Library (web UI)  
**Storage**: JSON files of sequences (`FileSequenceRepository`). The new condition kind is a new discriminator. Stored sequences without a `parameter` condition do not change.  
**Testing**: xUnit with FluentAssertions (`tests/unit`, `tests/integration`, `tests/contract`); Jest (`src/web-ui`)  
**Target Platform**: Windows service; the web UI in a desktop browser  
**Project Type**: Web service with a web UI  
**Performance Goals**: One evaluation is one dictionary lookup in the scope and one compare: O(1), with no screen capture and no device input. No benchmark is necessary, because the cost is less than the cost of the current `matchedImage` leaf. A `parameter` child before an `imageVisible` child in an `all` composite can stop the evaluation before the screen capture. No hot path changes.  
**Constraints**: The current condition kinds and the `imageVisible` parameter behaviour do not change (FR-015). Stored sequences stay byte-for-byte the same.  
**Scale/Scope**: About 14 C# files, 5 web UI files, 1 new OpenAPI filter, about 8 test classes, and the docs.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

*NON-NEGOTIABLE*: All text in this plan and in the artifacts it produces (research, data model, contracts, quickstart, tasks, code comments, user-facing messages) MUST obey Simplified Technical English (Constitution Principle VI).

*Phone check (Constitution Principle III)*: The feature changes the condition fields of the sequence editor. The sequence editor is in the Authoring area, which is `desktop-only`. The phone support level of this feature is `desktop-only`. The new controls use the same `select` and `input` controls as the `matchedImage` and `commandStepOutcome` fields, and follow docs/mobile-ui-principles.md.

| Gate | Result | Note |
|------|--------|------|
| I. Code quality | Pass | Lint and analyzers MUST pass with 0 errors. Each new case goes into its own private method (for example `EvaluateParameter`, `ValidateParameter`, `TryResolveParameter`), so the switch methods stay small. Public members get XML comments. Method names use CamelCase with no underscores. |
| II. Testing | Pass | Unit tests for `ParameterConditionRules` (compare table, shape, JSON converter), the resolver, the evaluator (composites, `negate`, failures), the save checks, and `ParameterScope.TryResolveEntered`. Contract tests for the save, `dryRun`, the read, and the OpenAPI document. An integration test for a run of an `If` step. Jest tests for `ConditionFields` and `validation.ts`. New test classes go to the complement shard. Run `scripts/ci/measure-shards.ps1` if a shard gets more than a few new classes (shard balance rule). |
| III. UX consistency | Pass | Error messages use the `Step '<id>' condition at <path>:` prefix of the other conditions and name the field and the remedy. The OpenAPI document describes the new kind. Phone level: `desktop-only`. |
| IV. Performance | Pass | No hot path change. The CHANGELOG entry says "no performance effect on other conditions". |
| V. Living docs | Pass | `docs/architecture.md` gets the `parameter` condition kind, with a new "Last reviewed" date. Spec 193 gets a Status line and a row in `specs/STATUS.md`. No earlier spec is reworked. |
| VI. STE | Pass | All new text uses STE. |
| Version bump | Planned | Bump `patch` in `installer/versioning/version.override.json` before the push (CLAUDE.md rule). |

Post-design check: all gates stay Pass.

## Project Structure

### Documentation (this feature)

```text
specs/193-parameter-condition/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── parameter-condition.md   # API contract: shape, compare rules, save errors, run failures, log, OpenAPI
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 output (/speckit-tasks)
```

### Source Code (repository root)

```text
src/GameBot.Domain/Commands/SequenceStepCondition.cs           # ParameterStepCondition, discriminator "parameter"
src/GameBot.Domain/Commands/ParameterConditionRules.cs         # NEW: operators, ParameterConditionValue + converter, shape checks, compare
src/GameBot.Domain/Commands/FileSequenceRepository.cs          # last guard for the shape
src/GameBot.Domain/Parameters/ParameterScope.cs                # TryResolveEntered
src/GameBot.Domain/Parameters/SequenceStepConditionResolver.cs # resolve ParameterStepCondition (ResolvedValue, CompareAsNumber)
src/GameBot.Domain/Services/SequenceStepConditionEvaluator.cs  # EvaluateParameter, log line, Describe
src/GameBot.Domain/Services/SequenceRunner.cs                  # ToFailureKind: not_a_number gives ParameterValueInvalid
src/GameBot.Domain/Services/CompositeConditionValidator.cs     # root gate and ValidateParameter
src/GameBot.Domain/Services/ParameterValidationService.cs      # codes, AddParameterConditionIssues
src/GameBot.Service/Models/SequenceStepContracts.cs            # ParameterConditionContract
src/GameBot.Service/Endpoints/SequencesEndpoints.cs            # map to and from the domain, value-kind request check
src/GameBot.Service/Swagger/ConditionalFlowSchemaDocumentFilter.cs # generate and alias ParameterCondition
src/GameBot.Service/Swagger/ParameterConditionSchemaFilter.cs  # NEW: OpenAPI text, operator enum, value oneOf
src/GameBot.Service/GameBotServiceSetup.cs                     # register the filter
src/web-ui/src/types/sequenceFlow.ts                           # ParameterStepCondition type
src/web-ui/src/types/stepEntry.ts                              # conditionType 'parameter'
src/web-ui/src/components/sequences/ConditionFields.tsx        # parameter controls, parameterNames prop
src/web-ui/src/pages/SequencesPage.tsx                         # map the form fields, pass parameter names
src/web-ui/src/lib/validation.ts                               # shape and unknown-name rules
tests/unit/Sequences/ParameterConditionRulesTests.cs           # NEW
tests/unit/Sequences/ParameterConditionEvaluationTests.cs      # NEW: resolver + evaluator + composites + failures
tests/unit/Sequences/ParameterConditionValidationTests.cs      # NEW: shape + declarations
tests/unit/Parameters/ParameterScopeEnteredValueTests.cs       # NEW
tests/unit/Gamepacks/ParameterConditionGamepackTests.cs        # NEW: gamepack round trip (FR-016)
tests/contract/Sequences/ParameterConditionContractTests.cs    # NEW: save, dryRun, read, 400s
tests/contract/Sequences/ParameterConditionOpenApiTests.cs     # NEW
tests/integration/Sequences/ParameterConditionRunTests.cs      # NEW: run of an If step and a step condition
src/web-ui/src/components/sequences/__tests__/ParameterConditionFields.test.tsx # NEW
src/web-ui/src/lib/__tests__/parameterConditionValidation.spec.ts              # NEW
docs/architecture.md, CHANGELOG.md, specs/STATUS.md, installer/versioning/version.override.json
```

**Structure Decision**: The feature extends the current projects (`GameBot.Domain`, `GameBot.Service`, `src/web-ui`) and the current test projects. It adds 2 new source files, 1 new OpenAPI filter, and no new project.

## Complexity Tracking

No constitution violation. This section is empty.
