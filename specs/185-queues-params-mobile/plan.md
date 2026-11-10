# Implementation Plan: Sleeker Queue Entries and Parameters on the Phone

**Branch**: `185-queues-params-mobile` | **Date**: 2026-11-09 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/185-queues-params-mobile/spec.md`

## Summary

This feature changes the web UI in two places. The service does not change.

1. **Big screen.** The main row of an entry card shows only the key controls. The schedule controls move into one collapsible **Entry settings panel**. The panel has a Schedule part and then a Parameters part. A closed card shows a one-line schedule summary. The queue editor holds the open state of each panel above the cards. A card then keeps its panel open when it moves to another scheduling area.
2. **Phone.** A schedule row of the queue detail opens a separate **entry page**. The page has its own history entry and the address `?area=queues&queue=<id>&entry=<entryId>`. It shows the schedule as read-only text and the shared parameter form. A save writes the values to the linked template with the call that the enable switch uses.

One shared helper gives the schedule text to the card summary and to the phone rows (FR-004).

## Technical Context

**Language/Version**: TypeScript 5, React 18 (web UI in `src/web-ui`)
**Primary Dependencies**: Vite, `@dnd-kit/core` and `@dnd-kit/sortable` (already used). No new dependency.
**Storage**: None new. Template entries already hold the schedule and `parameterValues`.
**Testing**: Vitest and Testing Library for unit and component tests. Playwright for layout checks (mocked API routes).
**Target Platform**: Browser. Desktop mode and phone mode (`useLayoutMode()`).
**Project Type**: Web application. Only the web UI changes. The service and the API do not change.
**Performance Goals**: A panel opens and closes with no request. The entry page makes one read when it opens (the sequence). Its first content shows in under 1 s on the LAN. This is the budget of the queue detail.
**Constraints**: The phone rules of `docs/mobile-ui-principles.md` apply. Targets are at least 44 px. Inputs use 16 px. There is no sideways scroll from 320 px. No control depends on hover. No action is sent again. The desktop layout changes only in the entry card.
**Scale/Scope**: A queue has up to about 30 entries. The feature adds one phone screen, one shared helper, and one changed card component.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Gate | Result |
|------|--------|
| Build and tests are green before work starts (release blocker) | Pass. Master is green (feature 184 is merged). Task T001 checks it again. |
| Principle VI, STE | Pass after the analysis fixes. All artifacts use STE. Task T041 checks the artifacts, the UI text, the comments, and the docs. |
| Principle III, phone check | Pass. The Queues area stays `reduced`. The parameter values of an entry are `full` on the phone. The schedule editor stays `desktop-only`. The plan follows `docs/mobile-ui-principles.md`. |
| Principle II, tests | Pass. Each new function and component has a test. Playwright checks the layout. |
| Principle IV, performance goal | Pass. See Technical Context. |
| Principle V, living docs | Pass. The plan updates `docs/architecture.md` and `docs/mobile-ui-principles.md`. Specs 173 and 184 get the note "iterated by 185". |
| Version bump before the PR | Task T040. |
| No underscores in method names | Pass. All new names use CamelCase. |

After Phase 1 the result is the same. The design adds no service, no storage, and no dependency. There is no violation to justify.

## Phone Rules (summary)

- One primary action for each screen. On the entry page it is **Save**. Back is a plain button.
- Each tap target is at least 44 px. The shared form works at 375 px (feature 168).
- Text inputs use 16 px. The shared form already does this.
- The new CSS has no `:hover` rule outside `@media (hover: hover)`.
- When the connection is lost, Save is off and the banner shows.
- One press of Save sends one call. A failed save keeps the typed values and does not retry.
- The page uses the same wrapper as the queue detail, so safe areas and `dvh` work.
- The entry page mounts in phone mode only. The inline panel mounts in desktop mode only.

## Project Structure

### Documentation (this feature)

```text
specs/185-queues-params-mobile/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── ui-contract.md
├── checklists/requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
src/web-ui/src/
├── components/queues/
│   ├── SchedulingSequenceCard.tsx      # CHANGE: slim main row, settings button, summary
│   ├── EntrySettingsPanel.tsx          # NEW: Schedule part and Parameters part
│   ├── SchedulingArea.tsx              # CHANGE: pass the open state to each card
│   ├── QueueSchedulingAreas.tsx        # CHANGE: hold the set of open entry IDs
│   ├── QueueSchedulingAreas.css        # CHANGE: row and panel styles
│   ├── scheduleSummary.ts              # NEW: shared schedule text
│   ├── queueActivity.ts                # CHANGE: the schedule text moves to scheduleSummary.ts
│   ├── PhoneQueuesView.tsx             # CHANGE: tappable rows, entry page, leave request prop
│   ├── PhoneEntryPage.tsx              # NEW: the phone entry page
│   ├── PhoneEntryPage.css              # NEW: phone layout of the page
│   └── IconButton.tsx                  # CHANGE: add the `settings` icon
├── components/phone/phone.css          # CHANGE: tappable schedule row
├── lib/
│   ├── phoneHistory.ts                 # CHANGE: add `entryId` to the position and the address
│   ├── phoneLeaveGuard.ts              # NEW: stops a back step when the page has unsaved edits
│   ├── queueEntryToggle.ts             # CHANGE: export the entry mapper
│   └── queueEntryParameters.ts         # NEW: builds the save request for the values of one entry
├── App.tsx                             # CHANGE: hold selectedEntryId, run the leave guard
└── types/navigation.ts                 # no change (the area level stays `reduced`)

src/web-ui/tests/e2e/                   # CHANGE editor-row-height.spec.ts, NEW phone-entry-page.spec.ts
docs/architecture.md                    # CHANGE: phone queue screens, "Last reviewed" date
docs/mobile-ui-principles.md            # CHANGE: entry page, "Last reviewed" date
installer/versioning/version.override.json   # CHANGE: patch bump before the PR
CHANGELOG.md                                 # CHANGE: user-visible entry
```

**Structure Decision**: Only the web UI changes. The shared parameter form (`ParameterBindingForm`) is used as it is on both screens (FR-022).

## Design Notes

- **Open state above the cards.** A card is created again when it moves to another `SchedulingArea` component. Local state in the card would close the panel at once. `QueueSchedulingAreas` therefore holds `openEntryIds`. It passes `open` and `onToggleOpen` down to each card.
- **Settings button.** One icon button with the label `Entry settings for <name>`. It replaces the button `Parameters for <name>`. It shows when the card has the schedule handlers or the parameter handler.
- **Parameters part.** It shows when the sequence declares parameters or the entry has values. In other cases the panel shows the Schedule part and the button `Add a value`. The button keeps the ad-hoc function (research R-4).
- **Summary text.** `scheduleSummary` reads the timer mode first. In relative mode it reads the offset. In time-of-day mode it reads the time. The phone rows and the card both use it. The phone row text does not change.
- **Phone entry page.** The page gets the queue detail, the template, the entry ID, and the connection state. It reads the sequence with `getSequence` once. It holds the draft values. Save sends `saveQueueTemplate` with all entries and the new values of one entry. When the sequence read fails, the page shows the text "The parameters did not load." and turns Save off.
- **Rows that cannot open.** A row with no entry ID is not tappable. The row shows a hint only when the template needs an upgrade.
- **Leave guard.** `phoneLeaveGuard` holds one optional function. The `popstate` handler in `App.tsx` calls it. When it returns true, the handler goes forward one step with `history.go(1)` and raises `leaveRequest` (a counter prop, passed from `App.tsx` through `PhoneQueuesView` to `PhoneEntryPage`). The page then shows the keep-or-discard dialog. The on-screen Back button calls the same guard.
- **No new API.** The phone uses the calls that exist: `getQueue`, `getQueueTemplate`, `getSequence`, and `saveQueueTemplate`.

## Complexity Tracking

No constitution violation. This table stays empty.
