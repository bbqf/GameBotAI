# Implementation Plan: Fix inline WaitForImage in installed gamepack sequences

**Branch**: `177-fix-installed-waitforimage` | **Date**: 2026-11-04 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/177-fix-installed-waitforimage/spec.md`

## Summary

Bug B-039 (issue #373). Gamepack install stores an inline `WaitForImage` step with its action payload only. The step has no `WaitForImage` configuration (`SequenceStep.WaitForImage` is null). The runner reads only that configuration, so the step runs as a 1 s delay with no image.

The fix has two parts:

1. Add one Domain class, `WaitForImageConfigMapper`. It is the ONLY reader of the WaitForImage payload fields: `timeoutMs`, `until`, `absentCaptures`, and `detectionTarget` (image id, confidence, offsets, strategy, region). It also holds the number reader (`TryReadInt32`) and the region reader (`TryReadRegion`) that `SequencesEndpoints` has today. These two readers MOVE to Domain. `SequencesEndpoints` keeps no private copy: the save mapping calls `Map`, and the save-time region check (feature 130, `CollectRegionErrors`) calls `TryReadRegion` for its error text and keeps its messages.
2. Add a Domain helper, `GamepackWaitForImageMapping`. It walks the steps of a sequence in one recursive walk (the `Body` and `ElseBody` members of `SequenceStep`, at every depth). It sets `WaitForImage` on each `WaitForImage` step. `GamepackValidator` (the plan builder) calls the helper after `RewriteCommandIds` and before the `PlannedObject` is added. The content is then final before the take-over hash (`PostHash`) is computed. `GamepackApplier` does not change. The runner does not change.

Before the mapper code is final, the work MUST check the real runtime type of the nested `detectionTarget` value after a pack read (research decision 3). The mapper MUST accept that type.

Parity (SC-003) is proved by a contract test that compares the stored step of a pack install with the stored step of `POST /api/sequences` for the same payload. A test that compares the mapper with itself does not count.

## Technical Context

**Language/Version**: C# 13 on .NET 9  
**Primary Dependencies**: System.Text.Json (already in use). No new dependency.  
**Storage**: Sequence JSON files through `FileSequenceRepository` (no format change; the `waitForImage` member already exists in the stored form)  
**Testing**: xUnit. Unit tests (`tests/unit`) for the mapper, the readers, and the walk. Contract tests (`tests/contract/Gamepacks`) for install and parity, with a real pack JSON file.  
**Target Platform**: Windows service, Linux CI  
**Project Type**: Web service with Domain library  
**Performance Goals**: No change. The walk is linear in the number of steps and runs once for each install.  
**Constraints**: No new API endpoint and no contract change (FR-007). Install adds no validation (FR-005a). The hash of the final content must match the stored content (FR-005b). Image-reference checks of a pack do not change (spec clarification C1).  
**Scale/Scope**: One new Domain mapper (with the moved readers), one new Domain walk helper, one call in `GamepackValidator`, call-site changes in `SequencesEndpoints` (remove `MapWaitForImageConfig`, `MapWaitForImageDetectionTarget`, `TryReadInt32`, and the body of `TryReadRegion`), tests, and docs.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- Build and tests: no known red state. Implementation must run build and tests before each commit. PASS.
- STE (Principle VI): all new text in this plan and in the artifacts follows STE. PASS.
- Code quality (I): the fix removes copies of logic instead of adding them. Methods stay under 50 lines (the detection target reader is split into small methods). Method names use CamelCase with no underscores. PASS.
- Testing (II): a failing reproduction test is written and shown to fail before the fix (bug-fix rule). Unit and contract tests cover the mapper, the readers, the nested walk, new install, take-over install, update, parity with the save path, and a missing reference image. PASS.
- UX (III): no web UI change. Phone support level: not applicable. PASS.
- Performance (IV): no hot path change. The walk runs only at install. PASS.
- Living documentation (V): update `specs/STATUS.md` and the spec `Status` line. Review `docs/architecture.md` and refresh "Last reviewed" if the text about install or the mapping changes. PASS (planned in tasks).
- Workflow: shard balance rule applies to new tests. Version bump in `installer/versioning/version.override.json` before the PR push. PASS (planned).

Post-design re-check: no new violation. The design adds no project, no endpoint, and no dependency. Complexity Tracking is empty.

## Project Structure

### Documentation (this feature)

```text
specs/177-fix-installed-waitforimage/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── waitforimage-mapping.md   # Internal contract of the shared mapper, the readers, and the walk
└── tasks.md             # Phase 2 output (/speckit-tasks)
```

### Source Code (repository root)

```text
src/GameBot.Domain/Commands/
└── WaitForImageConfigMapper.cs          # NEW: Map, TryReadInt32, TryReadRegion; the only reader of the payload fields
src/GameBot.Domain/Gamepacks/
├── GamepackWaitForImageMapping.cs       # NEW: one recursive walk over Body and ElseBody
└── GamepackValidator.cs                 # CHANGED: call the walk after RewriteCommandIds, before PlannedObject is added
src/GameBot.Service/Endpoints/
└── SequencesEndpoints.cs                # CHANGED: two Map call sites use the Domain Map; MapWaitForImageConfig,
                                         #   MapWaitForImageDetectionTarget, and TryReadInt32 are removed; the save-time
                                         #   region check calls the Domain TryReadRegion and keeps its messages

tests/unit/Gamepacks/                    # NEW: mapper, reader, and walk tests (nested, no-action, runtime type from a pack file)
tests/contract/Gamepacks/                # NEW: install test from a real pack JSON file; parity test (install vs POST /api/sequences);
                                         #   missing reference image test; take-over and update tests
docs/architecture.md                     # REVIEW: refresh "Last reviewed" if text changes
specs/STATUS.md                          # CHANGED: add row 177
```

**Structure Decision**: Keep the single solution layout. Put shared logic in `GameBot.Domain` because Service depends on Domain and not the other way round. Domain code in `Gamepacks` cannot call code in Service.

Region note: `SequencesEndpoints.TryReadRegion` is a Service method today. It moves to Domain as `WaitForImageConfigMapper.TryReadRegion`. It returns the four optional whole numbers and the same error texts (including the step-label prefix). The Service wraps the four numbers in `PixelRegionDto` for `PixelRegionDto.Validate`. Only one region reader stays (spec clarification D1). See research.md decision 4.

## Complexity Tracking

No violation. This table is empty.
