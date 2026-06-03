import React, { useMemo } from 'react';
import { SearchableDropdown, SearchableOption } from '../SearchableDropdown';
import { useAdbDevices } from '../../services/useAdbDevices';

export type QueueFormValue = {
  name: string;
  emulatorSerial: string;
  cycleExecution: boolean;
};

type QueueFormProps = {
  mode: 'create' | 'edit';
  value: QueueFormValue;
  onChange: (value: QueueFormValue) => void;
  onSubmit: () => void;
  onCancel: () => void;
  submitting?: boolean;
  formError?: string;
  fieldErrors?: { name?: string; emulatorSerial?: string };
  /** Edit-mode only: row 2 — template controls, rendered after the emulator and before game controls. */
  templateControls?: React.ReactNode;
  /** Edit-mode only: row 3 — game controls, rendered after template controls and before cycle execution. */
  gameControls?: React.ReactNode;
  /** Edit-mode only: row 4 — queue sequence entries, rendered after cycle execution and before the actions. */
  entries?: React.ReactNode;
};

export const QueueForm: React.FC<QueueFormProps> = ({
  mode,
  value,
  onChange,
  onSubmit,
  onCancel,
  submitting,
  formError,
  fieldErrors,
  templateControls,
  gameControls,
  entries,
}) => {
  const isEdit = mode === 'edit';
  // The emulator binding is immutable after creation, so only fetch devices when creating.
  const { devices, loading: devicesLoading } = useAdbDevices(!isEdit);

  const emulatorOptions = useMemo<SearchableOption[]>(
    () => devices.map((d) => ({ value: d.serial, label: d.serial, description: d.state })),
    [devices]
  );

  return (
    <form
      className="edit-form"
      aria-label={isEdit ? 'Edit queue form' : 'Create queue form'}
      onSubmit={(e) => {
        e.preventDefault();
        onSubmit();
      }}
    >
      <div className="field-row">
        <div className="field">
          <label htmlFor="queue-name">Name *</label>
          <input
            id="queue-name"
            value={value.name}
            onChange={(e) => onChange({ ...value, name: e.target.value })}
            onKeyDown={(e) => {
              // Row 2/4 slots carry their own inputs; with no submit button the form never
              // implicitly submits, so trigger submit explicitly from the name field.
              if (e.key === 'Enter') {
                e.preventDefault();
                onSubmit();
              }
            }}
            aria-invalid={Boolean(fieldErrors?.name)}
            aria-describedby={fieldErrors?.name ? 'queue-name-error' : undefined}
            disabled={submitting}
          />
          {fieldErrors?.name && <div id="queue-name-error" className="field-error" role="alert">{fieldErrors.name}</div>}
        </div>

        <div className="field">
          <label htmlFor="queue-emulator">Emulator *</label>
          {isEdit ? (
            <input id="queue-emulator" value={value.emulatorSerial} readOnly aria-readonly="true" disabled />
          ) : (
            <SearchableDropdown
              id="queue-emulator"
              value={value.emulatorSerial || undefined}
              options={emulatorOptions}
              placeholder={devicesLoading ? 'Loading devices…' : 'Select an emulator…'}
              disabled={submitting}
              onChange={(serial) => onChange({ ...value, emulatorSerial: serial ?? '' })}
              error={fieldErrors?.emulatorSerial}
            />
          )}
        </div>
      </div>

      {isEdit && templateControls}

      {isEdit && gameControls}

      <div className="field">
        <label>
          <input
            type="checkbox"
            checked={value.cycleExecution}
            onChange={(e) => onChange({ ...value, cycleExecution: e.target.checked })}
            disabled={submitting}
            aria-label="Cycle execution"
          />
          {' '}Cycle execution
        </label>
      </div>

      {isEdit && entries}

      <div className="form-actions">
        <button type="button" onClick={onSubmit} disabled={submitting}>Save</button>
        <button type="button" onClick={onCancel} disabled={submitting}>Cancel</button>
      </div>
      {formError && <div className="form-error" role="alert">{formError}</div>}
    </form>
  );
};
