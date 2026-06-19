# Quickstart: Drag-and-Drop Scheduling Areas

## What this delivers

The queue template editor shows sequences in four labeled, drag-and-drop areas instead of a flat list:

```
┌─────────────────────────────────────────────────────────────┐
│  Start of execution                       (At Queue Start)   │  ← full width
├───────────────────────────────┬─────────────────────────────┤
│  Once per run   (Once Per Run)│                             │
├───────────────────────────────┤  After every step           │
│  Scheduled            (Timer)  │      (After Every Step)     │
└───────────────────────────────┴─────────────────────────────┘
```

Drag a card **between** areas to change its schedule; drag **within** an area to reorder. New sequences land in **Once per run**.

## Run the web UI locally

```powershell
# From the web UI project
& "C:\src\GameBot\src\web-ui"   # (open this folder)
npm install        # @dnd-kit/* already in package.json
npm run dev        # Vite dev server
```

Open the app, go to **Queues**, edit a queue with a few sequences, and use the four scheduling areas in the template editor section.

## Manual verification (maps to spec acceptance scenarios)

1. **Grouping (US1)**: Open a template containing one of each schedule type → each sequence sits in its matching area; all four areas are visible and labeled, empty ones show a drop hint.
2. **Reassign by drag (US2)**: Drag a card from "Once per run" to "After every step" → its badge becomes "After Every Step". Save the template, reload → card stays in "After every step".
3. **Timer area (US2)**: Drag a card into "Scheduled" → it becomes a Timer with empty time; set a time. Drag it out to "Once per run" → timer controls disappear; drag it back into "Scheduled" → the time you set is restored.
4. **Reorder (US3)**: In an area with A, B, C, drag C above A → order becomes C, A, B; save/reload preserves it.
5. **Default add (US4)**: Add a sequence → it appears at the bottom of "Once per run".

## Automated tests (quality gate: `vite build` + `jest` green)

```powershell
& "C:\src\GameBot\src\web-ui\node_modules\.bin\jest" --config "C:\src\GameBot\src\web-ui\jest.config.cjs"
# or from the project: npm test ; npm run build
```

Test coverage to add:
- `schedulingAreas.test.ts` — pure reducer: R1–R8 from the contract (cross-area reassign for each pair, into/out of Timer with retention, within-area reorder, no-op, default add).
- `QueueSchedulingAreas.test.tsx` — C1–C7: four labeled areas, grouping by schedule type, empty-area hints, badges, Timer-only controls, disabled state.
- `QueuesPage.templates.spec.tsx` (extend) — I1–I4: save/reload round-trip for reassign + reorder, default-to-OncePerRun on add, timer-detail retention vs emission.
- Existing scheduling/queue suite (`QueueEntryList.test.tsx`, `QueuesPage.templates.spec.tsx`) — must stay green (I5); no runtime/API behavior changes.

## Out of scope (do not implement here)

- Any API, scheduler, or stored-model change.
- Keyboard-operable drag-and-drop alternative (explicitly deferred — clarified 2026-06-18).
- Splitting "Timer" into separate time-of-day / relative areas.
