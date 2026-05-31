import React, { useMemo, useState } from 'react';
import { SearchableDropdown, SearchableOption } from '../SearchableDropdown';
import { QueueEntryDto } from '../../services/queues';
import { SequenceDto } from '../../services/sequences';

type QueueEntryListProps = {
  entries: QueueEntryDto[];
  sequences: SequenceDto[];
  onAdd: (sequenceId: string) => void;
  onRemove: (entryId: string) => void;
  disabled?: boolean;
};

export const QueueEntryList: React.FC<QueueEntryListProps> = ({ entries, sequences, onAdd, onRemove, disabled }) => {
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
        {entries.map((entry) => (
          <li key={entry.entryId} className="queue-entry-row" data-testid="queue-entry">
            <span className="queue-entry-name">
              {entry.sequenceName ?? entry.sequenceId}
              {entry.stale && <span className="badge badge-warning" role="status"> (stale)</span>}
            </span>
            <button type="button" onClick={() => onRemove(entry.entryId)} disabled={disabled} aria-label={`Remove ${entry.sequenceName ?? entry.sequenceId}`}>
              Remove
            </button>
          </li>
        ))}
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
