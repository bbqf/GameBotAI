# Implementation Plan: Images Authoring UI

**Branch**: `[021-images-authoring-ui]` | **Date**: 2025-12-30 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/021-images-authoring-ui/spec.md`

## Summary

Add Images authoring pages matching existing authoring UI: list image IDs, open details with rendered image from GET /api/images/{id}, overwrite via PUT for same ID, run detection via POST /api/images/detect with defaults surfaced in UI and tabled results, and delete only when unreferenced by triggers. Update backend GET /api/images/{id} to return stored image content and metadata; enforce 10 MB limit and png/jpg/jpeg types; block deletions with trigger references (409 with blocking IDs). Frontend consumes Images API and surfaces validation, detection, and dependency errors consistently.

## Technical Context

**Language/Version**: C# 13 / .NET 9 (backend), TypeScript ES2020 / React 18 (frontend)  
**Primary Dependencies**: ASP.NET Core Minimal API, existing file-backed image store under `data/images`, React 18 + Vite toolchain  
**Storage**: File-system image blobs and metadata; trigger references from JSON repositories  
**Testing**: xUnit + coverlet for backend; React Testing Library/Playwright for frontend interactions  
**Target Platform**: Windows (per project tooling and ADB/Tesseract dependencies)  
**Project Type**: Web (backend API + React authoring UI)  
**Performance Goals**: Detail preview ≤2s for ≤10 MB images; overwrite completion with refreshed preview ≤5s; delete propagation to list ≤2s  
**Constraints**: 10 MB upload cap; allowed MIME types png/jpg/jpeg; last-write-wins overwrites; deletion blocked if trigger references exist (returns 409)  
**Scale/Scope**: Tens to low hundreds of images; concurrency limited to single-team authoring; file-system storage

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

Planned work aligns with constitution:
- Code Quality: reuse existing .NET/React lint/format/analyzers; keep endpoints small and documented; no new external deps beyond existing stacks.
- Testing: add unit/integration for image endpoints (create/get/put/delete conflict) and frontend UI flows for list/detail/overwrite/delete; target ≥80% line / ≥70% branch on touched code.
- UX Consistency: reuse authoring layout/components; actionable errors for validation and dependency blocks; stable API responses and status codes.
- Performance: budgets declared (2s detail, 5s overwrite, 2s delete propagation); last-write-wins avoids locking; 10 MB limit to bound I/O.

Gate status: PASS (no violations requiring waivers).

## Project Structure

### Documentation (this feature)

```text
specs/021-images-authoring-ui/
├── plan.md              # This file (/speckit.plan command output)
├── research.md          # Phase 0 output (/speckit.plan command)
├── data-model.md        # Phase 1 output (/speckit.plan command)
├── quickstart.md        # Phase 1 output (/speckit.plan command)
├── contracts/           # Phase 1 output (/speckit.plan command)
└── tasks.md             # Phase 2 output (/speckit.tasks command - NOT created by /speckit.plan)
```

### Source Code (repository root)
<!--
  ACTION REQUIRED: Replace the placeholder tree below with the concrete layout
  for this feature. Delete unused options and expand the chosen structure with
  real paths (e.g., apps/admin, packages/something). The delivered plan must
  not include Option labels.
-->

```text
src/
├── GameBot.Service/            # ASP.NET Core Minimal API (images endpoints)
├── GameBot.Domain/             # Domain models/repos (image + trigger references)
├── GameBot.Emulator/           # Emulator integration (unchanged)
└── web-ui/                     # React authoring UI

tests/
├── contract/
├── integration/
└── unit/
```

**Structure Decision**: Use existing backend (GameBot.Service) for Images API updates and frontend (web-ui) for authoring pages; tests live under corresponding test projects.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

No complexity waivers required.
