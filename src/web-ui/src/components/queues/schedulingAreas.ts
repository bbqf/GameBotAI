import { ScheduleType } from '../../services/queueTemplates';
import { QueueEntryDto } from '../../services/queues';
import { EntrySchedule } from './QueueEntryList';

/**
 * Pure view-model for the four scheduling areas in the queue template editor.
 *
 * The editor groups runtime entries into four labeled areas — one per `ScheduleType` —
 * and lets the operator drag cards between areas (auto-changing the schedule option) and
 * within an area (reordering execution). All logic here is pure so the drag/reorder/reassign
 * decisions are unit-testable without simulating pointer events (jsdom cannot do real DnD).
 */

export type SchedulingAreaId = 'startOfExecution' | 'oncePerRun' | 'scheduled' | 'afterEveryStep';

/** Operator-facing labels. The wire/stored identifier behind "After every step" stays `EveryStep`. */
export const AREA_LABELS: Record<SchedulingAreaId, string> = {
  startOfExecution: 'Start of execution',
  oncePerRun: 'Once per run',
  scheduled: 'Scheduled',
  afterEveryStep: 'After every step',
};

/**
 * Fixed inter-area order used to flatten the four areas into a single linear order for
 * display + save. Invisible to execution (each schedule type runs in its own pass); it only
 * needs to be stable so the positional template save/restore stays correct.
 */
export const CANONICAL_AREA_ORDER: SchedulingAreaId[] = [
  'startOfExecution',
  'oncePerRun',
  'scheduled',
  'afterEveryStep',
];

const AREA_TO_SCHEDULE: Record<SchedulingAreaId, ScheduleType> = {
  startOfExecution: 'AtQueueStart',
  oncePerRun: 'OncePerRun',
  scheduled: 'Timer',
  afterEveryStep: 'EveryStep',
};

const SCHEDULE_TO_AREA: Record<ScheduleType, SchedulingAreaId> = {
  AtQueueStart: 'startOfExecution',
  OncePerRun: 'oncePerRun',
  Timer: 'scheduled',
  EveryStep: 'afterEveryStep',
};

export const scheduleTypeForArea = (areaId: SchedulingAreaId): ScheduleType => AREA_TO_SCHEDULE[areaId];
export const areaForScheduleType = (scheduleType: ScheduleType): SchedulingAreaId => SCHEDULE_TO_AREA[scheduleType];

/** Default schedule for an entry with no recorded schedule (FR-007: new entries are OncePerRun). */
export const DEFAULT_SCHEDULE: EntrySchedule = { scheduleType: 'OncePerRun', timerTimeOfDay: '' };

/** A presentational projection of one entry within an area. */
export type SchedulingCard = {
  entryId: string;
  sequenceId: string;
  label: string;
  stale: boolean;
  schedule: EntrySchedule;
};

export type SchedulingAreasState = {
  orderedEntryIds: string[];
  schedule: Record<string, EntrySchedule>;
};

export type DragMove = {
  entryId: string;
  targetArea: SchedulingAreaId;
  /** Final resting index within the target area's cards (after the dragged card is removed). */
  targetIndex: number;
};

const scheduleOf = (schedule: Record<string, EntrySchedule>, entryId: string): EntrySchedule =>
  schedule[entryId] ?? DEFAULT_SCHEDULE;

const emptyBuckets = (): Record<SchedulingAreaId, string[]> => ({
  startOfExecution: [],
  oncePerRun: [],
  scheduled: [],
  afterEveryStep: [],
});

/** Bucket entry ids into their current areas, preserving the given linear order within each area. */
const bucketEntryIds = (
  orderedEntryIds: string[],
  schedule: Record<string, EntrySchedule>,
): Record<SchedulingAreaId, string[]> => {
  const buckets = emptyBuckets();
  for (const id of orderedEntryIds) {
    buckets[areaForScheduleType(scheduleOf(schedule, id).scheduleType)].push(id);
  }
  return buckets;
};

/**
 * Group entries into the four areas for rendering. Each entry lands in the area matching its
 * current `scheduleType` (default `OncePerRun`), ordered per `orderedEntryIds`. No entry is
 * lost or duplicated; ids without a matching entry are skipped.
 */
export const groupEntriesIntoAreas = (
  orderedEntryIds: string[],
  schedule: Record<string, EntrySchedule>,
  entriesById: Record<string, QueueEntryDto>,
): Record<SchedulingAreaId, SchedulingCard[]> => {
  const areas: Record<SchedulingAreaId, SchedulingCard[]> = {
    startOfExecution: [],
    oncePerRun: [],
    scheduled: [],
    afterEveryStep: [],
  };
  for (const entryId of orderedEntryIds) {
    const entry = entriesById[entryId];
    if (!entry) continue;
    const sched = scheduleOf(schedule, entryId);
    areas[areaForScheduleType(sched.scheduleType)].push({
      entryId: entry.entryId,
      sequenceId: entry.sequenceId,
      label: entry.sequenceName ?? entry.sequenceId,
      stale: entry.stale,
      schedule: sched,
    });
  }
  return areas;
};

const sameOrder = (a: string[], b: string[]): boolean =>
  a.length === b.length && a.every((x, i) => x === b[i]);

/**
 * Apply a drag move (cross-area reassign or within-area reorder) and return the new state.
 *
 * - Cross-area: sets `schedule[entryId].scheduleType` to the destination area's type. Timer
 *   detail fields (`timerTimeOfDay`/`timerRelativeOffset`/`timerMode`) are RETAINED in state so
 *   they are restored if the card is dragged back into "Scheduled" (FR-009); they are only
 *   emitted on save when the current type is `Timer`.
 * - Within-area: only reorders `orderedEntryIds`; `scheduleType` is unchanged (FR-005).
 * - No-op (same area + resulting order unchanged): returns the original `state` reference.
 *
 * The linear order is always rebuilt in canonical inter-area order with the operator's
 * within-area order preserved.
 */
export const applyDragMove = (state: SchedulingAreasState, move: DragMove): SchedulingAreasState => {
  const { entryId, targetArea, targetIndex } = move;
  const currentSchedule = scheduleOf(state.schedule, entryId);
  const sourceArea = areaForScheduleType(currentSchedule.scheduleType);

  const buckets = bucketEntryIds(state.orderedEntryIds, state.schedule);

  // Remove from source, then insert at the clamped target index within the target area.
  buckets[sourceArea] = buckets[sourceArea].filter((id) => id !== entryId);
  const insertAt = Math.max(0, Math.min(targetIndex, buckets[targetArea].length));
  buckets[targetArea] = [
    ...buckets[targetArea].slice(0, insertAt),
    entryId,
    ...buckets[targetArea].slice(insertAt),
  ];

  const orderedEntryIds = CANONICAL_AREA_ORDER.flatMap((area) => buckets[area]);

  // No-op: staying in the same area with no change to the linear order leaves state untouched.
  if (sourceArea === targetArea && sameOrder(orderedEntryIds, state.orderedEntryIds)) {
    return state;
  }

  let schedule = state.schedule;
  if (sourceArea !== targetArea) {
    schedule = {
      ...state.schedule,
      [entryId]: { ...currentSchedule, scheduleType: scheduleTypeForArea(targetArea) },
    };
  }

  return { orderedEntryIds, schedule };
};
