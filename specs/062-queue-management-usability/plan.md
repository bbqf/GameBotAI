# Implementation Plan: Queue Management Usability

**Branch**: `062-queue-management-usability` | **Date**: 2026-06-22 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/062-queue-management-usability/spec.md`

## Summary

Three self-contained usability improvements to the Queues management UI, all confined to the React
web-ui and backed by the existing queue/template HTTP endpoints (no backend changes):

1. **Remove the Sequences (entry-count) column** from the queues overview table.
2. **One-click template save** — saving back to the queue's currently associated template, or under a
   genuinely new name, persists in a single action with no overwrite prompt; only a collision with a
   *different* existing template still asks to confirm.
3. **Co-located save confirmations** — both the queue Save and the template Save show their
   success/failure result inline at the control the user clicked, replacing the page-top status
   banner for these saves.

## Technical Context

**Language/Version**: TypeScript 5.x, React 18 (functional components + hooks)
**Primary Dependencies**: React, Vite (build), Jest + React Testing Library (test); existing
`lib/api` helpers and `services/queueTemplates.ts` / `services/queues.ts`
**Storage**: N/A in UI — persistence via existing `/api/queues` and `/api/queue-templates` endpoints (unchanged)
**Testing**: Jest + React Testing Library (`vite build` + `jest` is the real green gate for web-ui)
**Target Platform**: Browser (web-ui served by the GameBot host)
**Project Type**: Web application — frontend change only (`src/web-ui`)
**Performance Goals**: No hot path; interactions are local state + a single existing API call. No perf-sensitive change.
**Constraints**: Keep existing keyboard/Enter behaviors and ARIA roles intact; preserve template name validation; `colSpan` of expandable rows must track the new column count.
**Scale/Scope**: ~5 component/page files touched plus their tests; no schema, route, or contract changes.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

- **I. Code Quality Discipline**: PASS — small, cohesive component edits; CamelCase preserved; no new
  dependencies; no dead code (the `overwrite` confirmation branch in `SaveTemplateDialog` is repurposed,
  not duplicated).
- **II. Testing Standards**: PASS — every behavior change is covered by Jest/RTL tests (column removal,
  one-click vs. collision-confirm save, inline confirmation on success/failure). Existing tests that
  assert the old behavior (Sequences column, page-top banner text, always-prompt overwrite) are updated
  in the same change. `vite build` + `jest` must be green before commit.
- **III. User Experience Consistency**: PASS — this feature *is* a UX-consistency improvement;
  confirmations become actionable and co-located; error messages remain actionable.
- **IV. Performance Requirements**: PASS — no hot-path impact; no benchmark needed. Documented here as
  required by the constitution.

No violations → Complexity Tracking not required.

## Project Structure

### Documentation (this feature)

```text
specs/062-queue-management-usability/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output (UI state shapes / decision logic)
├── quickstart.md        # Phase 1 output (manual verification)
├── checklists/
│   └── requirements.md  # Spec quality checklist (already passing)
└── tasks.md             # Phase 2 output (/speckit-tasks — not created here)
```

No `contracts/` directory: the feature introduces no new or changed external interface. It reuses the
existing `POST /api/queue-templates` (with its `overwrite` flag and 409-on-collision behavior) and the
existing `PUT /api/queues/{id}` update endpoint, both unchanged.

### Source Code (repository root)

```text
src/web-ui/src/
├── pages/
│   └── QueuesPage.tsx                         # remove Sequences column + colSpan; route save results inline; one-click save decision
├── components/queues/
│   ├── QueueForm.tsx                          # surface inline queue-save confirmation at the Save action row
│   ├── QueueTemplateControls.tsx             # surface inline template-save confirmation at the Save Template row
│   └── SaveTemplateDialog.tsx                 # one-click path: only confirm overwrite on different-template collision
└── pages/__tests__/ , components/queues/__tests__/
    ├── QueuesPage.layout.spec.tsx             # update: assert no Sequences column / column count
    ├── QueuesPage.templates.spec.tsx          # update: inline confirmation location; one-click save
    ├── QueueTemplateControls.test.tsx         # update/extend: inline confirmation rendering
    └── SaveTemplateDialog.test.tsx            # update: one-click vs collision-confirm behavior
```

**Structure Decision**: Single-frontend web application. All changes live under `src/web-ui/src`;
the host/back end is untouched. Existing component boundaries are preserved — no new components are
required (confirmations attach to the existing `QueueForm` and `QueueTemplateControls`).

## Complexity Tracking

No constitution violations; section intentionally empty.
