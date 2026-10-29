import React from 'react';
import type { ParameterDeclaration } from './types';
import { displayLabel, toReference } from './types';
import { isWholePlaceholder } from './parameterValueRules';

export type ParameterPassThroughProps = {
  /** The parameter of the command that the step fills. */
  declaration: ParameterDeclaration;
  /** What the sequence declares. Only parameters of the same type are offered. */
  sequenceDeclarations: readonly ParameterDeclaration[];
  /** The stored binding value. A value like `{{name}}` is a pass-through. */
  value: string | null | undefined;
  /** Called with `{{name}}` for a sequence parameter, or with an empty text to go back to a typed value. */
  onChange: (value: string) => void;
  disabled?: boolean;
};

/** The sequence parameters that can fill `declaration`: the same type, and not the same name as itself. */
export const passThroughCandidates = (
  declaration: ParameterDeclaration,
  sequenceDeclarations: readonly ParameterDeclaration[],
): ParameterDeclaration[] =>
  sequenceDeclarations.filter((d) => d.type === declaration.type && d.name.trim().length > 0);

/** The sequence parameter name that a stored value passes through, or null. */
export const passThroughName = (value: string | null | undefined): string | null =>
  value && isWholePlaceholder(value) ? value.slice(2, -2).trim() : null;

/**
 * "Use a parameter of this sequence" (feature 168, FR-028). The step stores `{{name}}`. The run gives the
 * sequence value to the command parameter. No range or list check applies to a stored placeholder.
 */
export const ParameterPassThrough: React.FC<ParameterPassThroughProps> = ({
  declaration,
  sequenceDeclarations,
  value,
  onChange,
  disabled,
}) => {
  const candidates = passThroughCandidates(declaration, sequenceDeclarations);
  if (candidates.length === 0) return null;
  const selected = passThroughName(value) ?? '';

  return (
    <label className="parameter-pass-through">
      <span>Use a parameter of this sequence</span>
      <select
        value={selected}
        disabled={disabled}
        aria-label={`Use a parameter of this sequence for ${displayLabel(declaration)}`}
        onChange={(e) => onChange(e.target.value === '' ? '' : toReference(e.target.value))}
      >
        <option value="">Enter a value here</option>
        {candidates.map((candidate) => (
          <option key={candidate.name} value={candidate.name}>
            {displayLabel(candidate)}
          </option>
        ))}
      </select>
    </label>
  );
};
