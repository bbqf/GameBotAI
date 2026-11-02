# Implementation Plan: Wait for Still Step

**Branch**: `174-wait-for-still-step` | **Date**: 2026-11-02 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/174-wait-for-still-step/spec.md`

## Summary

Add a new sequence step `WaitForStill`. The step waits until a region of the screen stops changing.
It compares each capture with the previous capture. It ends with `still` when the region stays the same
for `stableMs`. It ends with `timeout_elapsed` at `timeoutMs`.

The step is a service-level action, in the same pattern as `quiz-lookup` (feature 142). It needs no new
property on `SequenceStep`. A new handler reads frames from `ISessionFrameSource`. A pure `FrameDifference`
class compares two frames with `LockBits` (full compare of every pixel in the region). A pure
`WaitForStillTracker` class holds the stable-time state. The runner, the validator, the OpenAPI text, the
outcome states, the execution log, and the web UI get small changes. See [research.md](research.md).

## Technical Context

**Language/Version**: C# on .NET (service, domain); TypeScript with React (web UI)
**Primary Dependencies**: `System.Drawing` (`Bitmap`, `LockBits`); existing `ISessionFrameSource`, `PixelRegion`, `WaitForImageSchedule`; no new package
**Storage**: Sequence JSON files (`FileSequenceRepository`); no format change
**Testing**: xUnit (unit, contract, integration); Vitest (web UI)
**Target Platform**: Windows service with a web UI (desktop editor)
**Project Type**: Web application (service plus web UI)
**Performance Goals**: Frame compare of a 1280x720 region in a few milliseconds. Poll every `AppConfig.CaptureIntervalMs` (default 500 ms). SC-001: end no later than `stableMs` plus one interval after the last change. SC-002: end no later than `timeoutMs` plus one interval.
**Constraints**: Keep one previous frame only. Dispose each bitmap. Let cancellation pass up. Do not change `WaitForImage` behavior (FR-011).
**Scale/Scope**: One new action type; about 8 new files and about 15 changed files.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

*NON-NEGOTIABLE*: All text in this plan and in the artifacts it produces obeys Simplified Technical English (Constitution Principle VI).

*Phone check (Principle III)*: The web UI form for this step has the phone support level **desktop-only**. This is the same level as the `WaitForImage` authoring form. The form follows docs/mobile-ui-principles.md for the desktop-only area.

| Principle | Status | Evidence |
|-----------|--------|----------|
| I. Code quality | Pass | Small classes, each under about 50 lines per method. CamelCase method names. Doc comments on public types. No new dependency. |
| II. Testing | Pass | Unit tests for `FrameDifference`, `WaitForStillTracker`, payload, handler, outcome states. Contract test for OpenAPI. Integration tests for a full run. Vitest tests for the form. Tests are red first (research R14). |
| III. UX consistency | Pass | Error messages are clear and have a remedy (see contract). The step follows `WaitForImage` in validation, run, and outcome use (FR-009). Phone level is stated above. |
| IV. Performance | Pass | Goals are in Technical Context. The PR description has a timing note for SC-001, SC-002, and SC-004. |
| V. Living documentation | Pass | The polish phase updates `docs/architecture.md` (with "Last reviewed"), `CHANGELOG.md`, `specs/STATUS.md`, and the spec status line. The polish phase also checks that the contract file `contracts/wait-for-still-step.md` matches the final code. |
| VI. STE | Pass | All new text is written in STE. `/speckit-analyze` checks it. |
| Version bump | Planned | Before the push, set `patch` in `installer/versioning/version.override.json` (master `patch` plus 1) and mention it in the CHANGELOG. |
| Shard balance | Planned | The polish phase measures the test shards with `scripts/ci/measure-shards.ps1`. |

Post-design re-check: no violation. No entry is needed in Complexity Tracking.

## Project Structure

### Documentation (this feature)

```text
specs/174-wait-for-still-step/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── wait-for-still-step.md
└── tasks.md
```

### Source Code (repository root)

```text
src/GameBot.Domain/
├── Actions/ActionTypes.cs, SequenceActionTypes.cs      # changed: new type constant and list entry
├── Vision/FrameDifference.cs                            # new: mean absolute difference with LockBits
├── WaitForStill/
│   ├── WaitForStillOutcomes.cs                          # new
│   ├── WaitForStillTracker.cs                           # new
│   └── WaitForStillPayload.cs                           # new
├── Services/SequenceRunner.cs                           # changed: service-level action, outcome record
├── Services/SequenceStepValidationService.cs            # changed: call payload validation
├── Services/StepOutcomeStates.cs                        # changed: allow still, timeout_elapsed
└── Commands/FileSequenceRepository.cs                   # changed: load check

src/GameBot.Service/
├── Services/WaitForStill/WaitForStillHandler.cs         # new
├── Services/SequenceExecution/SequenceExecutionService.cs  # changed: dispatch and log entry
├── Services/StepThrough/StepThroughActionPreview.cs     # changed: preview text
├── Swagger/PrimitiveActionSchemaFilter.cs               # changed: payload and commandOutcome text
└── GameBotServiceSetup.cs                               # changed: register handler

src/web-ui/src/
├── components/sequences/WaitForStillFields.tsx          # new
├── components/sequences/PrimitiveActionFields.tsx       # changed: route the type
├── pages/SequencesPage.tsx                              # changed: "Add wait for still step" button
└── lib/validation.ts                                    # changed: expected states

tests/
├── unit/WaitForStill/                                   # new: difference, tracker, payload, handler
├── unit/Sequences/                                      # changed: type sync, outcome states, runner
├── contract/Sequences/PrimitiveActionTypesOpenApiTests.cs  # changed
├── integration/Sequences/WaitForStillSequenceExecutionIntegrationTests.cs  # new
└── src/web-ui/src/components/sequences/__tests__/WaitForStillFields.test.tsx  # new

docs/architecture.md, CHANGELOG.md, specs/STATUS.md      # changed in the polish phase
installer/versioning/version.override.json               # changed: version bump
```

**Structure Decision**: Web application layout. The domain project holds the pure logic. The service
project holds the handler that uses the capture source. The web UI holds the form. No new project is added.

## Key Design Decisions

- Service-level action, not a new `SequenceStep` property (R1). Sequence steps only (R2).
- The runner uses the existing service-level path for dry run, step-through, stop, and log (R3).
- Frame compare: mean absolute R, G, B difference over the whole region, scale 0 to 1, read with `LockBits` (full compare of every pixel, no sampling) (R4).
- Stable time counts from the previous frame time. A failed capture calls `Reset()` (R5, R6).
- A failed capture or a missing frame is a skip and retry. Cancellation passes up. No session gives `timeout_elapsed` (R6).
- Region: validate at save time; clip at run time; fully outside gives `failed` (R7).
- Outcome names `still` and `timeout_elapsed` are allowed in `commandOutcome` conditions (R9).
- Web UI form is desktop-only (R11).

## Complexity Tracking

No violation. This table is empty.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| None | N/A | N/A |
