import React, { useMemo, useState } from 'react';
import { SearchableDropdown, SearchableOption } from '../SearchableDropdown';
import { QueueEntryDto } from '../../services/queues';
import { SequenceDto } from '../../services/sequences';
import { ScheduleType } from '../../services/queueTemplates';

export type EntrySchedule = {
  scheduleType: ScheduleType;
  timerTimeOfDay: string;
};

type QueueEntryListProps = {
  entries: QueueEntryDto[];
  sequences: SequenceDto[];
  onAdd: (sequenceId: string) => void;
  onRemove: (entryId: string) => void;
  entrySchedule?: Record<string, EntrySchedule>;
  onScheduleTypeChange?: (entryId: string, scheduleType: ScheduleType) => void;
  onTimerTimeChange?: (entryId: string, timerTimeOfDay: string) => void;
  disabled?: boolean;
};

const SCHEDULE_LABELS: Record<ScheduleType, string> = {
  OncePerRun: 'Once Per Run',
  EveryStep: 'Every Step',
  Timer: 'Timer',
};

export const QueueEntryList: React.FC<QueueEntryListProps> = ({
  entries,
  sequences,
  onAdd,
  onRemove,
  entrySchedule = {},
  onScheduleTypeChange,
  onTimerTimeChange,
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

          return (
            <li key={entry.entryId} className="queue-entry-row" data-testid="queue-entry">
              <span className="queue-entry-name">
                {entry.sequenceName ?? entry.sequenceId}
                {entry.stale && <span className="badge badge-warning" role="status"> (stale)</span>}
                {isEveryStep && <span className="badge badge-info" role="status" aria-label="Every Step"> Every Step</span>}
                {isTimer && <span className="badge badge-info" role="status" aria-label="Timer"> Timer</span>}
              </span>

              {onScheduleTypeChange && (
                <select
                  aria-label={`Schedule type for ${entry.sequenceName ?? entry.sequenceId}`}
                  value={schedule.scheduleType}
                  disabled={disabled}
                  onChange={(e) => onScheduleTypeChange(entry.entryId, e.target.value as ScheduleType)}
                >
                  {(Object.keys(SCHEDULE_LABELS) as ScheduleType[]).map((t) => (
                    <option key={t} value={t}>{SCHEDULE_LABELS[t]}</option>
                  ))}
                </select>
              )}

              {onTimerTimeChange && isTimer && (
                <input
                  type="time"
                  aria-label={`Timer time for ${entry.sequenceName ?? entry.sequenceId}`}
                  value={schedule.timerTimeOfDay}
                  disabled={disabled}
                  onChange={(e) => onTimerTimeChange(entry.entryId, e.target.value)}
                />
              )}

              <button
                type="button"
                onClick={() => onRemove(entry.entryId)}
                disabled={disabled}
                aria-label={`Remove ${entry.sequenceName ?? entry.sequenceId}`}
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
