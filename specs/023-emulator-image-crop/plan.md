# Implementation Plan: Emulator Screenshot Cropping

**Branch**: `022-emulator-image-crop` | **Date**: 2026-01-20 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/022-emulator-image-crop/spec.md`

## Summary

Add an end-to-end flow that captures the current emulator screen, lets the user draw a rectangle, and saves the cropped region as a PNG image with duplicate-name protection and clear save confirmation. Leverage existing emulator session capture and file-backed image storage under `data/images` without introducing new dependencies.

## Technical Context

<!--
  ACTION REQUIRED: Replace the content in this section with the technical details
  for the project. The structure here is presented in advisory capacity to guide
  the iteration process.
-->

**Language/Version**: C# / .NET 9 (frontend: React 18/Vite)
**Primary Dependencies**: Existing GameBot emulator session & ADB capture, System.Drawing/OpenCvSharp already in repo; no new packages.
**Storage**: File-backed images under `data/images` with metadata persisted alongside existing JSON repos if needed.
**Testing**: xUnit integration/unit tests for capture/crop pipeline; Playwright/RTL for web crop UX.
**Target Platform**: Windows
**Project Type**: Backend service + web UI authoring
**Performance Goals**: Capture→crop→save completes in ≤1s p95 for 1080p frames; saved file matches selection within 1px.
**Constraints**: PNG output only; minimum crop 16x16 px; avoid additional background services; reuse current logging/telemetry pipeline.
**Scale/Scope**: Single-user authoring sessions; moderate asset counts (<10k images) stored on disk.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

Validate planned work against the GameBot Constitution:
- Code Quality: Keep capture/crop logic modular; follow existing analyzers; no new deps; security scan and secret checks unchanged.
- Testing: Add xUnit unit/integration tests for capture/crop; Playwright/RTL for rectangle UX; maintain ≥80% line / ≥70% branch coverage touched areas; ensure determinism by using fixed test images.
- UX Consistency: Align messages with existing authoring UI patterns; clear duplicate-name prompts; consistent success/failure notifications.
- Performance: Target ≤1s p95 for capture→crop→save; avoid regressions; measure with representative 1080p frames.

[Validated against `.specify/memory/constitution.md`; no gates blocked.]

## Project Structure

### Documentation (this feature)

```text
specs/[###-feature]/
├── plan.md              # This file (/speckit.plan command output)
├── research.md          # Phase 0 output (/speckit.plan command)
├── data-model.md        # Phase 1 output (/speckit.plan command)
├── quickstart.md        # Phase 1 output (/speckit.plan command)
├── contracts/           # Phase 1 output (/speckit.plan command)
└── tasks.md             # Phase 2 output (/speckit.tasks command - NOT created by /speckit.plan)
```

### Source Code (repository root)

```text
src/
├── GameBot.Service/        # Backend minimal API and emulator integration
├── GameBot.Emulator/       # Emulator session utilities
├── GameBot.Domain/         # Domain models and repositories
└── web-ui/                 # React authoring UI (Vite)

tests/
├── integration/
├── contract/
└── unit/
```

**Structure Decision**: Backend service plus web UI; reuse existing emulator and domain projects; tests live under `tests/` per existing folders.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| None | N/A | N/A |
