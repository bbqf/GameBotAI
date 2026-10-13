# Implementation Plan: Phone UI Queue Activity and Navigation

**Branch**: `140-phone-ui-queues-nav` | **Date**: 2026-10-13 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/140-phone-ui-queues-nav/spec.md`

## Summary

Three changes to the phone layout of the web UI. The service does not change.

1. Remove the Execution area from the phone UI. Add the phone support level `hidden`. A phone request for the old Execution address shows the Queues area and replaces the history entry.
2. Show queue activity. The queue detail shows the state, the "Now" text, the last result, and a schedule list (enabled entries by next run time, then disabled entries). Each queue card shows a one-line summary. Data refreshes every 5 s while the page is visible and the connection is up. The data comes from `GET /api/queues/{id}/monitor` and the queue template (research R-01).
3. Fix back navigation. Replace the single `replaceState` call in `App.tsx` with the History API: `pushState` for each move to a new screen, `popstate` to restore. Each entry stores `idx` in `history.state`. The address holds `area` and `queue`.

## Technical Context

**Language/Version**: TypeScript 5.x, React 18 (existing `src/web-ui`)
**Primary Dependencies**: None new. React, Vite, Jest, React Testing Library (existing). History API and `document.visibilityState` from the browser.
**Storage**: N/A. `history.state` holds `{ idx }`.
**Testing**: Jest and React Testing Library. Gate: `npm test` and `vite build` (lint and `tsc --noEmit` have known old failures).
**Target Platform**: Browsers on phones (320 to 768 CSS px wide) and on desktops.
**Project Type**: Web application (frontend only change)
**Performance Goals**: Data on screen at most 15 s old (SC-002). Poll every 5 s.
**Constraints**: No sideways scroll from 320 to 768 px. Tap targets at least 44 x 44 px. Desktop behavior unchanged (SC-007).
**Scale/Scope**: 1 to 5 queues, up to tens of entries per queue.

No item is marked NEEDS CLARIFICATION. See [research.md](research.md).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

*NON-NEGOTIABLE*: All text in this plan and in the artifacts it produces obeys Simplified Technical English (Principle VI).

*Phone check (Principle III)*: This feature changes the phone layout of the web UI.
- Queues area: support level `reduced` (no change). Start, stop, and enable switches stay. Schedule edit stays desktop-only.
- Execution area: new level `hidden` on the phone. Desktop keeps it.
- Other areas: no change.
- The feature follows `docs/mobile-ui-principles.md`. The plan updates that document (FR-017).

| Principle | Result |
|-----------|--------|
| I. Code Quality | Pass. Small pure helpers and one hook. No new dependency. |
| II. Testing | Pass. Tests for navigation history, schedule ordering, polling, hidden area, redirect. Existing desktop tests stay. |
| III. UX Consistency | Pass. Phone level stated above. Error texts say the next action ("Queue not found" with a button to the list). |
| IV. Performance | Pass. 5 s poll, paused when hidden or offline. One extra monitor request per running queue. |
| V. Living Documentation | Pass. Update `docs/architecture.md` (web UI section, Last reviewed date) and `docs/mobile-ui-principles.md`. Update `specs/STATUS.md` and the `Status` line of spec 139 (iterated by 140). |
| VI. STE | Pass. All new text is in STE. |

Result after Phase 1 design: no change. No violation. The Complexity Tracking table is empty.

## Design Decisions

- D-1: No router. Use the History API (R-04).
- D-2: The schedule list is made in the web UI. The template gives the entries and the enabled state. The monitor gives the next time. A pure function merges them and sorts them (data-model.md, ScheduleRow).
- D-3: The `idx` value in `history.state` decides the on-screen back action. No module flag (R-05).
- D-4: `PhoneSupportLevel` gets the value `hidden`. One record drives the tab bar, the More sheet, and the redirect (R-06).
- D-5: One polling hook, `usePolling`, with visibility and connection rules. The list and the detail both use it (R-02, R-03).
- D-6: Relative time text comes from a pure function that takes `now` as an argument (R-07).
- D-7: The selected queue id lives in `App.tsx` (read from the `queue` address value). The queue detail gets it as a prop from the first task. No task builds the detail with local state first.

## Project Structure

### Documentation (this feature)

```text
specs/140-phone-ui-queues-nav/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── phone-queues-nav-contract.md   # UI behavior rules
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 output (/speckit-tasks)
```

### Source Code (repository root)

```text
src/web-ui/src/
├── App.tsx                                  # History API: push, popstate, redirect, queue in address
├── types/
│   ├── navigation.ts                        # PhoneSupportLevel 'hidden'; execution -> hidden
│   └── __tests__/navigation.test.ts         # Updated: execution hidden, no phone Execution
├── lib/
│   └── phoneHistory.ts                      # New: read/write position, idx in history.state (pure helpers)
├── hooks/
│   └── usePolling.ts                        # New: 5 s poll, pause on hidden page or lost connection
├── components/
│   ├── phone/
│   │   ├── PhoneTabBar.tsx                  # Filter 'hidden' areas (tab bar and More sheet)
│   │   ├── phone.css                        # Styles for activity, schedule rows, card summary
│   │   └── __tests__/PhoneTabBar.test.tsx   # Updated
│   └── queues/
│       ├── PhoneQueuesView.tsx              # Queue in address, activity, schedule, card summary, not-found
│       ├── queueActivity.ts                 # New: pure functions for QueueActivity and ScheduleRow
│       └── __tests__/                       # PhoneQueuesView.test.tsx updated; queueActivity.test.ts new
└── __tests__/                               # App navigation tests (history, redirect, desktop unchanged)

docs/
├── mobile-ui-principles.md                  # Updated (FR-017)
└── architecture.md                          # Updated (web UI section, Last reviewed)
specs/STATUS.md                              # Updated
specs/139-mobile-friendly-ui/spec.md         # Status line: iterated by 140
```

**Structure Decision**: Frontend only. The files above are the exact paths. The tasks step confirms each path against the code and removes any path that is not needed.

## Complexity Tracking

No violation. Table not needed.
