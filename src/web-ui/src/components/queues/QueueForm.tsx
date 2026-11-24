import React, { useMemo } from 'react';
import { SearchableDropdown, SearchableOption } from '../SearchableDropdown';
import { useAdbDevices } from '../../services/useAdbDevices';
import { RecoveryFormFields, recoveryFieldErrors } from './deviceRecoveryForm';

export type QueueFormValue = RecoveryFormFields & {
  name: string;
  emulatorSerial: string;
  cycleExecution: boolean;
  /** Opt-in idle-pause: back the game out during idle gaps over the threshold (feature 073). */
  pauseWhenIdle: boolean;
  /** Idle-detection threshold in seconds (default 30). */
  idleThresholdSeconds: number;
  /** Optional LDPlayer instance name to cold-start before the session (feature 074); '' means unset. */
  emulatorInstanceName: string;
  /** Optional LDPlayer instance index for the pre-session cold-start (feature 074); null means unset. */
  emulatorInstanceIndex: number | null;
  /** Opt-in: start the queue again after a service restart if it was running (feature 098). */
  resumeOnServiceStart: boolean;
  /** Opt-in: move a Timer booking of a sequence to a pending firing in the window (feature 189). */
  smartScheduling: boolean;
  /** The smart scheduling window in minutes, from 1 to 120 (default 15). */
  smartSchedulingWindowMinutes: number;
  // The device recovery members (feature 199) come from RecoveryFormFields.
};

/** The permitted smart scheduling window, in minutes (feature 189). */
export const SMART_WINDOW_MIN = 1;
export const SMART_WINDOW_MAX = 120;

/** True when the window is a whole number from 1 to 120 (feature 189). */
export const isValidSmartWindow = (minutes: number): boolean =>
  Number.isInteger(minutes) && minutes >= SMART_WINDOW_MIN && minutes <= SMART_WINDOW_MAX;

/** The text of a window out of range (feature 189). */
export const SMART_WINDOW_ERROR = 'Enter a value from 1 to 120.';

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
  /**
   * Edit-mode only: the queue runs (feature 173). The emulator stays read-only, as in every edit. A note near Save says
   * when the saved settings apply.
   */
  running?: boolean;
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
  running,
}) => {
  const isEdit = mode === 'edit';
  const { devices, loading: devicesLoading } = useAdbDevices(!isEdit);

  const recoveryErrors = recoveryFieldErrors(value, value.emulatorInstanceName);

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

      {/* Row 2b'': Smart scheduling (feature 189) */}
      <div className="field">
        <label>
          <input
            type="checkbox"
            checked={value.smartScheduling}
            onChange={(e) => onChange({ ...value, smartScheduling: e.target.checked })}
            disabled={submitting}
            aria-label="Smart scheduling"
            aria-describedby="queue-smart-help"
          />
          {' '}Smart scheduling
        </label>
        <div id="queue-smart-help" className="form-hint">
          Moves a booking of a sequence to a pending firing that is not more than the window later. The queue wakes up less often.
        </div>
      </div>
      {value.smartScheduling && (
        <div className="field">
          <label htmlFor="queue-smart-window">Window (minutes)</label>
          <input
            id="queue-smart-window"
            type="number"
            min={SMART_WINDOW_MIN}
            max={SMART_WINDOW_MAX}
            value={value.smartSchedulingWindowMinutes}
            onChange={(e) => onChange({ ...value, smartSchedulingWindowMinutes: Number(e.target.value) })}
            disabled={submitting}
            aria-label="Window (minutes)"
            aria-invalid={!isValidSmartWindow(value.smartSchedulingWindowMinutes)}
            aria-describedby={isValidSmartWindow(value.smartSchedulingWindowMinutes) ? undefined : 'queue-smart-window-error'}
          />
          {!isValidSmartWindow(value.smartSchedulingWindowMinutes) && (
            <div id="queue-smart-window-error" className="field-error" role="alert">{SMART_WINDOW_ERROR}</div>
          )}
        </div>
      )}

      {/* Row 2b': Resume after a service restart (feature 098) */}
      <div className="field">
        <label>
          <input
            type="checkbox"
            checked={value.resumeOnServiceStart}
            onChange={(e) => onChange({ ...value, resumeOnServiceStart: e.target.checked })}
            disabled={submitting}
            aria-label="Resume after service restart"
            aria-describedby="queue-resume-help"
          />
          {' '}Resume after service restart
        </label>
        <div id="queue-resume-help" className="form-hint">
          If the queue is running when the service stops, start it again when the service comes back.
        </div>
      </div>

      {/* Row 2c: Emulator cold-start (feature 074). Optional instance identifier; when set, the queue
          starts this LDPlayer instance before binding its session, so it can self-start from cold. */}
      <div className="field-row">
        <div className="field">
          <label htmlFor="queue-emulator-instance-name">Emulator instance name</label>
          <input
            id="queue-emulator-instance-name"
            value={value.emulatorInstanceName}
            onChange={(e) => onChange({ ...value, emulatorInstanceName: e.target.value })}
            placeholder="e.g. PNS (optional — cold-start this instance)"
            disabled={submitting}
            aria-label="Emulator instance name"
          />
        </div>
        <div className="field">
          <label htmlFor="queue-emulator-instance-index">Emulator instance index</label>
          <input
            id="queue-emulator-instance-index"
            type="number"
            min={0}
            value={value.emulatorInstanceIndex ?? ''}
            onChange={(e) =>
              onChange({ ...value, emulatorInstanceIndex: e.target.value === '' ? null : Number(e.target.value) })
            }
            placeholder="optional"
            disabled={submitting}
            aria-label="Emulator instance index"
          />
        </div>
      </div>

      {/* Row 2d: Device recovery (feature 199). The option follows the pattern of smart scheduling. */}
      <div className="field">
        <label>
          <input
            type="checkbox"
            checked={value.deviceRecoveryOn}
            onChange={(e) => onChange({ ...value, deviceRecoveryOn: e.target.checked })}
            disabled={submitting}
            aria-label="Device recovery"
            aria-describedby={recoveryErrors.instanceName ? 'queue-recovery-help queue-recovery-instance-error' : 'queue-recovery-help'}
            aria-invalid={recoveryErrors.instanceName ? true : undefined}
          />
          {' '}Reboot the instance when the device is not live
        </label>
        <div id="queue-recovery-help" className="form-hint">
          When the device stays not live, the service reboots the emulator instance. It needs the emulator instance name.
        </div>
        {recoveryErrors.instanceName && (
          <div id="queue-recovery-instance-error" className="field-error" role="alert">{recoveryErrors.instanceName}</div>
        )}
      </div>
      {value.deviceRecoveryOn && (
        <div className="field-row">
          <div className="field">
            <label htmlFor="queue-recovery-after">Wait before the first reboot (minutes)</label>
            <input
              id="queue-recovery-after"
              type="number"
              min={1}
              step={0.1}
              value={value.recoveryAfterMinutes}
              onChange={(e) => onChange({ ...value, recoveryAfterMinutes: Number(e.target.value) })}
              disabled={submitting}
              aria-label="Recovery wait minutes"
              aria-invalid={recoveryErrors.afterMinutes ? true : undefined}
              aria-describedby={recoveryErrors.afterMinutes ? 'queue-recovery-after-error' : undefined}
            />
            {recoveryErrors.afterMinutes && (
              <div id="queue-recovery-after-error" className="field-error" role="alert">{recoveryErrors.afterMinutes}</div>
            )}
          </div>
          <div className="field">
            <label htmlFor="queue-recovery-attempts">Maximum attempts</label>
            <input
              id="queue-recovery-attempts"
              type="number"
              min={1}
              max={5}
              step={1}
              value={value.recoveryMaxAttempts}
              onChange={(e) => onChange({ ...value, recoveryMaxAttempts: Number(e.target.value) })}
              disabled={submitting}
              aria-label="Recovery maximum attempts"
              aria-invalid={recoveryErrors.maxAttempts ? true : undefined}
              aria-describedby={recoveryErrors.maxAttempts ? 'queue-recovery-attempts-error' : undefined}
            />
            {recoveryErrors.maxAttempts && (
              <div id="queue-recovery-attempts-error" className="field-error" role="alert">{recoveryErrors.maxAttempts}</div>
            )}
          </div>
          <div className="field">
            <label htmlFor="queue-recovery-cooldown">Time between attempts (minutes)</label>
            <input
              id="queue-recovery-cooldown"
              type="number"
              min={0}
              step={0.1}
              value={value.recoveryCooldownMinutes}
              onChange={(e) => onChange({ ...value, recoveryCooldownMinutes: Number(e.target.value) })}
              disabled={submitting}
              aria-label="Recovery time between attempts minutes"
              aria-invalid={recoveryErrors.cooldownMinutes ? true : undefined}
              aria-describedby={recoveryErrors.cooldownMinutes ? 'queue-recovery-cooldown-error' : undefined}
            />
            {recoveryErrors.cooldownMinutes && (
              <div id="queue-recovery-cooldown-error" className="field-error" role="alert">{recoveryErrors.cooldownMinutes}</div>
            )}
          </div>
        </div>
      )}

      {/* Row 3: Game controls (edit only) */}
      {isEdit && gameControls}

      {/* Row 4: Save / Cancel */}
      <div className="form-actions">
        <button type="button" onClick={onSubmit} disabled={submitting}>Save</button>
        <button type="button" onClick={onCancel} disabled={submitting}>Cancel</button>
      </div>
      {isEdit && running && (
        <div className="form-hint" id="queue-running-note">
          Settings apply at the next start. Smart scheduling applies to the next booking. Device recovery applies within 1 minute.
        </div>
      )}
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
