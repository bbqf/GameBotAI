# Implementation Plan: Clean up the Queues page on the big screen

**Branch**: `173-queues-ui-cleanup` | **Date**: 2026-10-30 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/173-queues-ui-cleanup/spec.md`

## Summary

The Queues list gets icon buttons, an Activity column in place of the Emulator column, and no Schedule button. The Edit page opens for a running queue. It shows the pause controls, a notice of waiting template changes, the live monitor, the settings, and a template-only editor. The phone and the big screen use one shared rule for the activity text and one shared notice component. The service gets three small changes: a pause field in the list, a running queue accepts settings, and the entry calls refuse a running queue.

## Technical Context

**Language/Version**: TypeScript with React (web UI), C# on .NET (service)
**Primary Dependencies**: Existing only. No new package. Icons are inline SVG.
**Storage**: None added. No stored field changes.
**Testing**: Vitest and Testing Library (web UI), xUnit (service), Playwright (the Actions column width, SC-002, and the existing mobile spec)
**Target Platform**: Browser (desktop and phone layout), Windows service
**Project Type**: Web application (web UI and service)
**Performance Goals**: One monitor call for each running queue every 5 s. No time number is set for the first load. A failed monitor call does not block the list.
**Constraints**: The desktop layout tests that do not touch queue rows stay green. The phone follows `docs/mobile-ui-principles.md`. All new text is STE.
**Scale/Scope**: About 1 to 10 queues. About 8 web UI files change, 4 are new, 2 are deleted. 2 service files change (the endpoint file and one response contract).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Gate | Result |
|------|--------|
| Build and test must be green before work goes on | Pass. Master is green (last commit 27dfe61). Tasks check the build in T001 and T045. The strategy runs the tests before each commit. |
| STE in plan and artifacts (Principle VI) | Pass. All artifacts are written in STE. |
| Phone check (Principle III) | Pass. Phone support level: `reduced`. The phone gets the shared activity text, the shared notice, and the existing switches. It gets no icon buttons and no Edit page. |
| I. Code quality | Pass. One shared rule replaces a copy. No underscore in method names. Functions stay short. |
| II. Testing | Pass. Tests for the shared rule (six states), the icon row, the running editor, the notice, and the service changes. A failing test comes first for each changed behavior. |
| III. UX consistency | Pass. Same words on both screens. Error messages state what to do (409 messages). |
| IV. Performance | Pass. Goal stated above. The poll uses the existing `usePolling`. |
| V. Living docs | Action: update `docs/architecture.md` (API surface: list field, running update, entry guard) and refresh "Last reviewed". Update `docs/mobile-ui-principles.md` and `docs/live-template-changes.md`. Update the `Status` line of specs 171 (iterated by 173) and `specs/STATUS.md`. |
| Version | Action: before the PR, set `patch` in `installer/versioning/version.override.json` (master patch plus 1). The change adds an optional response field only, so the minor version stays. |
| Shard balance | Action: if a new test file goes into a shard more than 20% above the average, split it (`scripts/ci/measure-shards.ps1`). |

**Post-design re-check**: Pass. The design adds no stored data, no new dependency, and no new project.

## Project Structure

### Documentation (this feature)

```text
specs/173-queues-ui-cleanup/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── queues-api-and-ui.md
├── checklists/
│   └── requirements.md
└── tasks.md             # Created by /speckit-tasks
```

### Source Code (repository root)

```text
src/GameBot.Service/
├── Endpoints/QueuesEndpoints.cs          # list field; PUT {id} for running; entry guards
└── Contracts/Queues/                     # list response gets operatorPause

src/web-ui/src/
├── pages/QueuesPage.tsx                  # icon row, Activity column, no Schedule, Edit for running
├── services/queues.ts                    # operatorPause on QueueDto
├── components/queues/
│   ├── queueActivity.ts                  # shared activity rule (pause aware)
│   ├── IconButton.tsx                    # new: icon button with label and tooltip
│   ├── QueueRowActions.tsx               # new: the icons of one row
│   ├── TemplateChangesNotice.tsx         # new: shared notice and Review button
│   ├── RunningQueueEditor.tsx            # new: template-only editor for a running queue
│   ├── QueuePauseControls.tsx            # review flag moves to the notice
│   ├── PhoneQueuesView.tsx               # shared rule, notice
│   ├── QueueForm.tsx                     # emulator read-only and settings note for running
│   ├── ReviewTemplateChangesDialog.tsx   # one explanation sentence
│   └── QueueLiveScheduleControl.tsx      # deleted, with its tests
└── __tests__ and components/queues/__tests__   # new and changed tests

src/web-ui/tests/e2e/queues-actions-width.spec.ts   # new: Actions column width (SC-002)

tests/ (service unit and integration tests for the queue endpoints)

docs/architecture.md, docs/mobile-ui-principles.md, docs/live-template-changes.md, CHANGELOG.md
```

**Structure Decision**: Keep the current web application layout. Add the new components beside the queue components. Share logic through `queueActivity.ts`, not through a new module.

## Phases (summary)

1. **Service**: list field, running update, entry guards, with tests first.
2. **Shared rule and notice**: `queueActivity.ts` (pause aware), `TemplateChangesNotice`, then use them on the phone and check the phone tests.
3. **Edit page for a running queue** (US1): monitor, settings, `RunningQueueEditor`, no-template case. Then the Activity column (US2).
4. **Review place** (US5): pause controls and `TemplateChangesNotice` on the Edit page. This comes before the icon work, so the review is always reachable.
5. **Row cleanup** (US3, US4): `IconButton`, `QueueRowActions`, remove the Schedule control.
6. **Phone** (US6, US7): the shared rule and the notice on the phone.
7. **Docs, version, and checks**: docs, status lines, patch bump, the six-state test, the quickstart run.

## Risks

- A setting may change a running run in a way that research R3 did not find. The test in R3 catches it. The Edit page text for that setting changes then.
- The page tests (`QueuesPage.*.spec.tsx`, about 100 cases) search for text buttons. They change to search for icon labels. Change them in phase 3, not later.
- The review needs entries with IDs. A newly linked template may need an upgrade. The notice shows "needs upgrade" and links to the existing upgrade panel.

## Complexity Tracking

No violation. No entry needed.
