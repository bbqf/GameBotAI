# Implementation Plan: Web UI — Authoring (MVP)

**Branch**: `001-web-ui-authoring` | **Date**: 2025-12-26 | **Spec**: specs/001-web-ui-authoring/spec.md
**Input**: Feature specification from `specs/001-web-ui-authoring/spec.md`

## Summary

Deliver a browser-first Authoring UI to create and manage sequences against the existing GameBot service. MVP includes create and view-by-id, responsive layout, token-gated API client, and validation surfacing. Frontend stack is React + Vite + TypeScript.

## Technical Context

**Language/Version**: TypeScript (ES2020), React 18, Vite 5  
**Primary Dependencies**: React, Vite, @vitejs/plugin-react  
**Storage**: None (frontend only; service uses file-backed JSON)  
**Testing**: NEEDS CLARIFICATION (Jest + RTL recommended)  
**Target Platform**: Web (Chrome/Edge latest), Windows host for service  
**Project Type**: Frontend SPA (MVP)  
**Performance Goals**: p95 create/edit roundtrip < 1.5s (local); error render < 300ms  
**Constraints**: Mobile viewport ≥375px; accessibility WCAG AA labels/contrast  
**Scale/Scope**: MVP: 2 screens (Create, View); extend to list/edit/delete subsequently

## Constitution Check

GATE pre-Phase 0:
- Code Quality: Keep components small and cohesive; pin dependencies; no secrets committed.
- Testing: Adopt Jest + React Testing Library; target ≥80% line coverage in pages/components touched.
- UX Consistency: Clear error messages from 400 responses; consistent labels; versioning not applicable (frontend MVP).
- Performance: Document budgets above; avoid unnecessary re-renders; measure initial interactions.

Re-check after Phase 1 design with contracts and data model finalized.

## Project Structure

### Documentation (this feature)

```text
specs/001-web-ui-authoring/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
└── contracts/
    └── sequences.openapi.yaml
```

### Source Code (repository root)

```text
src/
├── GameBot.Service/           # existing backend (ASP.NET Core Minimal API)
├── GameBot.Domain/            # existing domain
├── GameBot.Emulator/          # existing emulator
└── web-ui/                    # new frontend (React + Vite)
    ├── src/
    │   ├── components/
    │   ├── pages/
    │   └── lib/
    ├── index.html
    └── vite.config.ts
```

**Structure Decision**: Extend existing monorepo with `src/web-ui` for frontend alongside the service.

## Complexity Tracking

No violations identified at this stage.
