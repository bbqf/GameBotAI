import React from 'react';
import { useSortable } from '@dnd-kit/sortable';
import { CSS } from '@dnd-kit/utilities';
import { ScheduleType } from '../../services/queueTemplates';
import { TimerMode } from './QueueEntryList';
import { SchedulingAreaId, SchedulingCard } from './schedulingAreas';

type SchedulingSequenceCardProps = {
  card: SchedulingCard;
  areaId: SchedulingAreaId;
  disabled?: boolean;
  onRemove: (entryId: string) => void;
  /** Non-drag reassign path: change the card's schedule option (moves it to the matching area). */
  onReassign: (entryId: string, scheduleType: ScheduleType) => void;
  onTimerTimeChange?: (entryId: string, timerTimeOfDay: string) => void;
  onTimerModeChange?: (entryId: string, mode: TimerMode) => void;
  onTimerRelativeOffsetChange?: (entryId: string, offset: string) => void;
};

const SCHEDULE_LABELS: Record<ScheduleType, string> = {
  OncePerRun: 'Once Per Run',
  // Display label only — the stored/wire identifier remains "EveryStep" (FR-002/FR-010).
  EveryStep: 'After Every Step',
  Timer: 'Timer',
  AtQueueStart: 'At Queue Start',
};

const pad2 = (n: number): string => n.toString().padStart(2, '0');

const parseOffset = (raw: string | undefined): { h: number; m: number; s: number } => {
  const [h, m, s] = (raw ?? '').split(':').map((x) => parseInt(x, 10));
  return {
    h: Number.isFinite(h) ? h : 0,
    m: Number.isFinite(m) ? m : 0,
    s: Number.isFinite(s) ? s : 0,
  };
};

const composeOffset = (h: number, m: number, s: number): string => `${pad2(h)}:${pad2(m)}:${pad2(s)}`;

export const SchedulingSequenceCard: React.FC<SchedulingSequenceCardProps> = ({
  card,
  areaId,
  disabled,
  onRemove,
  onReassign,
  onTimerTimeChange,
  onTimerModeChange,
  onTimerRelativeOffsetChange,
}) => {
  const { attributes, listeners, setNodeRef, transform, transition, isDragging } = useSortable({
    id: card.entryId,
    data: { areaId },
    disabled,
  });

  const style: React.CSSProperties = {
    transform: transform ? CSS.Translate.toString(transform) : undefined,
    transition,
    opacity: isDragging ? 0.4 : undefined,
    position: 'relative',
  };

  const schedule = card.schedule;
  const label = card.label;
  const isScheduled = areaId === 'scheduled';
  const timerMode: TimerMode = schedule.timerMode ?? (schedule.timerRelativeOffset ? 'relative' : 'timeOfDay');
  const isRelative = isScheduled && timerMode === 'relative';
  const offsetParts = parseOffset(schedule.timerRelativeOffset);

  const emitOffset = (h: number, m: number, s: number) => {
    // Client-side non-negative validation: never emit a negative component (FR-021).
    if (h < 0 || m < 0 || s < 0) return;
    onTimerRelativeOffsetChange?.(card.entryId, composeOffset(h, m, s));
  };

  return (
    <div ref={setNodeRef} style={style} className="scheduling-card" data-testid="queue-entry">
      <span
        className="scheduling-card__handle"
        aria-label="Drag to reorder"
        title="Drag to reorder"
        {...attributes}
        {...listeners}
        style={{ cursor: disabled ? 'not-allowed' : 'grab', userSelect: 'none', padding: '0 6px', color: '#888' }}
      >
        ⠿
      </span>

      <span className="scheduling-card__name">
        {label}
        {card.stale && <span className="badge badge-warning" role="status"> (stale)</span>}
        {areaId === 'startOfExecution' && <span className="badge badge-info" role="status" aria-label="At Queue Start"> At Queue Start</span>}
        {areaId === 'afterEveryStep' && <span className="badge badge-info" role="status" aria-label="After Every Step"> After Every Step</span>}
        {isScheduled && !isRelative && <span className="badge badge-info" role="status" aria-label="Timer"> Timer</span>}
        {isRelative && <span className="badge badge-info" role="status" aria-label="Relative timer"> Timer · relative</span>}
      </span>

      <select
        aria-label={`Schedule type for ${label}`}
        value={schedule.scheduleType}
        disabled={disabled}
        onChange={(e) => onReassign(card.entryId, e.target.value as ScheduleType)}
      >
        {(Object.keys(SCHEDULE_LABELS) as ScheduleType[]).map((t) => (
          <option key={t} value={t}>{SCHEDULE_LABELS[t]}</option>
        ))}
      </select>

      {isScheduled && onTimerModeChange && (
        <select
          aria-label={`Timer mode for ${label}`}
          value={timerMode}
          disabled={disabled}
          onChange={(e) => onTimerModeChange(card.entryId, e.target.value as TimerMode)}
        >
          <option value="timeOfDay">Time of day</option>
          <option value="relative">Relative</option>
        </select>
      )}

      {isScheduled && !isRelative && onTimerTimeChange && (
        <input
          type="time"
          aria-label={`Timer time for ${label}`}
          value={schedule.timerTimeOfDay}
          disabled={disabled}
          onChange={(e) => onTimerTimeChange(card.entryId, e.target.value)}
        />
      )}

      {isRelative && onTimerRelativeOffsetChange && (
        <span className="timer-relative-offset" aria-label={`Relative offset for ${label}`}>
          <input
            type="number"
            min={0}
            aria-label={`Offset hours for ${label}`}
            value={offsetParts.h}
            disabled={disabled}
            onChange={(e) => emitOffset(parseInt(e.target.value, 10) || 0, offsetParts.m, offsetParts.s)}
          />
          <span aria-hidden="true">h</span>
          <input
            type="number"
            min={0}
            max={59}
            aria-label={`Offset minutes for ${label}`}
            value={offsetParts.m}
            disabled={disabled}
            onChange={(e) => emitOffset(offsetParts.h, parseInt(e.target.value, 10) || 0, offsetParts.s)}
          />
          <span aria-hidden="true">m</span>
          <input
            type="number"
            min={0}
            max={59}
            aria-label={`Offset seconds for ${label}`}
            value={offsetParts.s}
            disabled={disabled}
            onChange={(e) => emitOffset(offsetParts.h, offsetParts.m, parseInt(e.target.value, 10) || 0)}
          />
          <span aria-hidden="true">s</span>
        </span>
      )}

      <button
        type="button"
        onClick={() => onRemove(card.entryId)}
        disabled={disabled}
        aria-label={`Remove ${label}`}
      >
        Remove
      </button>
    </div>
  );
};
