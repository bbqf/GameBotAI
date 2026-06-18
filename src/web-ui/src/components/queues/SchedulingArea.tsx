import React from 'react';
import { useDroppable } from '@dnd-kit/core';
import { SortableContext, verticalListSortingStrategy } from '@dnd-kit/sortable';
import { DropIndicator, dropIndicatorBefore } from '../DropIndicator';
import { ScheduleType } from '../../services/queueTemplates';
import { TimerMode } from './QueueEntryList';
import { SchedulingSequenceCard } from './SchedulingSequenceCard';
import { AREA_LABELS, SchedulingAreaId, SchedulingCard } from './schedulingAreas';

type SchedulingAreaProps = {
  areaId: SchedulingAreaId;
  cards: SchedulingCard[];
  disabled?: boolean;
  activeId?: string | null;
  overId?: string | null;
  onRemove: (entryId: string) => void;
  onReassign: (entryId: string, scheduleType: ScheduleType) => void;
  onTimerTimeChange?: (entryId: string, timerTimeOfDay: string) => void;
  onTimerModeChange?: (entryId: string, mode: TimerMode) => void;
  onTimerRelativeOffsetChange?: (entryId: string, offset: string) => void;
};

const EMPTY_HINT = 'Drop sequences here.';

export const SchedulingArea: React.FC<SchedulingAreaProps> = ({
  areaId,
  cards,
  disabled,
  activeId,
  overId,
  onRemove,
  onReassign,
  onTimerTimeChange,
  onTimerModeChange,
  onTimerRelativeOffsetChange,
}) => {
  const label = AREA_LABELS[areaId];
  // The area itself is a droppable so empty areas can receive a dragged card.
  const { setNodeRef, isOver } = useDroppable({ id: areaId, data: { areaId, isArea: true } });
  const ids = cards.map((c) => c.entryId);
  const indicatorBefore = dropIndicatorBefore(ids, activeId ?? null, overId ?? null);

  return (
    <section
      ref={setNodeRef}
      className={`scheduling-area scheduling-area--${areaId}${isOver ? ' scheduling-area--over' : ''}`}
      aria-label={label}
      data-area={areaId}
    >
      <h4 className="scheduling-area__heading">{label}</h4>
      <div className="reorderable-list scheduling-area__list">
        {cards.length === 0 ? (
          <div className="empty-state">{EMPTY_HINT}</div>
        ) : (
          <SortableContext items={ids} strategy={verticalListSortingStrategy}>
            {cards.map((card, index) => (
              <React.Fragment key={card.entryId}>
                {indicatorBefore === index && <DropIndicator />}
                <div className="reorderable-list__item">
                  <SchedulingSequenceCard
                    card={card}
                    areaId={areaId}
                    disabled={disabled}
                    onRemove={onRemove}
                    onReassign={onReassign}
                    onTimerTimeChange={onTimerTimeChange}
                    onTimerModeChange={onTimerModeChange}
                    onTimerRelativeOffsetChange={onTimerRelativeOffsetChange}
                  />
                </div>
              </React.Fragment>
            ))}
            {indicatorBefore === cards.length && <DropIndicator />}
          </SortableContext>
        )}
      </div>
    </section>
  );
};
