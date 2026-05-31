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
      <div className="field">
        <label htmlFor="queue-name">Name *</label>
        <input
          id="queue-name"
          value={value.name}
          onChange={(e) => onChange({ ...value, name: e.target.value })}
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
        {isEdit && <div className="form-hint">The bound emulator cannot be changed after creation.</div>}
      </div>

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

      <div className="form-actions">
        <button type="submit" disabled={submitting}>Save</button>
        <button type="button" onClick={onCancel} disabled={submitting}>Cancel</button>
      </div>
      {formError && <div className="form-error" role="alert">{formError}</div>}
    </form>
  );
};
