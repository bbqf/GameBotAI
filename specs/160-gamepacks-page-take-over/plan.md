# Implementation Plan: Gamepacks Page Take-Over Option

**Branch**: `160-gamepacks-page-take-over` | **Date**: 2026-10-22 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/160-gamepacks-page-take-over/spec.md`

## Summary

The Gamepacks page gets one take-over check box next to the Dry run check box. It applies to the Install button and
the Update button of each catalog version. The page sends the check box state as `takeOver` in the dry run and in the
install. The dry run notice shows `Take over N.` for each dry run made with take-over on. With take-over on, a real
install needs a valid dry run of the same version made with take-over on. Else the button is disabled and the row shows
`Run the dry run with take-over first.` A change of the take-over box clears the dry run result. A change of the Dry run
box keeps it. The API and the backend do not change.

## Technical Context

**Language/Version**: TypeScript, React (web UI in `src/web-ui`)
**Primary Dependencies**: React, existing service `src/web-ui/src/services/gamepackCatalog.ts`
**Storage**: N/A (page state only; the choice is not kept between visits)
**Testing**: Jest (`npm test` runs `jest`), with React Testing Library
**Target Platform**: Web UI, desktop and phone browsers
**Project Type**: Web application (front end change only)
**Performance Goals**: No change
**Constraints**: Phone support level full (docs/mobile-ui-principles.md): 44 px touch target, no horizontal scroll at 375 px
**Scale/Scope**: One page, one service function, one new check box

## Constitution Check

*GATE: Passed before Phase 0. Re-checked after Phase 1.*

- Code quality (I): small change in two source files; no new dependency. Pass.
- Testing (II): Jest tests for the page and the service (see Test Plan). Build and tests must pass before progress. Pass.
- UX consistency (III): the check box reuses the class `gamepacks-check`. Phone support level: full. It follows
  docs/mobile-ui-principles.md. A manual check at 375 px is part of the PR text. Pass.
- Performance (IV): no change. Pass.
- Living documentation (V): the docs task updates `docs/gamepacks.md` (take-over option and the file-install gap).
  It greps "Gamepacks" in `docs/architecture.md` and updates "Last reviewed" if the change affects a match. Pass.
- STE (VI): all text in the artifacts and the UI strings use Simplified Technical English. Pass.

No violation. Complexity Tracking is not needed.

## Project Structure

### Documentation (this feature)

```text
specs/160-gamepacks-page-take-over/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── catalog-install-ui.md
└── tasks.md             # Created by /speckit-tasks
```

### Source Code (repository root)

```text
src/web-ui/src/
├── services/
│   ├── gamepackCatalog.ts                       # installFromCatalog gets takeOver; result gets takenOver
│   └── __tests__/gamepackCatalog.test.ts        # Created by the setup task (absent today)
├── pages/
│   ├── GamepacksPage.tsx                        # check box, lastDryRun state, disabled rule, notice text
│   └── __tests__/GamepacksPage.test.tsx         # Page tests
└── (phone.css: no change unless long text overflows at 375 px)

docs/gamepacks.md                                # Take-over option and file-install gap
specs/STATUS.md                                  # Row for feature 160 (final task)
```

**Structure Decision**: Front end only. No backend file changes (FR-009).

## Test Plan (Jest)

- Page: the check box is shown and off at load.
- Page: take-over off, dry run finds conflicts: the page lists them and the check box is available (FR-005).
- Page: the dry run request sends `takeOver` false when the box is off, and true when it is on.
- Page: the notice shows `Take over N.` for a dry run made with take-over on (valid or not), and `Take over 0.` when N is 0.
- Page: the notice has no count for a dry run made with take-over off.
- Page: a change of the take-over box clears the notice and disables both Install and Update (take-over on).
- Page: a change of the Dry run box keeps the last result.
- Page: a valid dry run of version A with take-over on does not enable the button of version B; the row of B shows the hint.
- Page: a real install sends `takeOver` true after a valid matching dry run, and false when the box is off (with or without dry run).
- Service: `installFromCatalog` sends `{ name, version, dryRun, takeOver }`.

## Phase 0 and Phase 1 Output

- research.md: decisions D1 to D6 (no dialogs, no file install, optional dry run rule, stale result, count text, phone).
- data-model.md: page state and request fields.
- contracts/catalog-install-ui.md: service signature, controls, messages.
- quickstart.md: manual check and Jest command.
