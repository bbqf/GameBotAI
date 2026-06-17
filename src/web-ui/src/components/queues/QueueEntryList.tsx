import React, { useMemo, useState } from 'react';
import { SearchableDropdown, SearchableOption } from '../SearchableDropdown';
import { QueueEntryDto } from '../../services/queues';
import { SequenceDto } from '../../services/sequences';
import { ScheduleType } from '../../services/queueTemplates';

export type TimerMode = 'timeOfDay' | 'relative';

export type EntrySchedule = {
  scheduleType: ScheduleType;
  timerTimeOfDay: string;
  /** Which Timer sub-mode is active. Defaults to time-of-day when unset. */
  timerMode?: TimerMode;
  /** Relative-mode offset as "HH:mm:ss". */
  timerRelativeOffset?: string;
};

type QueueEntryListProps = {
  entries: QueueEntryDto[];
  sequences: SequenceDto[];
  onAdd: (sequenceId: string) => void;
  onRemove: (entryId: string) => void;
  entrySchedule?: Record<string, EntrySchedule>;
  onScheduleTypeChange?: (entryId: string, scheduleType: ScheduleType) => void;
  onTimerTimeChange?: (entryId: string, timerTimeOfDay: string) => void;
  onTimerModeChange?: (entryId: string, mode: TimerMode) => void;
  onTimerRelativeOffsetChange?: (entryId: string, offset: string) => void;
  disabled?: boolean;
};

const SCHEDULE_LABELS: Record<ScheduleType, string> = {
  OncePerRun: 'Once Per Run',
  EveryStep: 'Every Step',
  Timer: 'Timer',
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

export const QueueEntryList: React.FC<QueueEntryListProps> = ({
  entries,
  sequences,
  onAdd,
  onRemove,
  entrySchedule = {},
  onScheduleTypeChange,
  onTimerTimeChange,
  onTimerModeChange,
  onTimerRelativeOffsetChange,
  disabled,
}) => {
  const [selected, setSelected] = useState<string | undefined>(undefined);

  const options = useMemo<SearchableOption[]>(
    () => sequences.map((s) => ({ value: s.id, label: s.name || s.id })),
    [sequences]
  );

  return (
    <section className="queue-entries" aria-label="Queue sequences">
      <h4>Sequences</h4>
      {entries.length === 0 && <div className="form-hint">No sequences in this queue yet.</div>}
      <ol className="queue-entry-list">
        {entries.map((entry) => {
          const schedule = entrySchedule[entry.entryId] ?? { scheduleType: 'OncePerRun' as ScheduleType, timerTimeOfDay: '' };
          const isTimer = schedule.scheduleType === 'Timer';
          const isEveryStep = schedule.scheduleType === 'EveryStep';
          const timerMode: TimerMode = schedule.timerMode ?? (schedule.timerRelativeOffset ? 'relative' : 'timeOfDay');
          const isRelative = isTimer && timerMode === 'relative';
          const label = entry.sequenceName ?? entry.sequenceId;
          const offsetParts = parseOffset(schedule.timerRelativeOffset);

          const emitOffset = (h: number, m: number, s: number) => {
            // Client-side non-negative validation: never emit a negative component (FR-021).
            if (h < 0 || m < 0 || s < 0) return;
            onTimerRelativeOffsetChange?.(entry.entryId, composeOffset(h, m, s));
          };

          return (
            <li key={entry.entryId} className="queue-entry-row" data-testid="queue-entry">
              <span className="queue-entry-name">
                {label}
                {entry.stale && <span className="badge badge-warning" role="status"> (stale)</span>}
                {isEveryStep && <span className="badge badge-info" role="status" aria-label="Every Step"> Every Step</span>}
                {isTimer && !isRelative && <span className="badge badge-info" role="status" aria-label="Timer"> Timer</span>}
                {isRelative && <span className="badge badge-info" role="status" aria-label="Relative timer"> Timer · relative</span>}
              </span>

              {onScheduleTypeChange && (
                <select
                  aria-label={`Schedule type for ${label}`}
                  value={schedule.scheduleType}
                  disabled={disabled}
                  onChange={(e) => onScheduleTypeChange(entry.entryId, e.target.value as ScheduleType)}
                >
                  {(Object.keys(SCHEDULE_LABELS) as ScheduleType[]).map((t) => (
                    <option key={t} value={t}>{SCHEDULE_LABELS[t]}</option>
                  ))}
                </select>
              )}

              {isTimer && onTimerModeChange && (
                <select
                  aria-label={`Timer mode for ${label}`}
                  value={timerMode}
                  disabled={disabled}
                  onChange={(e) => onTimerModeChange(entry.entryId, e.target.value as TimerMode)}
                >
                  <option value="timeOfDay">Time of day</option>
                  <option value="relative">Relative</option>
                </select>
              )}

              {onTimerTimeChange && isTimer && !isRelative && (
                <input
                  type="time"
                  aria-label={`Timer time for ${label}`}
                  value={schedule.timerTimeOfDay}
                  disabled={disabled}
                  onChange={(e) => onTimerTimeChange(entry.entryId, e.target.value)}
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
                onClick={() => onRemove(entry.entryId)}
                disabled={disabled}
                aria-label={`Remove ${label}`}
              >
                Remove
              </button>
            </li>
          );
        })}
      </ol>
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
