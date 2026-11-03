# Implementation Plan: Wait Until an Image Is Gone

**Branch**: `175-wait-for-image-gone` | **Date**: 2026-11-03 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/175-wait-for-image-gone/spec.md`

## Summary

Add the optional parameter `until` (`present` or `absent`) and the optional parameter `absentCaptures`
(whole number, 1 or more, default 2) to the sequence `WaitForImage` step. In absent mode the step ends
with the new outcome `image_absent` when `absentCaptures` consecutive captures lack the image. It ends with
`timeout_elapsed` at `timeoutMs`, and with `image_unavailable` when no capture source is available. A step
without `until` keeps its current behavior.

The runner gets a second poll loop for absent mode. The loop calls the existing condition evaluator in
`Present` mode and reads `ImageDetectionPassScope.Current?.LastPass` (`src/GameBot.Domain/Vision/ImageDetectionPassScope.cs`)
to tell "image not found" from "capture failed". A pure class `AbsentCaptureCounter` holds the count rule.
The validator, the OpenAPI text, the execution log, the step-through preview, the web UI form, and the docs
get small changes. See [research.md](research.md).

## Technical Context

**Language/Version**: C# on .NET (service, domain); TypeScript with React (web UI)
**Primary Dependencies**: Existing `ImageMatchEvaluator`, `ImageDetectionPassScope`, `WaitForImageSchedule`, `PixelRegion`; no new package
**Storage**: Sequence JSON files (`FileSequenceRepository`); no format change. Two optional fields are new in `action.parameters`.
**Testing**: xUnit (unit, contract, integration); Vitest (web UI)
**Target Platform**: Windows service with a web UI (desktop editor)
**Project Type**: Web application (service plus web UI)
**Performance Goals**: Same poll rate as present-mode `WaitForImage`. SC-001: end no later than `absentCaptures` capture intervals after the first capture that lacks the image. SC-002: end no later than `timeoutMs` plus one capture interval.
**Constraints**: Do not change present-mode behavior (FR-002, SC-005). Do not add a step type or an arrival check (FR-008). Let cancellation pass up with no outcome. A failed capture never counts as absent (FR-007).
**Scale/Scope**: Two new optional fields, one new outcome name, about 3 new domain files, and about 12 changed files.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

*NON-NEGOTIABLE*: All text in this plan and in the artifacts it produces obeys Simplified Technical English (Constitution Principle VI).

*Phone check (Principle III)*: The phone support level is **desktop-only**. This is the level of the `WaitForImage` authoring UI: the Sequences page is part of the Authoring area, which is desktop-only in docs/mobile-ui-principles.md. On a phone the page shows the desktop-only notice. The new fields follow docs/mobile-ui-principles.md for the desktop-only area.

| Principle | Status | Evidence |
|-----------|--------|----------|
| I. Code quality | Pass | Small classes and short methods. Doc comments on public types. No new dependency. The count rule is in one class (`AbsentCaptureCounter`). |
| II. Testing | Pass | Tests are red first. Failing tests for the log mapping (`image_absent` to `success`) and for the preview text come before the code that makes them pass (FR-009). Unit tests for the parser, the counter, the validator, and the runner loop. A runner test with a fake clock shows that the step ends before the fixed wait time (SC-004). Contract test for the OpenAPI text. Integration test for a full run. Vitest tests for the form. Existing `WaitForImage` tests stay unchanged (SC-005). |
| III. UX consistency | Pass | Error messages are clear and have a remedy. The mode follows `WaitForImage` in validation, run, log, and outcome use (FR-009). The phone level is stated above. |
| IV. Performance | Pass | Goals are in Technical Context. The PR description has a timing note for SC-001, SC-002, and SC-004. |
| V. Living documentation | Pass | The polish phase updates `docs/architecture.md` (with "Last reviewed"), `CHANGELOG.md`, `specs/STATUS.md`, and the spec status line. It also checks that `contracts/wait-for-image-until.md` matches the final code. |
| VI. STE | Pass | All new text is written in STE. `/speckit-analyze` checks it. |
| Version bump | Planned | The version task comes before the CHANGELOG task. Set `patch` in `installer/versioning/version.override.json` as CLAUDE.md states. |
| Shard balance | Planned | The polish phase measures the test shards with `scripts/ci/measure-shards.ps1`. The task has a skip rule. |

Post-design re-check: no violation. No entry is needed in Complexity Tracking.

## Project Structure

### Documentation (this feature)

```text
specs/175-wait-for-image-gone/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── wait-for-image-until.md
├── checklists/
└── tasks.md
```

### Source Code (repository root)

```text
src/GameBot.Domain/
├── Commands/WaitForImageConfig.cs                        # changed: Until, AbsentCaptures
├── Commands/Execution/WaitForImageUntil.cs               # new: constants and TryParse
├── Commands/Execution/AbsentCaptureCounter.cs            # new: PassKind and Observe (only count logic)
├── Parameters/CommandStepResolver.cs                     # changed: carry both fields
├── Triggers/Evaluators/ImageMatchEvaluator.cs            # read; changed only if LastPass is not written on every pass
├── Vision/ImageDetectionPassScope.cs                     # read: LastPass and FrameAvailable
├── Services/SequenceRunner.cs                            # changed: absent loop, outcome record
├── Services/SequenceStepValidationService.cs             # changed: ValidateWaitForImagePayload
├── Commands/FileSequenceRepository.cs                    # changed only if it does not call the validator
└── Logging/ExecutionLogModels.cs                         # read; no text list (skip rule)

src/GameBot.Service/
├── Models/SequenceStepContracts.cs                       # changed: WaitForImagePayloadContract
├── Models/ExecutionLogs.cs                               # read; no text list (skip rule)
├── Endpoints/SequencesEndpoints.cs                       # changed: MapWaitForImageConfig
├── Services/ExecutionLog/ExecutionLogService.cs          # changed: image_absent maps to success
├── Services/StepThrough/StepThroughActionPreview.cs      # changed: preview text names the absent mode
└── Swagger/PrimitiveActionSchemaFilter.cs                # changed: WaitForImage text

src/web-ui/src/
├── pages/SequencesPage.tsx                               # changed: fields, state, load, save, preview text
├── services/sequences.ts                                 # changed: types
├── services/executionLogsApi.ts, pages/executionLogGrid.ts  # changed: outcome label
├── components/sequences/PrimitiveActionFields.tsx        # changed only if the form is routed to it
└── pages/__tests__/
    ├── SequencesPageWaitForImageUntil.test.tsx           # new
    └── SequencesPage.stepThrough.spec.tsx                # changed: absent preview case

tests/
├── unit/Commands/WaitForImageUntilTests.cs               # new: parser of until
├── unit/Commands/AbsentCaptureCounterTests.cs            # new
├── unit/Sequences/SequenceRunnerWaitForImageTests.cs     # changed: absent loop, region, fake-clock SC-004 test
├── unit/Sequences/WaitForImageUntilValidationTests.cs    # new
├── unit/ExecutionLogs/ExecutionLogServiceMapStepStatusTests.cs  # changed
├── unit/StepThrough/WaitForImageAbsentPreviewTests.cs    # new
├── contract/Sequences/PrimitiveActionTypesOpenApiTests.cs  # changed
└── integration/Sequences/WaitForImageAbsentSequenceIntegrationTests.cs  # new

docs/architecture.md, CHANGELOG.md, specs/STATUS.md       # changed in the polish phase
installer/versioning/version.override.json                # changed: version bump
```

**Structure Decision**: Web application layout. The domain project holds the model, the count rule, and
the runner loop. The service project holds the payload contract, the log mapping, the preview text, and the
OpenAPI text. The web UI holds the form. No new project is added.

## Key Design Decisions

- Option `until` on the sequence `WaitForImage` step, not a new step type (R1). The command-step path stays in present mode.
- The absent loop calls the evaluator in `Present` mode and reads `LastPass.FrameAvailable` to tell absent from a failed capture (R2).
- The evaluator is `src/GameBot.Domain/Triggers/Evaluators/ImageMatchEvaluator.cs`. The pass scope is `src/GameBot.Domain/Vision/ImageDetectionPassScope.cs`. The plan task checks that the evaluator writes `LastPass` on every pass.
- One pure class `AbsentCaptureCounter` holds the count rule. No other task writes counter logic (R3).
- Same poll schedule and deadline as present mode (`WaitForImageSchedule.NextWait`) (R4).
- No capture source, or an evaluator that throws, gives `image_unavailable` at the deadline (R5).
- `image_absent` is a plain string set in `WaitForImageAsync`, as `image_detected` is. `stepOutcomes` records `success` (R6). The log task only maps `image_absent` to `success` in `ExecutionLogService.cs`.
- Validation: `until` is `present` or `absent`; `absentCaptures` is a whole number of 1 or more; `{{name}}` references are skipped (R7).
- The web UI form is desktop-only: a "Wait until" select and an "Absent captures" field. Save omits both fields in present mode (R8).
- SC-004 has a runner test with a fake clock: the step ends before the fixed wait time (R10).

## Complexity Tracking

No violation. This table is empty.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| None | N/A | N/A |
