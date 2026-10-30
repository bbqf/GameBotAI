import React, { useState } from 'react';
import { ParameterizableField } from '../parameters/ParameterizableField';
import { ParameterSourceControl } from '../parameters/ParameterSourceControl';
import { useFieldTemplates } from '../parameters/useFieldTemplates';
import type { ParameterDeclaration, ParameterScopeEntry } from '../parameters/types';

export type EnsureEmulatorRunningPanelValue = {
  instanceName?: string;
  instanceIndex?: string;
  adbSerial: string;
  /** Placeholders for step fields (feature 169). Includes keys that this panel has no control for. */
  fieldTemplates?: Record<string, string>;
};

export type EnsureEmulatorRunningPanelProps = {
  initialValue?: EnsureEmulatorRunningPanelValue;
  onConfirm: (value: EnsureEmulatorRunningPanelValue) => void;
  onCancel: () => void;
  disabled?: boolean;
  /**
   * Parameters in scope for this command (feature 078). When supplied, the text fields offer an
   * insert-parameter picker so the operator never types brace syntax by hand. Empty/omitted keeps
   * the fields as plain inputs, which is exactly the pre-feature behaviour.
   */
  parameterScope?: ParameterScopeEntry[];
  /** Parameters that the command declares (feature 169). */
  parameters?: ParameterDeclaration[];
  /** Placeholders of the step that is in edit. */
  fieldTemplates?: Record<string, string>;
  /** Service messages by field path, for this step only. */
  fieldErrors?: Record<string, string>;
};

const INDEX_PATH = 'ensureEmulatorRunning.instanceIndex';

/** True when a value is a parameter reference rather than a literal. */
const isReference = (value: string): boolean => /\{\{[\w.]+\}\}/.test(value);

const validateIndex = (value: string): string | null => {
  if (!value.trim()) return null;
  // A parametrized index is validated at resolve time, not here — the value is unknown until a run.
  if (isReference(value)) return null;
  const n = Number(value);
  if (!Number.isInteger(n) || isNaN(n) || n < 0) return 'Instance index must be a non-negative integer.';
  return null;
};

export const EnsureEmulatorRunningPanel: React.FC<EnsureEmulatorRunningPanelProps> = ({
  initialValue,
  onConfirm,
  onCancel,
  disabled,
  parameterScope,
  parameters,
  fieldTemplates,
  fieldErrors,
}) => {
  const templates = useFieldTemplates(fieldTemplates);
  const [instanceName, setInstanceName] = useState(initialValue?.instanceName ?? '');
  const [instanceIndex, setInstanceIndex] = useState(initialValue?.instanceIndex ?? '');
  const [adbSerial, setAdbSerial] = useState(initialValue?.adbSerial ?? '');
  const [attempted, setAttempted] = useState(false);

  const serialError = adbSerial.trim() ? null : 'ADB serial is required.';
  const indexTemplate = templates.get(INDEX_PATH);
  const indexError = indexTemplate ? null : validateIndex(instanceIndex);
  const identifierError =
    instanceName.trim() || instanceIndex.trim() || indexTemplate ? null : 'Provide an instance name or index.';
  const hasErrors = Boolean(serialError || indexError || identifierError);

  const buttonLabel = initialValue !== undefined ? 'Save' : 'Add';

  const handleConfirm = () => {
    setAttempted(true);
    if (hasErrors) return;
    onConfirm({
      instanceName: instanceName.trim() || undefined,
      // The service needs a name or an index. With a placeholder and no plain value, send 0.
      instanceIndex: instanceIndex.trim() || (indexTemplate && !instanceName.trim() ? '0' : undefined),
      adbSerial: adbSerial.trim(),
      fieldTemplates: templates.result(),
    });
  };

  return (
    <div className="action-panel action-panel--ensure-emulator-running">
      <p className="action-panel__description">
        Verifies the LDPlayer instance is running and responsive; starts or restarts it if needed.
      </p>
      <div className="field">
        <label htmlFor="ensure-emulator-instance-name">Instance name</label>
        <input
          id="ensure-emulator-instance-name"
          value={instanceName}
          onChange={(e) => setInstanceName(e.target.value)}
          disabled={disabled}
          placeholder="e.g. LDPlayer-5558"
        />
      </div>
      <div className="field">
        <label htmlFor="ensure-emulator-instance-index">Instance index</label>
        <ParameterSourceControl
          path={INDEX_PATH}
          label="Instance index"
          parameters={parameters ?? []}
          template={indexTemplate}
          onChange={(t) => templates.set(INDEX_PATH, t)}
          disabled={disabled}
          error={fieldErrors?.[INDEX_PATH]}
        >
          <input
            id="ensure-emulator-instance-index"
            type="number"
            min="0"
            value={instanceIndex}
            onChange={(e) => setInstanceIndex(e.target.value)}
            disabled={disabled}
            aria-invalid={attempted && Boolean(indexError)}
            aria-describedby={attempted && indexError ? 'ensure-emulator-instance-index-error' : undefined}
          />
        </ParameterSourceControl>
        {attempted && indexError && (
          <div id="ensure-emulator-instance-index-error" className="field-error" role="alert">{indexError}</div>
        )}
      </div>
      <div className="field">
        {parameterScope && parameterScope.length > 0 ? (
          // Feature 078: this is the field the motivating scenario varies per instance, so it is the
          // one that most benefits from the picker — choosing `queue.emulatorSerial` here is what
          // collapses N duplicated commands into one.
          <ParameterizableField
            id="ensure-emulator-adb-serial"
            label="ADB serial *"
            value={adbSerial}
            onChange={setAdbSerial}
            scope={parameterScope}
            disabled={disabled}
            placeholder="e.g. emulator-5558"
            error={attempted && serialError ? serialError : undefined}
          />
        ) : (
          <>
            <label htmlFor="ensure-emulator-adb-serial">ADB serial *</label>
            <input
              id="ensure-emulator-adb-serial"
              value={adbSerial}
              onChange={(e) => setAdbSerial(e.target.value)}
              disabled={disabled}
              placeholder="e.g. emulator-5558"
              aria-invalid={attempted && Boolean(serialError)}
              aria-describedby={attempted && serialError ? 'ensure-emulator-adb-serial-error' : undefined}
            />
            {attempted && serialError && (
              <div id="ensure-emulator-adb-serial-error" className="field-error" role="alert">{serialError}</div>
            )}
          </>
        )}
      </div>
      {attempted && identifierError && (
        <div className="field-error" role="alert">{identifierError}</div>
      )}
      <div className="action-panel__controls">
        <button type="button" onClick={handleConfirm} disabled={disabled}>
          {buttonLabel}
        </button>
        <button type="button" onClick={onCancel} disabled={disabled}>
          Cancel
        </button>
      </div>
    </div>
  );
};
