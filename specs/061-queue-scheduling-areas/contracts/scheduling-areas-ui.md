# UI / Component Contract: Scheduling Areas Editor

This feature is web-UI-only. **No HTTP API contract changes.** The "contracts" here are the component interfaces and observable interaction behaviors that tests assert against. Existing endpoints (`/api/queues/*`, `/api/queue-templates`) are used unchanged.

## Unchanged API surface (explicit non-goals)

- `POST /api/queue-templates` (save), `GET /api/queue-templates/{id}` (load) — payloads and `ScheduleType` values unchanged (`OncePerRun`, `EveryStep`, `Timer`, `AtQueueStart`).
- `PUT /api/queues/{id}/entries` (`replaceQueueEntries`), `POST/DELETE .../entries` (add/remove) — unchanged.
- No new schedule option; `EveryStep` remains the stored/wire identifier behind the "After every step" label.

## Component: `QueueSchedulingAreas`

Replaces `QueueEntryList` in the editor. Props:

```ts
type QueueSchedulingAreasProps = {
  entries: QueueEntryDto[];                 // runtime entries, in current order
  sequences: SequenceDto[];                 // for the add control + labels
  entrySchedule: Record<string, EntrySchedule>;
  onAdd: (sequenceId: string) => void;      // routes to "Once per run" by default (caller seeds OncePerRun)
  onRemove: (entryId: string) => void;
  // Drag result: the new linear order + the new schedule map (already computed by the pure reducer).
  onReorderAndReassign: (next: { orderedEntryIds: string[]; schedule: Record<string, EntrySchedule> }) => void;
  // Timer detail edits within the "Scheduled" area (reuse existing handlers).
  onTimerTimeChange?: (entryId: string, timerTimeOfDay: string) => void;
  onTimerModeChange?: (entryId: string, mode: TimerMode) => void;
  onTimerRelativeOffsetChange?: (entryId: string, offset: string) => void;
  disabled?: boolean;
};
```

### Observable behaviors (test assertions)

| ID | Behavior |
|----|----------|
| C1 | Renders exactly four areas with accessible labels "Start of execution", "Once per run", "Scheduled", "After every step". |
| C2 | Each entry renders as a card in the area matching `entrySchedule[entryId].scheduleType` (default `OncePerRun` when absent). |
| C3 | "Start of execution" is the only full-width area (top); "Once per run" above "Scheduled" on the left; "After every step" on the right. (Layout asserted via container class/structure.) |
| C4 | An empty area still renders with its label and an empty-state hint and is a valid drop target. |
| C5 | A card shows the schedule badge consistent with its area (At Queue Start / After Every Step / Timer / Timer · relative); `OncePerRun` shows no schedule badge. Stale entries keep the stale badge and remain present. |
| C6 | In the "Scheduled" area only, a card exposes timer controls (mode toggle + time-of-day input or relative offset inputs). |
| C7 | When `disabled`, cards are not draggable and schedule controls are disabled, but grouping still renders. |

## Pure module: `schedulingAreas.ts`

```ts
function areaForScheduleType(t: ScheduleType): SchedulingAreaId;
function scheduleTypeForArea(a: SchedulingAreaId): ScheduleType;

function groupEntriesIntoAreas(
  orderedEntryIds: string[],
  schedule: Record<string, EntrySchedule>,
  entries: Record<string, QueueEntryDto>,
): Record<SchedulingAreaId, SchedulingCard[]>;

function applyDragMove(
  state: { orderedEntryIds: string[]; schedule: Record<string, EntrySchedule> },
  move: { entryId: string; targetArea: SchedulingAreaId; targetIndex: number },
): { orderedEntryIds: string[]; schedule: Record<string, EntrySchedule> };
```

### Reducer contract (test assertions)

| ID | Given | When | Then |
|----|-------|------|------|
| R1 | entry in `oncePerRun` | move to `afterEveryStep` | `schedule[id].scheduleType === 'EveryStep'`; card now in `afterEveryStep`; persisted-order updated |
| R2 | entry in `afterEveryStep` | move to `startOfExecution` | `scheduleType === 'AtQueueStart'` |
| R3 | any entry | move to `scheduled` | `scheduleType === 'Timer'`; timer fields empty if none prior |
| R4 | `Timer` entry with `timerTimeOfDay='15:30'` | move out of `scheduled` to `oncePerRun` | `scheduleType === 'OncePerRun'`; `timerTimeOfDay` **retained** in state (inactive) |
| R5 | the R4 entry | move back into `scheduled` | `scheduleType === 'Timer'`; `timerTimeOfDay` still `'15:30'` |
| R6 | three entries A,B,C in one area | move C before A | within-area order becomes C,A,B; no `scheduleType` change |
| R7 | entry | drop at its own current position | state unchanged (no-op) |
| R8 | newly added entry (`OncePerRun`) | — | appears last in `oncePerRun` |
| R9 | entry | cancelled / dropped outside any area (no/`null` target — FR-013) | state unchanged; entry returns to its origin area and position |

## Integration contract: `QueuesPage` save/load round-trip

| ID | Behavior |
|----|----------|
| I1 | Reassigning a card to another area, saving, then reloading the template shows the card in the destination area with its new `scheduleType` (round-trips for every ordered area pair). |
| I2 | Reordering within an area, saving, reloading preserves the within-area order (and thus the per-type execution order). |
| I3 | Adding a sequence places it in "Once per run"; saving emits it with `scheduleType: 'OncePerRun'`. |
| I4 | A template with a `Timer` card retains `timerTimeOfDay`/`timerRelativeOffset` across save/reload; a card moved out of "Scheduled" before save is emitted with the destination type and **no** timer fields. |
| I5 | Existing scheduling/queue tests pass unchanged (no runtime/API behavior change). |
