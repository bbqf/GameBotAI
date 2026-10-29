import React, { useMemo, useState } from 'react';
import type { ParameterBinding, ParameterDeclaration } from './types';
import { ParameterBindingForm } from './ParameterBindingForm';
import { validateParameterName } from './parameterNameRules';
import { checkParameterValue } from './parameterValueRules';

export type ParameterFormPreviewProps = {
  parameters: ParameterDeclaration[];
};

/**
 * A live preview of the form that a person fills (feature 168). It uses the same form as the real
 * call sites, in preview mode: defaults show, there is no Inherit control, and nothing is saved.
 */
export const ParameterFormPreview: React.FC<ParameterFormPreviewProps> = ({ parameters }) => {
  const [typed, setTyped] = useState<ParameterBinding[]>([]);

  // Only valid, named parameters show. The preview follows the order of the cards.
  const shown = useMemo(
    () => parameters.filter((p, i) => validateParameterName(p.name, parameters.filter((_, j) => j !== i).map((o) => o.name)) === undefined),
    [parameters],
  );

  const bindings = useMemo<ParameterBinding[]>(
    // A default that does not fit its declaration shows as empty. The card shows the message.
    () => shown.map((p) => typed.find((b) => b.name === p.name)
      ?? { name: p.name, value: p.default && checkParameterValue(p, p.default) === null ? p.default : '' }),
    [shown, typed],
  );

  if (shown.length === 0) return null;

  return (
    <aside className="parameter-form-preview" aria-label="Preview of the value form">
      <h4>Preview</h4>
      <p className="parameter-declaration-hint">This is the form that a person sees. Nothing is saved here.</p>
      <ParameterBindingForm declarations={shown} bindings={bindings} onChange={setTyped} previewMode />
    </aside>
  );
};
