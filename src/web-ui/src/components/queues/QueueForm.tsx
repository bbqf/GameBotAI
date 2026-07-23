import React, { useMemo } from 'react';
import { SearchableDropdown, SearchableOption } from '../SearchableDropdown';
import { useAdbDevices } from '../../services/useAdbDevices';

export type QueueFormValue = {
  name: string;
  emulatorSerial: string;
  cycleExecution: boolean;
  /** Opt-in idle-pause: back the game out during idle gaps over the threshold (feature 073). */
  pauseWhenIdle: boolean;
  /** Idle-detection threshold in seconds (default 30). */
  idleThresholdSeconds: number;
};

type QueueFormProps = {
  mode: 'create' | 'edit';
  value: QueueFormValue;
  onChange: (value: QueueFormValue) => void;
  onSubmit: () => void;
  onCancel: () => void;
  submitting?: boolean;
  formError?: string;
  /** Inline save outcome shown at the Save/Cancel action row (co-located with the Save button). */
  saveResult?: { kind: 'success' | 'error'; message: string };
  fieldErrors?: { name?: string; emulatorSerial?: string };
  /** Edit-mode only: game name + unlink row, rendered after cycle execution. */
  gameControls?: React.ReactNode;
  /**
   * Edit-mode only: the full template section (template name link, sequence entries,
   * Save Template / Reload Template), rendered below the separator.
   */
  templateControls?: React.ReactNode;
};

export const QueueForm: React.FC<QueueFormProps> = ({
  mode,
  value,
  onChange,
  onSubmit,
  onCancel,
  submitting,
  formError,
  saveResult,
  fieldErrors,
  gameControls,
  templateControls,
}) => {
  const isEdit = mode === 'edit';
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
      {/* Row 1: Name | Emulator */}
      <div className="field-row">
        <div className="field">
          <label htmlFor="queue-name">Name *</label>
          <input
            id="queue-name"
            value={value.name}
            onChange={(e) => onChange({ ...value, name: e.target.value })}
            onKeyDown={(e) => {
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

      {/* Row 2: Cycle execution */}
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

      {/* Row 2b: Idle-pause (feature 073) */}
      <div className="field">
        <label>
          <input
            type="checkbox"
            checked={value.pauseWhenIdle}
            onChange={(e) => onChange({ ...value, pauseWhenIdle: e.target.checked })}
            disabled={submitting}
            aria-label="Pause game when idle"
          />
          {' '}Pause game when idle
        </label>
      </div>
      {value.pauseWhenIdle && (
        <div className="field">
          <label htmlFor="queue-idle-threshold">Idle threshold (seconds)</label>
          <input
            id="queue-idle-threshold"
            type="number"
            min={1}
            value={value.idleThresholdSeconds}
            onChange={(e) => onChange({ ...value, idleThresholdSeconds: Number(e.target.value) })}
            disabled={submitting}
            aria-label="Idle threshold seconds"
          />
        </div>
      )}

      {/* Row 3: Game controls (edit only) */}
      {isEdit && gameControls}

      {/* Row 4: Save / Cancel */}
      <div className="form-actions">
        <button type="button" onClick={onSubmit} disabled={submitting}>Save</button>
        <button type="button" onClick={onCancel} disabled={submitting}>Cancel</button>
      </div>
      {saveResult && (
        saveResult.kind === 'success'
          ? <div className="form-hint" role="status">{saveResult.message}</div>
          : <div className="form-error" role="alert">{saveResult.message}</div>
      )}
      {formError && <div className="form-error" role="alert">{formError}</div>}

      {/* Separator + template section (edit only) */}
      {isEdit && templateControls && (
        <>
          <hr className="queue-form-separator" />
          {templateControls}
        </>
      )}
    </form>
  );
};
