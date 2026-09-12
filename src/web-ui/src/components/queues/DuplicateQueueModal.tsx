import React, { useEffect, useMemo, useState } from 'react';
import { SearchableDropdown, SearchableOption } from '../SearchableDropdown';
import { useAdbDevices } from '../../services/useAdbDevices';

export type DuplicateQueueEmulator = {
  emulatorSerial: string;
  emulatorInstanceName: string | null;
  emulatorInstanceIndex: number | null;
};

type DuplicateQueueModalProps = {
  open: boolean;
  sourceName: string;
  suggestedName: string;
  /** Source queue's current emulator config, pre-filled into the form; may be resubmitted unchanged. */
  sourceEmulator: DuplicateQueueEmulator;
  error?: string;
  onConfirm: (name: string, emulator: DuplicateQueueEmulator) => void;
  onCancel: () => void;
};

/**
 * Prompts for the duplicate's name and emulator target (feature 083 + emulator-override
 * amendment). Mirrors ConfirmDeleteModal's modal structure and QueueForm's emulator fields.
 * The emulator is the one field that, unlike the name, may be resubmitted unchanged — a
 * duplicate defaults to the source's emulator but can be pointed at a different one.
 */
export const DuplicateQueueModal: React.FC<DuplicateQueueModalProps> = ({
  open,
  sourceName,
  suggestedName,
  sourceEmulator,
  error,
  onConfirm,
  onCancel,
}) => {
  const [name, setName] = useState(suggestedName);
  const [emulatorSerial, setEmulatorSerial] = useState(sourceEmulator.emulatorSerial);
  const [emulatorInstanceName, setEmulatorInstanceName] = useState(sourceEmulator.emulatorInstanceName ?? '');
  const [emulatorInstanceIndex, setEmulatorInstanceIndex] = useState<number | null>(sourceEmulator.emulatorInstanceIndex);

  const { devices, loading: devicesLoading } = useAdbDevices(open);
  const emulatorOptions = useMemo<SearchableOption[]>(
    () => devices.map((d) => ({ value: d.serial, label: d.serial, description: d.state })),
    [devices]
  );

  useEffect(() => {
    if (open) {
      setName(suggestedName);
      setEmulatorSerial(sourceEmulator.emulatorSerial);
      setEmulatorInstanceName(sourceEmulator.emulatorInstanceName ?? '');
      setEmulatorInstanceIndex(sourceEmulator.emulatorInstanceIndex);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, suggestedName]);

  if (!open) return null;

  const trimmed = name.trim();
  const nameInvalid = trimmed.length === 0 || trimmed === sourceName.trim();
  const emulatorInvalid = emulatorSerial.trim().length === 0;

  return (
    <div className="modal-backdrop" role="dialog" aria-modal="true" aria-label="Duplicate Queue">
      <div className="modal">
        <h3>Duplicate Queue</h3>
        <p>
          Create a copy of <strong>{sourceName}</strong> with the same configuration, template,
          game, and entries, under a new name. The emulator defaults to the source&apos;s but may
          be changed.
        </p>
        <label htmlFor="duplicate-queue-name">New queue name</label>
        <input
          id="duplicate-queue-name"
          type="text"
          value={name}
          onChange={(e) => setName(e.target.value)}
          autoFocus
        />
        {nameInvalid && trimmed.length > 0 && (
          <div className="form-hint" role="status">Name must differ from &quot;{sourceName}&quot;.</div>
        )}

        <label htmlFor="duplicate-queue-emulator">Emulator</label>
        <SearchableDropdown
          id="duplicate-queue-emulator"
          value={emulatorSerial || undefined}
          options={emulatorOptions}
          placeholder={devicesLoading ? 'Loading devices…' : 'Select an emulator…'}
          onChange={(serial) => setEmulatorSerial(serial ?? '')}
          error={emulatorInvalid ? 'Emulator is required' : undefined}
        />

        <label htmlFor="duplicate-queue-instance-name">Emulator instance name</label>
        <input
          id="duplicate-queue-instance-name"
          type="text"
          value={emulatorInstanceName}
          onChange={(e) => setEmulatorInstanceName(e.target.value)}
          placeholder="e.g. PNS (optional — cold-start this instance)"
        />

        <label htmlFor="duplicate-queue-instance-index">Emulator instance index</label>
        <input
          id="duplicate-queue-instance-index"
          type="number"
          min={0}
          value={emulatorInstanceIndex ?? ''}
          onChange={(e) => setEmulatorInstanceIndex(e.target.value === '' ? null : Number(e.target.value))}
          placeholder="optional"
        />

        {error && <div className="form-error" role="alert">{error}</div>}
        <div className="modal-actions">
          <button type="button" className="btn btn-secondary" onClick={onCancel}>
            Cancel
          </button>
          <button
            type="button"
            disabled={nameInvalid || emulatorInvalid}
            onClick={() =>
              onConfirm(trimmed, {
                emulatorSerial: emulatorSerial.trim(),
                emulatorInstanceName: emulatorInstanceName.trim() || null,
                emulatorInstanceIndex,
              })
            }
          >
            Duplicate
          </button>
        </div>
      </div>
    </div>
  );
};
