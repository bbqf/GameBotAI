# Implementation Plan: Execution Log Grid Cleanup

**Branch**: `050-execution-log-grid` | **Date**: 2026-06-02 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/050-execution-log-grid/spec.md`

## Summary

Replace the Execution Logs page's two-panel (list + detail) layout with a single full-width tree grid. Every row — top-level executions (sequences, stand-alone commands) and every nested sub-element — uses the same columns: `Expand | Timestamp | Name | Type | Status | Additional information`. Rows expand/collapse independently at every level (multi-level, independent state), the former "Execution Detail" panel and all "Open in sequence" deep-link buttons are removed, and the grid scrolls horizontally on narrow viewports.

This is a **frontend-only** change. The existing API (`GET /api/execution-logs` and `GET /api/execution-logs/{id}/subtree`) already returns everything required: top-level `summary`, `executionType`, `finalStatus`, `childCount`, `objectRef`, plus the full recursive sub-element tree (`ExecutionTreeNodeDto` with `label`, `status`, `message`, `appliedDelayMs`, `commandName`, `conditionTrace`, `detailAttributes`). The `getExecutionLogDetail` call and the detail-panel rendering are removed.

## Technical Context

**Language/Version**: TypeScript 5.x, React 18.3 (function components + hooks)
**Primary Dependencies**: React, existing `services/executionLogsApi.ts`, `hooks/useNavigationCollapse`; no new runtime dependencies
**Storage**: N/A — data sourced from existing `/api/execution-logs` endpoints; no persistence changes
**Testing**: Jest + `@testing-library/react` (component/unit), Playwright (e2e) — both already configured in `src/web-ui`
**Target Platform**: Web browser (GameBot web-ui SPA), desktop and phone widths
**Project Type**: Web application — this feature touches only the frontend portion (`src/web-ui`)
**Performance Goals**: Expand/collapse toggle re-render in <100 ms for a typical tree (≤ a few hundred nodes); preserve existing 2 s live-poll cadence for running executions with no added per-toggle network calls (subtree is fetched once per top-level row and cached)
**Constraints**: No backend/API changes; preserve existing filtering, sorting, timestamp-mode, and live-update behavior; keep accessible expand controls (`aria-expanded`, labelled buttons); horizontal scroll (not column hiding/stacking) on narrow widths per clarification
**Scale/Scope**: Default page of 50 top-level entries; each subtree typically tens of nodes; single page component plus its CSS and tests

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

| Principle | Assessment |
|-----------|------------|
| I. Code Quality Discipline | PASS — single cohesive component refactor; `eslint`/`tsc` must stay clean; no dead code left (delete `renderDetail`, detail state, `getExecutionLogDetail` usage, deep-link helper). Functions kept small; recursive row renderer extracted. CamelCase only, **no underscores in method names**. |
| II. Testing Standards | PASS — component tests updated/added for: single full-width grid (no detail panel), per-node independent expansion (sequence → command step → command sub-elements), absence of "Open in sequence" buttons, additional-info column content, and preserved filter/sort/live-update. Existing `ExecutionLogsTree.test.tsx` deep-link test is removed and the nested-visibility test updated to reflect per-node collapse. Tests must pass before commit. |
| III. UX Consistency | PASS — consistent column model at all levels; accessible, labelled expand buttons; messages reused from existing data; no sensitive data exposed. |
| IV. Performance Requirements | PASS — perf note included above; toggle is local state only, subtree cached per row, no N+1 fetches introduced; live-poll cadence unchanged. |

**Result**: No violations. Complexity Tracking not required.

## Project Structure

### Documentation (this feature)

```text
specs/050-execution-log-grid/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output (view-model shapes)
├── quickstart.md        # Phase 1 output (manual verification)
├── contracts/           # Phase 1 output (UI grid contract; no API change)
│   └── execution-log-grid.md
└── checklists/
    └── requirements.md   # Created by /speckit-specify
```

### Source Code (repository root)

```text
src/web-ui/
├── src/
│   ├── pages/
│   │   ├── ExecutionLogs.tsx              # PRIMARY: rewrite to single full-width tree grid
│   │   └── __tests__/
│   │       └── ExecutionLogsTree.test.tsx # UPDATE: per-node expansion, no deep link, no detail panel
│   ├── services/
│   │   └── executionLogsApi.ts            # UNCHANGED (getExecutionLogDetail usage removed from page)
│   └── styles.css                         # UPDATE: grid layout at all levels, remove detail/layout split, horizontal scroll
```

**Structure Decision**: Web application; only the frontend `src/web-ui` is affected. The change is concentrated in the `ExecutionLogs.tsx` page component and its CSS, with test updates alongside. No new files in `src/` other than optional small helpers if extracted; no backend (`src/GameBot.*`) or contract (`specs/openapi.json`) changes.

## Complexity Tracking

> Not applicable — Constitution Check passed with no violations.
