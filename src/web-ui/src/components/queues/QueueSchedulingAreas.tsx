import React, { useMemo, useState } from 'react';
import {
  CollisionDetection,
  DndContext,
  DragEndEvent,
  DragOverEvent,
  DragStartEvent,
  PointerSensor,
  pointerWithin,
  useSensor,
  useSensors,
} from '@dnd-kit/core';
import { SearchableDropdown, SearchableOption } from '../SearchableDropdown';
import { QueueEntryDto } from '../../services/queues';
import { SequenceDto } from '../../services/sequences';
import { ScheduleType } from '../../services/queueTemplates';
import { EntrySchedule, TimerMode } from './QueueEntryList';
import { SchedulingArea } from './SchedulingArea';
import {
  applyDragMove,
  areaForScheduleType,
  CANONICAL_AREA_ORDER,
  DEFAULT_SCHEDULE,
  groupEntriesIntoAreas,
  SchedulingAreaId,
  SchedulingAreasState,
} from './schedulingAreas';
import './QueueSchedulingAreas.css';

export type QueueSchedulingAreasProps = {
  entries: QueueEntryDto[];
  sequences: SequenceDto[];
  entrySchedule: Record<string, EntrySchedule>;
  onAdd: (sequenceId: string) => void;
  onRemove: (entryId: string) => void;
  /** Drag result: the new linear order + the new schedule map (already computed by the pure reducer). */
  onReorderAndReassign: (next: SchedulingAreasState) => void;
  onTimerTimeChange?: (entryId: string, timerTimeOfDay: string) => void;
  onTimerModeChange?: (entryId: string, mode: TimerMode) => void;
  onTimerRelativeOffsetChange?: (entryId: string, offset: string) => void;
  disabled?: boolean;
};

const isAreaId = (id: string): id is SchedulingAreaId =>
  (CANONICAL_AREA_ORDER as string[]).includes(id);

/**
 * Pointer-based collision detection: the droppable directly under the cursor is the target,
 * rather than the dragged card's center (which is offset from the drag handle and feels
 * counter-intuitive). A card under the pointer wins over its enclosing area so the reorder
 * index is precise; empty space within an area falls back to the area itself. The dragged card
 * is excluded so it never targets itself, and releasing outside every area resolves to no target
 * (a no-op that returns the card to its origin — FR-013).
 */
const collisionDetectionStrategy: CollisionDetection = (args) => {
  const pointerCollisions = pointerWithin(args).filter((c) => c.id !== args.active.id);
  const cardCollision = pointerCollisions.find((c) => !isAreaId(String(c.id)));
  return cardCollision ? [cardCollision] : pointerCollisions;
};

export const QueueSchedulingAreas: React.FC<QueueSchedulingAreasProps> = ({
  entries,
  sequences,
  entrySchedule,
  onAdd,
  onRemove,
  onReorderAndReassign,
  onTimerTimeChange,
  onTimerModeChange,
  onTimerRelativeOffsetChange,
  disabled,
}) => {
  const [selected, setSelected] = useState<string | undefined>(undefined);
  const [activeId, setActiveId] = useState<string | null>(null);
  const [overId, setOverId] = useState<string | null>(null);

  const sensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 5 } }));

  const orderedEntryIds = useMemo(() => entries.map((e) => e.entryId), [entries]);
  const entriesById = useMemo(
    () => Object.fromEntries(entries.map((e) => [e.entryId, e])),
    [entries]
  );
  const state: SchedulingAreasState = { orderedEntryIds, schedule: entrySchedule };
  const areas = useMemo(
    () => groupEntriesIntoAreas(orderedEntryIds, entrySchedule, entriesById),
    [orderedEntryIds, entrySchedule, entriesById]
  );

  const options = useMemo<SearchableOption[]>(
    () => sequences.map((s) => ({ value: s.id, label: s.name || s.id })),
    [sequences]
  );

  /** Resolve the drop target (area + final index) from the dnd-kit `over` id. */
  const resolveTarget = (overTarget: string): { area: SchedulingAreaId; index: number } | null => {
    if (isAreaId(overTarget)) {
      return { area: overTarget, index: areas[overTarget].length };
    }
    for (const area of CANONICAL_AREA_ORDER) {
      const idx = areas[area].findIndex((c) => c.entryId === overTarget);
      if (idx !== -1) return { area, index: idx };
    }
    return null;
  };

  const handleDragStart = (event: DragStartEvent) => {
    setActiveId(event.active.id as string);
    setOverId(null);
  };

  const handleDragOver = (event: DragOverEvent) => {
    setOverId((event.over?.id as string) ?? null);
  };

  const handleDragEnd = (event: DragEndEvent) => {
    const { active, over } = event;
    setActiveId(null);
    setOverId(null);
    // FR-013: a cancelled drag or a drop outside any area leaves state unchanged.
    if (!over) return;
    const target = resolveTarget(over.id as string);
    if (!target) return;
    const next = applyDragMove(state, { entryId: active.id as string, targetArea: target.area, targetIndex: target.index });
    if (next === state) return; // no-op
    onReorderAndReassign(next);
  };

  const handleDragCancel = () => {
    setActiveId(null);
    setOverId(null);
  };

  // Non-drag reassign path (schedule selector): move the card to the matching area, appended.
  const handleReassign = (entryId: string, scheduleType: ScheduleType) => {
    const targetArea = areaForScheduleType(scheduleType);
    if (areaForScheduleType((entrySchedule[entryId] ?? DEFAULT_SCHEDULE).scheduleType) === targetArea) return;
    const targetIndex = areas[targetArea].length;
    const next = applyDragMove(state, { entryId, targetArea, targetIndex });
    if (next === state) return;
    onReorderAndReassign(next);
  };

  return (
    <section className="queue-entries scheduling-areas" aria-label="Queue sequences">
      <DndContext
        sensors={sensors}
        collisionDetection={collisionDetectionStrategy}
        onDragStart={handleDragStart}
        onDragOver={handleDragOver}
        onDragEnd={handleDragEnd}
        onDragCancel={handleDragCancel}
      >
        <div className="scheduling-areas__grid">
          <div className="scheduling-areas__top">
            <SchedulingArea
              areaId="startOfExecution"
              cards={areas.startOfExecution}
              disabled={disabled}
              activeId={activeId}
              overId={overId}
              onRemove={onRemove}
              onReassign={handleReassign}
              onTimerTimeChange={onTimerTimeChange}
              onTimerModeChange={onTimerModeChange}
              onTimerRelativeOffsetChange={onTimerRelativeOffsetChange}
            />
          </div>
          <div className="scheduling-areas__body">
            <div className="scheduling-areas__left">
              <SchedulingArea
                areaId="oncePerRun"
                cards={areas.oncePerRun}
                disabled={disabled}
                activeId={activeId}
                overId={overId}
                onRemove={onRemove}
                onReassign={handleReassign}
                onTimerTimeChange={onTimerTimeChange}
                onTimerModeChange={onTimerModeChange}
                onTimerRelativeOffsetChange={onTimerRelativeOffsetChange}
              />
              <SchedulingArea
                areaId="scheduled"
                cards={areas.scheduled}
                disabled={disabled}
                activeId={activeId}
                overId={overId}
                onRemove={onRemove}
                onReassign={handleReassign}
                onTimerTimeChange={onTimerTimeChange}
                onTimerModeChange={onTimerModeChange}
                onTimerRelativeOffsetChange={onTimerRelativeOffsetChange}
              />
            </div>
            <div className="scheduling-areas__right">
              <SchedulingArea
                areaId="afterEveryStep"
                cards={areas.afterEveryStep}
                disabled={disabled}
                activeId={activeId}
                overId={overId}
                onRemove={onRemove}
                onReassign={handleReassign}
                onTimerTimeChange={onTimerTimeChange}
                onTimerModeChange={onTimerModeChange}
                onTimerRelativeOffsetChange={onTimerRelativeOffsetChange}
              />
            </div>
          </div>
        </div>
      </DndContext>

      <div className="queue-entry-add">
        <SearchableDropdown
          id="queue-add-sequence"
          label="Add sequence"
          value={selected}
          options={options}
          placeholder="Select a sequence…"
          disabled={disabled}
          onChange={(v) => setSelected(v)}
        />
        <button
          type="button"
          disabled={disabled || !selected}
          onClick={() => {
            if (!selected) return;
            onAdd(selected);
            setSelected(undefined);
          }}
        >
          Add
        </button>
      </div>
    </section>
  );
};
