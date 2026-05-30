# Implementation Plan: Web UI Navigation Restructure

**Branch**: `[019-web-ui-nav]` | **Date**: 2025-12-29 | **Spec**: [specs/019-web-ui-nav/spec.md](specs/019-web-ui-nav/spec.md)
**Input**: Feature specification from `/specs/019-web-ui-nav/spec.md`

## Summary

Restructure the Web UI navigation into three areas (Authoring, Execution placeholder, Configuration) using a top tab bar that collapses to a simple menu on narrow screens. Move Actions/Sequences/Commands under Authoring, surface host/token controls at the top of Configuration, and remove Triggers UI/routes completely (standard app not-found handles any direct hits). Provide an empty-state view for Execution. No backend/API changes; the work is client-side within `src/web-ui`.

## Technical Context

**Language/Version**: TypeScript (ES2020), React 18, Vite 5  
**Primary Dependencies**: react, react-dom, @vitejs/plugin-react, React Testing Library, Jest, Playwright  
**Storage**: N/A (client-side state only)  
**Testing**: Jest + React Testing Library for unit/UI; Playwright for e2e; maintain ≥80% line / ≥70% branch coverage for touched areas per constitution  
**Target Platform**: Web (desktop and small-screen responsive)  
**Project Type**: React SPA (Vite)  
**Performance Goals**: p95 tab switch <150ms after assets loaded; collapsed menu interaction without noticeable layout shift (CLS-equivalent <0.1). Measure on desktop, cold first tab switch after load, with cache enabled.  
**Constraints**: Preserve one-click access between areas; collapse nav to menu near 768px; host/token stay at top of Configuration; no new backend endpoints  
**Scale/Scope**: UI shell/navigation only; existing Actions/Sequences/Commands flows remain intact

## Constitution Check

Pre-Phase check: compliant. Plan uses existing lint (`npm run lint`), Jest + RTL + Playwright for tests, no new dependencies, and declares performance budgets. UX consistency covered via active-state styling, focus visibility, `aria-current`. Performance budget defined for tab switching; no hot-path backend impact. No violations requiring waivers.

## Project Structure

### Documentation (this feature)

```text
specs/019-web-ui-nav/
├── plan.md          # This file (/speckit.plan output)
├── research.md      # Phase 0 output
├── data-model.md    # Phase 1 output
├── quickstart.md    # Phase 1 output
├── contracts/       # Phase 1 output
└── tasks.md         # Phase 2 output (/speckit.tasks)
```

### Source Code (repository root)

```text
src/
├── GameBot.Domain/
├── GameBot.Emulator/
├── GameBot.Service/
└── web-ui/
    ├── src/
    │   ├── components/
    │   ├── pages/
    │   └── styles/
    └── tests/        # Playwright e2e config and specs

tests/
├── unit/
├── integration/
└── contract/
```

**Structure Decision**: Modify only `src/web-ui` (React SPA) plus related tests/config; no backend layout changes required.

## Complexity Tracking

No constitution violations or exceptions are anticipated; table not required.
