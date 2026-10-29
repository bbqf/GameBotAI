import React, { useState } from 'react';
import type { ParameterDeclaration } from './types';
import { validateParameterName } from './parameterNameRules';
import { ParameterDeclarationCard } from './ParameterDeclarationCard';
import { ParameterFormPreview } from './ParameterFormPreview';
import { useLayoutMode } from '../../hooks/useLayoutMode';

export { validateParameterName };

export type ParameterDeclarationListProps = {
  parameters: ParameterDeclaration[];
  onChange: (parameters: ParameterDeclaration[]) => void;
  disabled?: boolean;
  /** What owns these declarations, used in the empty-state copy. */
  ownerLabel?: string;
};

const emptyDeclaration = (): ParameterDeclaration => ({
  name: '',
  type: 'text',
  default: null,
  required: false,
  description: '',
});

/**
 * The Parameters section of the command and sequence editors (feature 078, FR-025; feature 168).
 *
 * Declaring a parameter is what makes it appear in the insert-parameter picker and in the binding
 * form on every call site, so this is the entry point to the whole mechanism. Each parameter is a
 * card, and a live preview shows the form that a person fills. On a phone, the editor shows a notice
 * and no card controls (FR-024).
 */
export const ParameterDeclarationList: React.FC<ParameterDeclarationListProps> = ({
  parameters,
  onChange,
  disabled,
  ownerLabel = 'this item',
}) => {
  const [touched, setTouched] = useState<Record<number, boolean>>({});
  const layoutMode = useLayoutMode();

  const update = (index: number, patch: Partial<ParameterDeclaration>) => {
    onChange(parameters.map((p, i) => (i === index ? { ...p, ...patch } : p)));
  };

  const remove = (index: number) => onChange(parameters.filter((_, i) => i !== index));

  const move = (index: number, delta: number) => {
    const target = index + delta;
    if (target < 0 || target >= parameters.length) return;
    const next = [...parameters];
    [next[index], next[target]] = [next[target], next[index]];
    onChange(next);
  };

  if (layoutMode === 'phone') {
    return (
      <section className="parameter-declarations" aria-label="Parameters">
        <h3>Parameters</h3>
        <p className="parameter-declarations-phone-notice">
          Use a larger screen to edit parameters. You can still fill their values on this screen.
        </p>
      </section>
    );
  }

  return (
    <section className="parameter-declarations" aria-label="Parameters">
      <header className="parameter-declarations-header">
        <h3>Parameters</h3>
        <button
          type="button"
          className="parameter-declarations-add"
          disabled={disabled}
          onClick={() => onChange([...parameters, emptyDeclaration()])}
        >
          Add parameter
        </button>
      </header>

      {parameters.length === 0 ? (
        <p className="parameter-declarations-empty">
          {ownerLabel} has no parameters. Add one to let a caller supply a value instead of hard-coding
          it — or reference a <code>queue.…</code> built-in directly, which needs no declaration.
        </p>
      ) : (
        <div className="parameter-declarations-body">
          <ul className="parameter-declarations-list">
            {parameters.map((parameter, index) => (
              <ParameterDeclarationCard
                key={index}
                parameter={parameter}
                index={index}
                count={parameters.length}
                otherNames={parameters.filter((_, i) => i !== index).map((p) => p.name)}
                touched={!!touched[index]}
                onTouched={() => setTouched((t) => ({ ...t, [index]: true }))}
                onChange={(patch) => update(index, patch)}
                onMove={(delta) => move(index, delta)}
                onRemove={() => remove(index)}
                disabled={disabled}
              />
            ))}
          </ul>
          <ParameterFormPreview parameters={parameters} />
        </div>
      )}
    </section>
  );
};
