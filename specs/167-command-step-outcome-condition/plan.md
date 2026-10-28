# Implementation Plan: Sequence condition on a step outcome inside a command

**Branch**: `167-command-step-outcome-condition` | **Date**: 2026-10-28 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/167-command-step-outcome-condition/spec.md` (GitHub issue #352)

## Summary

A sequence author cannot tell which steps of a command ran. The `commandOutcome` condition reads one state for the whole command step. This feature adds a new condition type `commandStepOutcome` (option A of the issue). It reads the state of one top-level step of the command that a prior sequence step ran. The four readable states are `executed`, `skipped_detection_failed`, `completed_timeout`, and `image_detected`.

The design has six parts:

1. `CommandExecutor` marks each step outcome with a nesting `Depth` (additive field, no behavior change). `SequenceExecutionService` keeps the depth 0 entries with a readable state and puts them in a new optional field `StepStates` of `CommandDispatchOutcome` (research D1, D2).
2. `SequenceRunner` keeps a run-scoped map of step states for each sequence step. A new command run of a step replaces the older entry. A step that is skipped, fails, or does not run in a dry run keeps the older entry (D3).
3. `SequenceStepConditionEvaluator` evaluates the new leaf. A missing outcome gives false before `negate`, and the run does not fail. Each evaluation writes one log line (D4).
4. Validation: the Domain validators check the `stepRef` rules, the order minimum, and the state. The order check against the command runs ONLY at save and in the save dry run, in `SequencesEndpoints`. The run dry run and the run check only the `stepRef` rules (D5).
5. The OpenAPI schema lists the type. `commandStepOrder` has `minimum: 1` (D8).
6. The web-UI sequence editor creates and edits the condition (D7).

`commandOutcome`, the command run path, and saved sequences do not change.

## Technical Context

**Language/Version**: C# on .NET 9 (`net9.0`); TypeScript with React (web UI)
**Primary Dependencies**: ASP.NET Core minimal API, Swashbuckle schema filters, Microsoft.Extensions.Logging, React. No new package.
**Storage**: Sequence JSON files (`FileSequenceRepository`). The new condition is one more value of the `type` discriminator. The step states live in memory for one run only.
**Testing**: xUnit (`tests/unit`, `tests/contract`, `tests/integration`); Vitest for the web UI.
**Target Platform**: Windows service (the GameBot service) and the web UI in a browser.
**Project Type**: Web service with a web UI.
**Performance Goals**: One dictionary lookup for each evaluation. One small dictionary copy for each command run. No measurable cost.
**Constraints**: No change to how a command runs its steps (FR-009). The only change in the command run path is the additive `Depth` field (FR-009a). No change to `commandOutcome` (FR-010). No recall, shield, or run-scoped flag logic (FR-013).
**Scale/Scope**: About 21 source files change or are new. About 11 test files are new. One section of `docs/architecture.md` changes.

No item is NEEDS CLARIFICATION. The spec Clarifications section and research.md resolve all items.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

*NON-NEGOTIABLE*: All text in this plan and in the artifacts it produces (research, data model, contracts, quickstart, tasks, code comments, user-facing messages) MUST obey Simplified Technical English (Constitution Principle VI).

*Phone check (Constitution Principle III)*: The feature changes the web UI (sequence editor). Phone support level: reduced, the same as the other condition fields of the editor. It follows `docs/mobile-ui-principles.md`.

| Principle | Gate | Status |
|-----------|------|--------|
| I. Code Quality | No new analyzer warnings. Methods under about 50 lines. Public members get XML doc comments. The new condition reuses the `commandOutcome` code paths. | PASS (planned) |
| II. Testing | Tests come first for each user story: unit tests of the evaluator, states, log, positions, nested command, and validation; contract tests of the JSON shape, validation errors, and OpenAPI; one integration test of the end-run flow. A test shows that a nested step with the same order does not change the result. Coverage of 80% line and 70% branch for touched code. | PASS (planned) |
| III. UX Consistency | Errors use the style of `commandOutcome`. The editor shows the same errors as the service. Phone level: reduced. | PASS (planned) |
| IV. Performance | One lookup for each evaluation (see Technical Context). No new loop and no new I/O. | PASS |
| V. Living Documentation | Update `docs/architecture.md` and refresh "Last reviewed". At merge, set the spec `Status` and `specs/STATUS.md`. | PASS (planned) |
| VI. STE | All artifacts of this plan are in STE. Code comments and messages in STE. | PASS |
| Version bump | Bump `installer/versioning/version.override.json` before the PR push. | PASS (planned) |

**Post-design re-check**: PASS. The design adds one static class (`CommandStepOutcomeStates`), one condition class, one contract class, one optional field `StepStates`, and one optional field `Depth`. Each one has a clear reason in research.md. No violation needs a Complexity Tracking entry.

## Design rules (summary)

- **Order check scope**: The order check against the command runs ONLY at save and in the save dry run (`SequencesEndpoints`, through the command repository). The run dry run and the run check only the `stepRef` rules. A missing order at run time gives "no outcome recorded". When the command cannot be loaded, the check is skipped.
- **Latest-run rule**: A new command run of a sequence step replaces the older recorded outcome, even if the new run has no readable states. A step that is skipped, fails, or does not run in a dry run keeps the older entry.
- **Schema minimum**: The OpenAPI schema shows `minimum: 1` for `commandStepOrder`. The validators reject a value below 1.
- **Nested commands**: Only top-level steps (depth 0) are readable.
- **Missing outcome**: The condition is false before `negate`. The run does not fail.

## Project Structure

### Documentation (this feature)

```text
specs/167-command-step-outcome-condition/
├── plan.md              # This file (/speckit.plan command output)
├── research.md          # Phase 0 output (/speckit.plan command)
├── data-model.md        # Phase 1 output (/speckit.plan command)
├── quickstart.md        # Phase 1 output (/speckit.plan command)
├── contracts/
│   └── command-step-outcome-condition.md   # Phase 1 output (/speckit.plan command)
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 output (/speckit.tasks command - NOT created by /speckit.plan)
```

### Source Code (repository root)

```text
src/GameBot.Domain/
├── Commands/
│   ├── CommandStepOutcomeStates.cs          # new: four states, All, AllText, IsAllowed
│   ├── SequenceStepCondition.cs             # new condition class (type commandStepOutcome)
│   ├── ConditionExpression.cs               # new leaf in the condition model
│   ├── IfConfig.cs                          # new type allowed in the if condition
│   ├── SequenceStep.cs                      # new type allowed in the step condition
│   └── FileSequenceRepository.cs            # load and save of the new type
└── Services/
    ├── CommandDispatchOutcome.cs            # new optional field StepStates (order -> state)
    ├── SequenceRunner.cs                    # run-scoped commandStepStates map, latest-run rule
    ├── SequenceStepConditionEvaluator.cs    # new leaf evaluation and log line
    ├── SequenceStepValidationService.cs     # stepRef, order minimum, and state rules
    ├── CompositeConditionValidator.cs       # new type in the validation gate
    └── BreakStepIndex.cs                    # break condition position

src/GameBot.Service/
├── Services/
│   ├── CommandExecutor.cs                   # sets Depth on each step outcome (additive only)
│   ├── ICommandExecutor.cs                  # PrimitiveTapStepOutcome gets optional field Depth
│   └── SequenceExecution/
│       ├── SequenceExecutionService.cs      # builds StepStates from depth 0 outcomes
│       └── SequenceExecutionService.StepThrough.cs   # same for the step-through path
├── Models/
│   └── SequenceStepContracts.cs             # CommandStepOutcomeConditionContract
├── Endpoints/
│   └── SequencesEndpoints.cs                # map contract; order check at save and save dry run only
└── Swagger/
    ├── ConditionReferenceScopeSchemaFilter.cs   # type and fields in the schema
    ├── ConditionalFlowSchemaDocumentFilter.cs   # type in the condition enum, minimum 1
    ├── PrimitiveActionSchemaFilter.cs           # type and fields in the schema
    └── SwaggerConfig.cs                         # example (only if the examples list needs it)

src/web-ui/src/
├── components/sequences/
│   ├── ConditionFields.tsx                  # type option, step picker, order input, state select
│   └── BreakStepRow.tsx                     # type option in the break condition
├── lib/validation.ts                        # same errors as the service
├── pages/SequencesPage.tsx                  # load, edit, and save logic
└── types/
    ├── sequenceFlow.ts                      # new condition type
    └── stepEntry.ts                         # new condition fields

docs/architecture.md                         # condition type list; Last reviewed

tests/
├── unit/
│   ├── Commands/CommandExecutorStepDepthTests.cs
│   └── Sequences/
│       ├── CommandStepOutcomeStatesTests.cs
│       ├── CommandStepOutcomeConditionTests.cs
│       ├── CommandStepOutcomeLogTests.cs
│       ├── CommandStepOutcomePositionTests.cs
│       ├── CommandStepOutcomeNestedCommandTests.cs
│       └── CommandStepOutcomeValidationTests.cs
├── contract/Sequences/
│   ├── CommandStepOutcomeConditionContractTests.cs
│   ├── CommandStepOutcomeValidationContractTests.cs
│   └── CommandStepOutcomeOpenApiTests.cs
├── integration/Sequences/CommandStepOutcomeEndRunIntegrationTests.cs
└── (web UI) src/web-ui/src/components/sequences/__tests__/CommandStepOutcomeFields.test.tsx
    and src/web-ui/src/lib/__tests__/commandStepOutcomeValidation.spec.ts
```

**Structure Decision**: The existing layout is kept: the Domain project holds the condition model, the validators, the runner, and the evaluator. The Service project holds the command execution, the endpoints, and the schema filters. The web UI holds the editor. The order check against the command lives in the endpoint layer, because the Domain validators have no command repository.

## Complexity Tracking

No violation. No entry needed.
