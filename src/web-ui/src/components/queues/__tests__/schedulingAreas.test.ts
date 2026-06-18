import {
  areaForScheduleType,
  scheduleTypeForArea,
  groupEntriesIntoAreas,
  applyDragMove,
  CANONICAL_AREA_ORDER,
  SchedulingAreaId,
  SchedulingAreasState,
} from '../schedulingAreas';
import { ScheduleType } from '../../../services/queueTemplates';
import { QueueEntryDto } from '../../../services/queues';
import { EntrySchedule } from '../QueueEntryList';

const ALL_TYPES: ScheduleType[] = ['OncePerRun', 'EveryStep', 'Timer', 'AtQueueStart'];
const ALL_AREAS: SchedulingAreaId[] = ['startOfExecution', 'oncePerRun', 'scheduled', 'afterEveryStep'];

const entry = (entryId: string, over: Partial<QueueEntryDto> = {}): QueueEntryDto => ({
  entryId,
  sequenceId: `seq-${entryId}`,
  sequenceName: entryId.toUpperCase(),
  stale: false,
  ...over,
});

const entriesById = (...ids: string[]): Record<string, QueueEntryDto> =>
  Object.fromEntries(ids.map((id) => [id, entry(id)]));

const sched = (scheduleType: ScheduleType, extra: Partial<EntrySchedule> = {}): EntrySchedule => ({
  scheduleType,
  timerTimeOfDay: '',
  ...extra,
});

// ---------------------------------------------------------------------------
// T003/T004: area ↔ schedule-type mapping
// ---------------------------------------------------------------------------

describe('schedulingAreas mapping helpers', () => {
  it('round-trips every ScheduleType through area and back', () => {
    for (const t of ALL_TYPES) {
      expect(scheduleTypeForArea(areaForScheduleType(t))).toBe(t);
    }
  });

  it('round-trips every SchedulingAreaId through schedule type and back', () => {
    for (const a of ALL_AREAS) {
      expect(areaForScheduleType(scheduleTypeForArea(a))).toBe(a);
    }
  });

  it('maps Timer↔scheduled and EveryStep↔afterEveryStep', () => {
    expect(areaForScheduleType('Timer')).toBe('scheduled');
    expect(areaForScheduleType('EveryStep')).toBe('afterEveryStep');
    expect(scheduleTypeForArea('scheduled')).toBe('Timer');
    expect(scheduleTypeForArea('afterEveryStep')).toBe('EveryStep');
  });

  it('exposes the canonical area order', () => {
    expect(CANONICAL_AREA_ORDER).toEqual(['startOfExecution', 'oncePerRun', 'scheduled', 'afterEveryStep']);
  });
});

// ---------------------------------------------------------------------------
// T005 (US1): groupEntriesIntoAreas
// ---------------------------------------------------------------------------

describe('groupEntriesIntoAreas', () => {
  it('groups each entry into the area matching its scheduleType', () => {
    const schedule = {
      a: sched('AtQueueStart'),
      b: sched('OncePerRun'),
      c: sched('Timer'),
      d: sched('EveryStep'),
    };
    const areas = groupEntriesIntoAreas(['a', 'b', 'c', 'd'], schedule, entriesById('a', 'b', 'c', 'd'));
    expect(areas.startOfExecution.map((x) => x.entryId)).toEqual(['a']);
    expect(areas.oncePerRun.map((x) => x.entryId)).toEqual(['b']);
    expect(areas.scheduled.map((x) => x.entryId)).toEqual(['c']);
    expect(areas.afterEveryStep.map((x) => x.entryId)).toEqual(['d']);
  });

  it('defaults a missing schedule to oncePerRun', () => {
    const areas = groupEntriesIntoAreas(['a'], {}, entriesById('a'));
    expect(areas.oncePerRun.map((x) => x.entryId)).toEqual(['a']);
  });

  it('preserves orderedEntryIds order within an area', () => {
    const schedule = { a: sched('OncePerRun'), b: sched('OncePerRun'), c: sched('OncePerRun') };
    const areas = groupEntriesIntoAreas(['c', 'a', 'b'], schedule, entriesById('a', 'b', 'c'));
    expect(areas.oncePerRun.map((x) => x.entryId)).toEqual(['c', 'a', 'b']);
  });

  it('loses or duplicates no entry across areas', () => {
    const schedule = {
      a: sched('AtQueueStart'),
      b: sched('OncePerRun'),
      c: sched('Timer'),
      d: sched('EveryStep'),
      e: sched('OncePerRun'),
    };
    const ids = ['a', 'b', 'c', 'd', 'e'];
    const areas = groupEntriesIntoAreas(ids, schedule, entriesById(...ids));
    const flat = ALL_AREAS.flatMap((a) => areas[a].map((x) => x.entryId));
    expect(flat.sort()).toEqual([...ids].sort());
    expect(flat).toHaveLength(ids.length);
  });

  it('projects label, stale and schedule onto each card', () => {
    const schedule = { a: sched('Timer', { timerTimeOfDay: '15:30' }) };
    const entries = { a: entry('a', { sequenceName: null, stale: true }) };
    const areas = groupEntriesIntoAreas(['a'], schedule, entries);
    const card = areas.scheduled[0];
    expect(card.label).toBe('seq-a'); // falls back to sequenceId when name is null
    expect(card.stale).toBe(true);
    expect(card.schedule.timerTimeOfDay).toBe('15:30');
  });
});

// ---------------------------------------------------------------------------
// T013 (US2): applyDragMove reassign + timer retention; T020 (US3) reorder;
// T024 (US4) default-append
// ---------------------------------------------------------------------------

const state = (orderedEntryIds: string[], schedule: Record<string, EntrySchedule>): SchedulingAreasState => ({
  orderedEntryIds,
  schedule,
});

describe('applyDragMove — cross-area reassign (R1, R2, R3)', () => {
  it('R1: oncePerRun → afterEveryStep sets EveryStep', () => {
    const s = state(['a'], { a: sched('OncePerRun') });
    const next = applyDragMove(s, { entryId: 'a', targetArea: 'afterEveryStep', targetIndex: 0 });
    expect(next.schedule.a.scheduleType).toBe('EveryStep');
    expect(groupEntriesIntoAreas(next.orderedEntryIds, next.schedule, entriesById('a')).afterEveryStep.map((x) => x.entryId)).toEqual(['a']);
  });

  it('R2: afterEveryStep → startOfExecution sets AtQueueStart', () => {
    const s = state(['a'], { a: sched('EveryStep') });
    const next = applyDragMove(s, { entryId: 'a', targetArea: 'startOfExecution', targetIndex: 0 });
    expect(next.schedule.a.scheduleType).toBe('AtQueueStart');
  });

  it('R3: any → scheduled sets Timer with empty timer when none prior', () => {
    const s = state(['a'], { a: sched('OncePerRun') });
    const next = applyDragMove(s, { entryId: 'a', targetArea: 'scheduled', targetIndex: 0 });
    expect(next.schedule.a.scheduleType).toBe('Timer');
    expect(next.schedule.a.timerTimeOfDay).toBe('');
  });

  it('reassigns across every ordered area pair', () => {
    for (const from of ALL_AREAS) {
      for (const to of ALL_AREAS) {
        if (from === to) continue;
        const s = state(['a'], { a: sched(scheduleTypeForArea(from)) });
        const next = applyDragMove(s, { entryId: 'a', targetArea: to, targetIndex: 0 });
        expect(next.schedule.a.scheduleType).toBe(scheduleTypeForArea(to));
      }
    }
  });
});

describe('applyDragMove — Timer detail retention (R4, R5)', () => {
  it('R4: moving out of scheduled retains timer details inactive', () => {
    const s = state(['a'], { a: sched('Timer', { timerTimeOfDay: '15:30', timerMode: 'timeOfDay' }) });
    const next = applyDragMove(s, { entryId: 'a', targetArea: 'oncePerRun', targetIndex: 0 });
    expect(next.schedule.a.scheduleType).toBe('OncePerRun');
    expect(next.schedule.a.timerTimeOfDay).toBe('15:30');
    expect(next.schedule.a.timerMode).toBe('timeOfDay');
  });

  it('R5: moving back into scheduled restores retained timer details', () => {
    const s = state(['a'], { a: sched('OncePerRun', { timerTimeOfDay: '15:30', timerMode: 'timeOfDay' }) });
    const next = applyDragMove(s, { entryId: 'a', targetArea: 'scheduled', targetIndex: 0 });
    expect(next.schedule.a.scheduleType).toBe('Timer');
    expect(next.schedule.a.timerTimeOfDay).toBe('15:30');
  });

  it('retains a relative offset across the round-trip', () => {
    const s = state(['a'], { a: sched('Timer', { timerMode: 'relative', timerRelativeOffset: '00:10:00' }) });
    const out = applyDragMove(s, { entryId: 'a', targetArea: 'oncePerRun', targetIndex: 0 });
    expect(out.schedule.a.timerRelativeOffset).toBe('00:10:00');
    const back = applyDragMove(out, { entryId: 'a', targetArea: 'scheduled', targetIndex: 0 });
    expect(back.schedule.a.scheduleType).toBe('Timer');
    expect(back.schedule.a.timerRelativeOffset).toBe('00:10:00');
  });
});

describe('applyDragMove — within-area reorder (R6) and no-op (R7)', () => {
  it('R6: within-area reorder changes only order, not scheduleType', () => {
    const schedule = { a: sched('OncePerRun'), b: sched('OncePerRun'), c: sched('OncePerRun') };
    const s = state(['a', 'b', 'c'], schedule);
    const next = applyDragMove(s, { entryId: 'c', targetArea: 'oncePerRun', targetIndex: 0 });
    expect(next.orderedEntryIds).toEqual(['c', 'a', 'b']);
    expect(next.schedule.a.scheduleType).toBe('OncePerRun');
    expect(next.schedule.b.scheduleType).toBe('OncePerRun');
    expect(next.schedule.c.scheduleType).toBe('OncePerRun');
  });

  it('R7: dropping at the current position is a no-op (returns the same state)', () => {
    const schedule = { a: sched('OncePerRun'), b: sched('OncePerRun'), c: sched('OncePerRun') };
    const s = state(['a', 'b', 'c'], schedule);
    const next = applyDragMove(s, { entryId: 'b', targetArea: 'oncePerRun', targetIndex: 1 });
    expect(next).toBe(s);
  });
});

describe('applyDragMove — default-add ordering (R8)', () => {
  it('R8: an appended OncePerRun entry appears last in oncePerRun', () => {
    const schedule = { a: sched('OncePerRun'), b: sched('OncePerRun') };
    // A newly added entry is appended to the ordered list with OncePerRun.
    const s = state(['a', 'b', 'new'], { ...schedule, new: sched('OncePerRun') });
    const areas = groupEntriesIntoAreas(s.orderedEntryIds, s.schedule, entriesById('a', 'b', 'new'));
    expect(areas.oncePerRun.map((x) => x.entryId)).toEqual(['a', 'b', 'new']);
  });

  it('keeps canonical inter-area order when rebuilding the linear order', () => {
    // afterEveryStep entry first in input, but canonical order puts oncePerRun before it.
    const s = state(['d', 'b'], { d: sched('EveryStep'), b: sched('OncePerRun') });
    const next = applyDragMove(s, { entryId: 'b', targetArea: 'scheduled', targetIndex: 0 });
    // b → scheduled; canonical order = startOfExecution, oncePerRun, scheduled, afterEveryStep.
    expect(next.orderedEntryIds).toEqual(['b', 'd']);
  });
});
