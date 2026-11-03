# Implementation Plan: Use default label in the parameter value form

**Branch**: `176-use-default-label` | **Date**: 2026-11-03 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/176-use-default-label/spec.md`

## Summary

Rename the "Inherit" box in the parameter value form to "Use default". Change the accessible name to `Use default <name>`. Show the default value next to the label, or "No default" when the parameter has no default. An empty-string default shows "(empty)". An image default shows its image id. Only visible text, the accessible name, and code comments change. The `INHERIT` sentinel, the resolve rules (feature 078), and the backend do not change.

## Technical Context

**Language/Version**: TypeScript 5 with React 18 (web UI). No backend change.
**Primary Dependencies**: React, Vitest, Testing Library (all existing).
**Storage**: N/A
**Testing**: Vitest. Run `npm --prefix "C:\src\GameBot\src\web-ui" test`. The backend tests stay unchanged and run as a regression check with `dotnet test` and an absolute solution path.
**Target Platform**: Web UI, phone support level: full.
**Project Type**: Web application (web-ui only)
**Performance Goals**: N/A
**Constraints**: Phone width per docs/mobile-ui-principles.md. 44 px touch target. A long default must wrap.
**Scale/Scope**: 3 places use the form (queue template entry, run request, step binding). All use `ParameterBindingForm.tsx`.

## Constitution Check

*NON-NEGOTIABLE*: Build and required tests must pass before implementation continues. Status: pass (no change to build setup).

*NON-NEGOTIABLE*: All text uses Simplified Technical English (Principle VI). Status: pass. The label "Use default", "No default", "(empty)", and the CHANGELOG entry are in STE.

*Phone check (Principle III)*: Phone support level is full. The default text is in a `span` that wraps (`overflow-wrap: anywhere`). The check runs at the narrowest phone width in docs/mobile-ui-principles.md. The box keeps a 44 px touch target.

*Version bump*: Required (FR-011). Set `patch` to the `patch` on `origin/master` plus 1 at push time (CLAUDE.md rule). The plan has no fixed number.

Post-design re-check: pass. No violation. Complexity Tracking is empty.

## Project Structure

### Documentation (this feature)

```text
specs/176-use-default-label/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── ui-contract.md
└── tasks.md             # Created by /speckit.tasks
```

### Source Code (repository root)

```text
src/web-ui/src/
├── styles.css                                     # add .parameter-binding-default rule (shared stylesheet)
└── components/
    ├── parameters/
    │   ├── types.ts                               # add defaultDisplayText()
    │   ├── ParameterBindingForm.tsx               # label, aria-label, default text, comments
    │   ├── ParameterFormPreview.tsx               # comments only (FR-013)
    │   └── __tests__/ParameterBindingForm.test.tsx   # queries and new tests
    └── queues/__tests__/QueueSchedulingAreas.parameters.test.tsx   # queries

CHANGELOG.md                                       # STE entry (FR-010)
specs/STATUS.md                                    # row for 176, same format as row 175 (FR-012)
installer/versioning/version.override.json         # version bump (FR-011)
```

**Structure Decision**: Change the web UI only. The stylesheet is `src/web-ui/src/styles.css`. It holds all `.parameter-*` rules (for example line 216 and line 237). The class `parameter-binding-inherit` has no rule there, so the new class `parameter-binding-default` gets its own rule in that file.

## Design Notes

- `defaultDisplayText(declaration)` returns one of these:
  - `null` or `undefined` default: "No default".
  - Empty string: "(empty)".
  - Choice kind: the option label for the default value. Fall back to the raw value.
  - Image kind: the image id (the raw value).
  - Number and text kind: the raw value.
- Tests cover these default types: choice, number, image, empty, null, undefined (FR-003).
- The accessible name does not include the default text (clarification of 2026-11-03).
- The box stays visible when there is no default (FR-005). Ad-hoc rows and preview mode have no box.

## Complexity Tracking

No violation. No entry needed.
