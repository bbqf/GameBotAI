# Implementation Plan: If-Then-Else Conditions in Sequences

**Branch**: `067-sequence-if-conditions` | **Date**: 2026-07-06 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/067-sequence-if-conditions/spec.md`

## Summary

Add an `If` block step type to sequences: a condition (same model as while-loop conditions — `imageVisible` / `commandOutcome`, with negation), an optional then branch, and an optional else branch. Branches behave exactly like loop bodies. If blocks are allowed at the sequence top level and inside loop bodies; branches themselves are flat (no loops, no nested ifs). The sequence editor gets an `IfBlock` component visually parallel to `LoopBlock`, and the add-step column currently labelled "Loop" becomes "Loops and Conditions" with an "If" button after the loop buttons.

Implementation reuses the loop machinery end to end: `SequenceRunner.EvaluateLoopConditionAsync` for condition evaluation, `ExecuteLoopBodyAsync` for branch execution (giving break propagation, `{{iteration}}` substitution, and inter-step delays for free), the `Body` property for the then branch, and the loop validation/contract/mapping patterns for the new step type.

## Technical Context

**Language/Version**: C# / .NET 8 (backend), TypeScript + React 18 + Vite (web UI)
**Primary Dependencies**: ASP.NET Core minimal APIs (`GameBot.Service/Program.cs`), System.Text.Json polymorphic contracts, @dnd-kit (editor drag-drop), jest + @testing-library/react
**Storage**: JSON files via `FileSequenceRepository` (domain `CommandSequence` serialized with System.Text.Json)
**Testing**: xUnit (`tests/unit`, e.g. `SequenceRunnerLoopTests.cs`), jest (`src/web-ui`)
**Target Platform**: Windows service + browser web UI
**Project Type**: Web application (C# backend `src/GameBot.*`, frontend `src/web-ui`)
**Performance Goals**: Condition evaluation cost identical to existing while-loop condition checks (one image detection or dictionary lookup per if block encounter); no hot-path or allocation-profile changes beyond one extra `StepResult` per executed if block
**Constraints**: Existing sequences must round-trip unchanged (new JSON properties are optional/absent for old data); web-ui quality gate is `vite build` + `jest` (lint/tsc have pre-existing failures)
**Scale/Scope**: ~6 backend files (domain model, runner, validation, contracts, Program.cs mapping, execution-log service), ~8 web-ui files (types, IfBlock components, SequencesPage mapping/rendering/DnD, executionLogsApi, tests), plus docs

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

- **I. Code Quality**: PASS — new step type follows existing Loop/Break patterns; no new dependencies; public domain members get XML docs like `LoopConfig`'s.
- **II. Testing Standards**: PASS — plan includes xUnit runner/validation/mapping tests mirroring `SequenceRunnerLoopTests`, and jest tests mirroring `LoopBlock.test.tsx`. Known pre-existing web-ui lint/tsc failures are documented; gate is `vite build` + `jest`.
- **III. UX Consistency**: PASS — the feature's core requirement is visual/behavioural parity with loop blocks; error messages follow existing validation phrasing.
- **IV. Performance**: PASS — no hot-path change; one condition evaluation per if encounter, same primitive as loop conditions (perf note above).
- **V. Living Documentation**: PASS (required work identified) — `docs/architecture.md` domain model + API surface sections must be updated in the same PR with refreshed "Last reviewed"; spec 067 gets `Status: Implemented` at completion and `specs/STATUS.md` updated. Spec 034/042 statuses are unaffected (this feature adds to, does not supersede, loop behaviour).

**Post-design re-check**: PASS — design introduces no constitution violations; Complexity Tracking is empty.

## Project Structure

### Documentation (this feature)

```text
specs/067-sequence-if-conditions/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── sequences-api.md # If-step API contract (upsert + read + execution log shapes)
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
src/
├── GameBot.Domain/
│   ├── Commands/
│   │   ├── SequenceStep.cs              # + SequenceStepType.If, If config, ElseBody
│   │   └── IfConfig.cs                  # NEW: condition holder for if blocks
│   └── Services/
│       ├── SequenceRunner.cs            # + ExecuteIfStepAsync, dispatch in single-step + loop body
│       └── SequenceStepValidationService.cs  # + ValidateIfStep, allow If in loop bodies
├── GameBot.Service/
│   ├── Models/SequenceStepContracts.cs  # + IfConfigContract, If/ElseBody on step contract
│   ├── Program.cs                       # + ParseStepType "if", MapToLinearSteps/MapStepToDto/MapBodySteps/Flatten
│   └── Services/
│       ├── SequenceExecution/SequenceExecutionService.cs  # + if-step detail items
│       └── ExecutionLog/ExecutionLogService.cs            # + MapStepKind "if"
└── web-ui/src/
    ├── types/stepEntry.ts               # + IfStepEntry
    ├── types/sequenceFlow.ts            # + IfConfigDto, if/elseBody on SequenceLinearStep
    ├── components/sequences/
    │   ├── IfBlock.tsx                  # NEW: mirrors LoopBlock (then/else areas, Add else)
    │   ├── IfBlockHeader.tsx            # NEW: If badge + shared condition fields
    │   ├── ConditionFields.tsx          # NEW: condition editor extracted from LoopBlockHeader
    │   └── LoopBlock.tsx                # + render nested IfBlock in body, "If" add button
    ├── services/executionLogsApi.ts     # + 'if' node kind
    └── pages/SequencesPage.tsx          # + mapping, rendering, DnD scopes, "Loops and Conditions"

tests/
└── unit/Sequences/
    ├── SequenceRunnerIfTests.cs         # NEW
    └── SequenceStepValidationServiceTests (extended)

src/web-ui/src/components/sequences/__tests__/IfBlock.test.tsx  # NEW
src/web-ui/src/pages/__tests__/SequencesPage.spec.tsx           # extended
```

**Structure Decision**: Existing web-application layout — C# backend projects under `src/GameBot.*` with xUnit tests under `tests/unit`, React frontend under `src/web-ui`. No new projects.

## Key Design Decisions (details in research.md / data-model.md)

1. **Then branch reuses `SequenceStep.Body`** (the loop-body property); a new nullable `ElseBody` list holds the else branch (`null` = else absent, distinguishing "no else" from "empty else" for editor round-trip). `MapStepToDto` already serializes `body` generically.
2. **New `IfConfig` class** holding the required `SequenceStepCondition Condition` — mirrors the `LoopConfig` pattern (JSON property `if` on the step, no polymorphism needed).
3. **Runner**: `ExecuteIfStepAsync` evaluates the condition once via existing `EvaluateLoopConditionAsync` (identical semantics and error handling as while loops → SC-003 parity by construction), then runs the taken branch through existing `ExecuteLoopBodyAsync` (break propagation, `{{iteration}}` substitution, inter-step delays for free). Dispatched from `ExecuteSingleStepAsync` (top level) and `ExecuteLoopBodyAsync` (inside loops, so a branch break exits the enclosing loop).
4. **Step outcome map**: then/else executed → `success`; no branch taken → `skipped` (mirrors zero-iteration while); condition error or branch failure → `failed`. A later `commandOutcome` referencing a never-executed branch step fails evaluation exactly like today's unavailable references (resolves the deferred clarify item by reusing existing semantics).
5. **Execution log**: the if step is recorded as a `StepResult` (conditionType, conditionResult `true|false`, actionOutcome `then|else|none`) *before* its branch steps, so history reads "if evaluated → branch steps". Detail items get `stepType: "if"`; `ExecutionLogService.MapStepKind` and the web-ui `ExecutionTreeNodeKind` gain `'if'`.
6. **Validation**: `ValidateIfStep(insideLoop)` mirrors `ValidateLoopStep` — branch steps get the same checks as loop-body steps; `Loop`/`If` steps inside branches rejected (flat rule); `Break` inside a branch allowed only when the if block itself is inside a loop body; `ValidateLoopStep` body scan additionally accepts `If` children.
7. **Web UI**: condition editor extracted from `LoopBlockHeader` into a shared `ConditionFields` component used by both headers (guarantees FR-008 identical controls); `IfBlock` mirrors `LoopBlock` with a then area always visible and an else area behind an "Add else" affordance; add-step column label becomes "Loops and Conditions" with the If button after Count/While/Repeat‑Until.

## Complexity Tracking

*No constitution violations to justify — table intentionally empty.*
