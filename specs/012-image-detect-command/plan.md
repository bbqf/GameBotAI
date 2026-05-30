# Implementation Plan: Commands Based on Detected Image

**Branch**: `005-image-detect-command` | **Date**: 2025-12-05 | **Spec**: `specs/005-image-detect-command/spec.md`
**Input**: Feature specification from `/specs/005-image-detect-command/spec.md`

**Note**: Generated and filled by the /speckit.plan workflow.

## Summary

Enable commands to resolve coordinates at runtime from a uniquely detected reference image on the current screen. Provide per-command confidence (default 0.8) and optional (dx, dy) offsets, clamp final coordinates to screen bounds, and enforce “exactly one detection” before performing coordinate-requiring actions (tap, swipe, drag). Reuse the existing image detection pipeline in `GameBot.Domain.Vision` and expose resolved coordinates to all coordinate-consuming actions.

## Technical Context

**Language/Runtime**: C# / .NET 9 (Domain), .NET 8 (Service)
**Primary Dependencies**: Existing detection pipeline (OpenCvSharp via TemplateMatcher), Windows-only System.Drawing usage guarded with platform attributes; no new external packages.
**Storage**: File-based JSON repositories under `data/` (commands, triggers, config). No new persistence stores; extend command schema to include `DetectionTarget` parameters.
**Testing**: xUnit + FluentAssertions; integration tests for command execution; coverage via coverlet as enforced by repo scripts. Deterministic ordering already ensured in detections.
**Target Platform**: Windows (ADB emulator integration; System.Drawing guarded).
**Project Type**: ASP.NET Core Minimal API service + Domain libraries.
**Performance Goals**: Resolve and apply coordinates within 100 ms in p95 for single-match scenarios (from SC-001). Avoid allocations on hot paths; reuse existing screenshot/detection primitives.
**Constraints**: Must only act when exactly one detection >= threshold; clamp coordinates within emulator screen bounds; maintain logging conventions and categories.
**Scale/Scope**: Commands count O(10-100s); screen sizes up to 1440p typical; detection max-results consistent with existing pipeline (NEEDS CLARIFICATION: exact default max-results used by detection entrypoint).

Open Questions (to resolve in research):
- What is the exact default `maxResults` in the detection pipeline entrypoint? (NEEDS CLARIFICATION)
- Where should `DetectionTarget` live in Domain: `GameBot.Domain.Commands` vs `GameBot.Domain.Actions`? (Propose Commands) (NEEDS CLARIFICATION)
- How do coordinate-consuming actions currently receive coordinates (constructor parameter vs context)? Define an extensibility point for runtime resolution. (NEEDS CLARIFICATION)

## Constitution Check

Planned compliance against the GameBot Constitution:
- Code Quality: No new external deps; platform annotations maintained. Keep functions cohesive (<50 LOC); docstrings for new public types (`DetectionTarget`, `ResolvedCoordinate`). Static analysis warnings (e.g., CA1416) handled with attributes.
- Testing: Add unit tests for coordinate clamping and threshold validation; integration tests to assert unique-detection enforcement and offsets application. Maintain ≥80% line and ≥70% branch coverage in touched areas; determinism verified by existing ordering tests plus new scenarios.
- UX Consistency: Reuse logging category for detections; error messages actionable (“multiple detections: {count}, threshold: {t}”). No breaking API changes; command schema extension documented and versioned in spec/contracts.
- Performance: Adopt SC-001 (≤100 ms p95). Measure with Stopwatch around detection-to-resolution path; avoid redundant image loads; reuse grayscale/heatmap helpers.

Gate evaluation (pre-design): PASS with noted clarifications to be resolved in research.

## Project Structure

### Documentation (this feature)

```text
specs/005-image-detect-command/
├── plan.md              # This file (/speckit.plan output)
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
└── contracts/           # Phase 1 output (OpenAPI/JSON Schema)
```

### Source Code (repository root)

```text
src/
├── GameBot.Domain/
│   ├── Commands/
│   │   ├── DetectionTarget.cs          # NEW: command parameter model + validation
│   │   ├── ResolvedCoordinate.cs       # NEW: value object for resolved coordinates
│   │   └── Execution/
│   │       └── DetectionCoordinateResolver.cs  # NEW: resolves coordinates from detections
│   ├── Services/
│   │   └── CommandExecutionService.cs  # UPDATE: integrate resolver for coordinate-requiring actions
│   └── Vision/                         # existing detection pipeline reused
└── GameBot.Service/
    └── Program.cs                      # UPDATE: no API changes; ensure logging categories available

tests/
├── integration/
│   └── DetectionCommandIntegrationTests.cs   # NEW: unique detection, offsets, thresholds
└── unit/
    ├── DetectionCoordinateResolverTests.cs   # NEW: clamping, validation, base-point logic
    └── DetectionTargetValidationTests.cs     # NEW: confidence range, schema validation
```

**Structure Decision**: Extend Domain Commands with a `DetectionTarget` parameter object and a `DetectionCoordinateResolver` used by command execution. No new projects. Service surface remains unchanged.

## Complexity Tracking

No violations anticipated. No new projects or external dependencies are introduced.
