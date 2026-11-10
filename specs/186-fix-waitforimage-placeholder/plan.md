# Implementation Plan: Resolve parameter placeholders in inline WaitForImage

**Branch**: `186-fix-waitforimage-placeholder` | **Date**: 2026-11-10 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/186-fix-waitforimage-placeholder/spec.md`

## Summary

An inline WaitForImage step of a sequence keeps the text `{{name}}` in `detectionTarget.referenceImageId`. The cause is in `SequenceRunner.ExecuteSingleStepAsync`. It calls `ApplyScope`, and `ApplyScope` copies `step.WaitForImage` without substitution. The command path already resolves this field with `CommandStepResolver` (features 114 and 180). The plan uses that resolver for the sequence path.

The second defect is in the execution log. `SequenceExecutionService` writes `matchedImageId` into the step attribute map. The typed record `WaitForImageDetailAttributes` and the DTO `ExecutionLogWaitForImageDetailAttributesDto` have no `matchedImageId` field. So the field does not reach `detailAttributes`. The plan adds the field to the record, the DTO, the two builders, and the web-ui type.

## Technical Context

**Language/Version**: C# on .NET (net9 as in the repo), TypeScript for web-ui types
**Primary Dependencies**: ASP.NET minimal API, xUnit, Vitest (web-ui)
**Storage**: Execution log files (JSON). The new field is optional, so old logs stay valid.
**Testing**: xUnit unit tests (`tests/unit/Sequences`, `tests/unit/Commands`) and integration tests (`tests/integration/Sequences`). Vitest for the web-ui type.
**Target Platform**: Windows service (GameBot.Service)
**Project Type**: web-service with web-ui
**Performance Goals**: No change. The resolver runs one time for each WaitForImage step, before the wait. It adds no capture or poll work.
**Constraints**: A literal `{{placeholder}}` MUST NOT reach the image evaluator. Steps with literal ids MUST behave as before.
**Scale/Scope**: 2 domain files, 3 service files, 1 web-ui file, tests, docs.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: Build and test runs must pass before implementation progresses. Plan: run `dotnet build` and the unit and integration tests of the touched areas before each commit.

*NON-NEGOTIABLE*: All text in this plan and in its artifacts obeys STE (Principle VI). Status: obeyed.

- I. Code quality: methods stay under about 50 lines; names use CamelCase without underscores. Pass.
- II. Testing: bug fix. A failing test that shows the bug MUST come before the fix (tasks put the tests first). Pass.
- III. UX consistency: the error for an unknown parameter reuses the existing unresolved-parameter message. The change does not alter the web UI. Phone support level: not applicable (no UI change; only a TypeScript type gets one optional field). Pass.
- IV. Performance: no hot-path change. Pass.
- V. Living documentation: update `docs/architecture.md` if it describes WaitForImage resolution or log fields, and refresh "Last reviewed". Add the `Status` line to spec.md. Update `specs/STATUS.md`. Mark the change as an iteration of features 114, 180, and 181. Pass.
- VI. STE: obeyed in all new text. Pass.
- Version bump and CHANGELOG entry are done in phase 2 before the PR.

No violations. Complexity Tracking is not needed.

## Project Structure

### Documentation (this feature)

```text
specs/186-fix-waitforimage-placeholder/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── execution-log-detail-attributes.md
├── checklists/requirements.md
└── tasks.md             # created by /speckit-tasks
```

### Source Code (repository root)

```text
src/GameBot.Domain/
├── Parameters/CommandStepResolver.cs        # add public TryResolveWaitForImage (wraps TryDetection)
├── Services/SequenceRunner.cs               # resolve step.WaitForImage in ExecuteSingleStepAsync; log resolved ids
└── Logging/ExecutionLogModels.cs            # WaitForImageDetailAttributes: add MatchedImageId

src/GameBot.Service/
├── Models/ExecutionLogs.cs                  # ExecutionLogWaitForImageDetailAttributesDto: add MatchedImageId
├── Endpoints/ExecutionLogsEndpoints.cs      # copy MatchedImageId in the two DTO maps
└── Services/ExecutionLog/ExecutionLogService.cs  # fill MatchedImageId in both builders

src/web-ui/src/services/executionLogsApi.ts  # add optional matchedImageId

tests/unit/Sequences/SequenceRunnerWaitForImageTests.cs
tests/unit/Parameters/ (resolver tests)
tests/integration/Sequences/WaitForImageSequenceExecutionIntegrationTests.cs
```

**Structure Decision**: Existing layout. No new project. One new public method in the resolver. The sequence runner calls it.

## Design

1. Add `CommandStepResolver.TryResolveWaitForImage(WaitForImageConfig config, ParameterScope scope, out WaitForImageConfig? resolved, out ParameterResolutionError? error)`. It builds a temporary `CommandStep` of type WaitForImage and calls the existing `TryResolve`. The command path and the sequence path then use one rule set.
2. In `SequenceRunner.ExecuteSingleStepAsync`, resolve the config right after `ApplyScope` and before the dry-run check. Use the scope of the step. On error, fail the step with `ParameterResolutionError.ToMessage(stepKey)` and stop the sequence, as a failed guard does. The step does not wait and gives no `image_unavailable`.
3. The resolved config replaces `step.WaitForImage` for `RecordWaitForImageStepAsync`. `WaitForImageAsync` already handles `ReferenceImageIds` with more than one item and sets `MatchedImageId`.
4. Log: `detail.ReferenceImageId` shows the resolved first id. The log gets no new list field (see research R3). `matchedImageId` goes to `detailAttributes` through the typed record and DTO.
5. Dry run and step-through preview: no change, except that they must not fail on a placeholder (research R5).

## Phase 0 and Phase 1 outputs

See [research.md](research.md), [data-model.md](data-model.md), [contracts/](contracts/), and [quickstart.md](quickstart.md).
