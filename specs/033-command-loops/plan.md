# Implementation Plan: Command Loop Structures

**Branch**: `033-command-loops` | **Date**: 2026-03-31 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/033-command-loops/spec.md`

## Summary

Add three loop step types (`count`, `while`, `repeatUntil`) and a `break` step type to the `SequenceStep` domain model. Loops carry a body of ordered inner steps (all step types valid except loop-within-loop). The `SequenceRunner` is extended to execute loop bodies with iteration tracking, a `{{iteration}}` template placeholder substituted at execution time, and a configurable safety limit (default 1000, hard failure on breach). Per-iteration outcomes are recorded in the execution log. The authoring UI (`SequencesPage.tsx`) renders loop blocks as visually contained cards with a header badge and indented inner step list.

## Technical Context

**Language/Version**: C# 13 / .NET 9 (backend); TypeScript ES2020 / React 18 (frontend)
**Primary Dependencies**: ASP.NET Core Minimal API, `System.Text.Json` (polymorphic serialization), existing `SequenceRunner` + `SequenceStepCondition` infrastructure, React 18 + Vite 5
**Storage**: File-backed JSON sequence repository under `data/` (`FileSequenceRepository`); global config under `data/config/config.json`
**Testing**: xUnit + coverlet (backend unit/integration); React Testing Library + Playwright (frontend)
**Target Platform**: Windows desktop (Windows-only ADB/System.Drawing; backend service + React SPA)
**Project Type**: Web service + SPA authoring UI
**Performance Goals**: Loop execution overhead must not exceed 5 ms per iteration for the runner dispatch logic (excluding inner action I/O). Single sequence run with 10 loop iterations of 3 steps each must complete within existing sequence timing benchmarks.
**Constraints**: No new external NuGet/npm packages; loop nesting rejected (validation enforced); `{{iteration}}` substitution scoped to loop bodies only
**Scale/Scope**: Single-user desktop tool; sequences up to ~100 steps; loops up to 1000 iterations max (safety limit)

## Constitution Check

*GATE: Must pass before implementation begins. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing, implementation is blocked until fixed or explicitly waived.

| Gate | Status | Notes |
|------|--------|-------|
| Code Quality: lint/format/static analysis clean | Required | All new C# must use CamelCase methods; no underscores in method names |
| Tests: unit coverage >= 80% line / >= 70% branch for touched areas | Required | `SequenceRunner` loop paths, `TemplateSubstitutor`, validation rules |
| No new high/critical static analysis issues | Required | |
| UI interfaces documented | Required | `LoopBlock` component props documented |
| Performance: loop overhead <= 5 ms/iteration (dispatch only) | Required | Micro-benchmark note in PR |
| Build green before merging | Required | `dotnet build -c Debug` and `npm run build` must pass |
| Tests green before merging | Required | `dotnet test -c Debug` must pass; frontend component tests pass |
| No loop nesting allowed (enforced by validation) | Required | FR-012 |

## Project Structure

### Documentation (this feature)

```
specs/033-command-loops/
├── plan.md                          # This file
├── research.md                      # Phase 0 — design decisions (R-001 through R-008)
├── data-model.md                    # Phase 1 — domain model + validation rules
├── contracts/
│   └── sequence-loop-steps.md      # Phase 1 — API contracts and JSON shapes
└── tasks.md                         # Phase 2 — /speckit.tasks output (not yet created)
```

### Source Code

```
src/
  GameBot.Domain/
    Commands/
      SequenceStep.cs               EXTEND: add Loop, Break step types + Body + BreakCondition
      LoopConfig.cs                 NEW: CountLoopConfig / WhileLoopConfig / RepeatUntilLoopConfig
    Utils/
      TemplateSubstitutor.cs        NEW: {{iteration}} placeholder substitution utility
    Services/
      SequenceRunner.cs             EXTEND: loop execution + break signal + context vars injection
    Config/
      AppConfig.cs                  EXTEND: LoopMaxIterations property (default 1000)
    Logging/
      ExecutionLogModels.cs         EXTEND: LoopIterationOutcome + loopIterations on ExecutionStepOutcome
    Validation/
      SequenceValidator.cs          EXTEND: loop-specific validation rules (FR-004, FR-012, FR-021, FR-002a)

  GameBot.Service/
    Endpoints/
      SequencesEndpoints.cs         EXTEND: POST validate invokes extended validator

  web-ui/src/
    components/sequences/
      LoopBlock.tsx                 NEW: loop block container (colored border + background)
      LoopBlockHeader.tsx           NEW: loop type badge + count/condition summary
      BreakStepRow.tsx              NEW: break step row rendering
    pages/
      SequencesPage.tsx             EXTEND: dispatch loop/break step types to new components

tests/
  GameBot.Domain.Tests/
    SequenceRunnerLoopTests.cs      NEW: count/while/repeatUntil/break/safety-limit/conditionError paths
    TemplateSubstitutorTests.cs     NEW: {{iteration}} substitution + edge cases
    LoopValidationTests.cs          NEW: all validation rules (FR-004, FR-012, FR-021, FR-002a, FR-008)
  contract/
    SequenceLoopContractTests.cs    NEW: JSON serialization round-trip for loop/break steps
  web-ui/src/
    components/sequences/
      LoopBlock.test.tsx            NEW: render tests for count/while/repeatUntil + break step
```

## Complexity Tracking

No constitution violations. All additions are incremental extensions to existing patterns (`SequenceStepType`, `SequenceStepCondition`, `SequenceRunner`, `SequencesPage.tsx`). No new projects or repositories introduced.