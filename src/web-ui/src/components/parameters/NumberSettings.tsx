import React from 'react';
import type { ParameterDeclaration } from './types';

export type NumberSettingsProps = {
  parameter: ParameterDeclaration;
  onChange: (patch: Partial<ParameterDeclaration>) => void;
  disabled?: boolean;
};

const toNumber = (text: string): number | null => (/^-?\d+$/.test(text.trim()) ? Number.parseInt(text, 10) : null);

/** The problem of the number settings, with a hint. Returns undefined when they are fine. */
export const numberSettingsProblem = (parameter: ParameterDeclaration): string | undefined => {
  const { min, max, pattern } = parameter;
  if (min != null && max != null && min > max) return 'The minimum is more than the maximum. Change one of the two numbers.';
  if (pattern) {
    const markers = pattern.split('{value}').length - 1;
    if (markers !== 1) return 'The pattern must have exactly one {value} marker. Write the marker one time.';
  }
  return undefined;
};

/** Min, max, unit, and pattern of a number parameter, with a live technical result for the author (feature 168). */
export const NumberSettings: React.FC<NumberSettingsProps> = ({ parameter, onChange, disabled }) => {
  const sample = parameter.default && /^-?\d+$/.test(parameter.default) ? parameter.default : String(parameter.min ?? 5);
  const problem = numberSettingsProblem(parameter);

  return (
    <div className="number-settings">
      <label>
        <span>Minimum</span>
        <input
          type="text"
          inputMode="numeric"
          value={parameter.min ?? ''}
          disabled={disabled}
          aria-label="Minimum"
          onChange={(e) => onChange({ min: toNumber(e.target.value) })}
        />
      </label>
      <label>
        <span>Maximum</span>
        <input
          type="text"
          inputMode="numeric"
          value={parameter.max ?? ''}
          disabled={disabled}
          aria-label="Maximum"
          onChange={(e) => onChange({ max: toNumber(e.target.value) })}
        />
      </label>
      <label>
        <span>Unit</span>
        <input
          type="text"
          value={parameter.unit ?? ''}
          maxLength={20}
          disabled={disabled}
          placeholder="for example ms"
          aria-label="Unit"
          onChange={(e) => onChange({ unit: e.target.value === '' ? null : e.target.value })}
        />
      </label>
      <label>
        <span>Technical pattern</span>
        <input
          type="text"
          value={parameter.pattern ?? ''}
          disabled={disabled}
          placeholder="for example emulator-{value}"
          aria-label="Technical pattern"
          onChange={(e) => onChange({ pattern: e.target.value === '' ? null : e.target.value })}
        />
      </label>
      {parameter.pattern && !problem && (
        <p className="parameter-declaration-hint">
          Technical result: <code>{parameter.pattern.replace('{value}', sample)}</code>
        </p>
      )}
      {problem && (
        <p className="parameter-declaration-error" role="alert">
          {problem}
        </p>
      )}
    </div>
  );
};
