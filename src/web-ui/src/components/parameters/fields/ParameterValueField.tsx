import React, { useEffect, useState } from 'react';
import type { ParameterDeclaration } from '../types';
import { displayLabel, effectiveKind } from '../types';
import { checkParameterValue } from '../parameterValueRules';
import { ParameterPassThrough, passThroughCandidates, passThroughName } from '../ParameterPassThrough';
import { ChoiceField } from './ChoiceField';
import { ImagePickerField } from './ImagePickerField';
import { NumberField } from './NumberField';

export type ParameterValueFieldProps = {
  declaration: ParameterDeclaration;
  /** The technical value that is stored now. Null or empty means no value. */
  value: string | null | undefined;
  /** Called with the new technical value. It is not called with a new value that is invalid. */
  onChange: (value: string) => void;
  disabled?: boolean;
  /** Accessible name of the control. The default is "Value for <name>". */
  ariaLabel?: string;
  /**
   * The parameters of the sequence. Give this only at a sequence step form: it shows
   * "Use a parameter of this sequence" (feature 168, FR-028).
   */
  sequenceDeclarations?: readonly ParameterDeclaration[];
};

const TextField: React.FC<{
  declaration: ParameterDeclaration;
  value: string | null | undefined;
  onChange: (value: string) => void;
  disabled?: boolean;
  ariaLabel: string;
}> = ({ declaration, value, onChange, disabled, ariaLabel }) => {
  const [draft, setDraft] = useState(value ?? '');
  useEffect(() => setDraft(value ?? ''), [value]);
  return (
    <input
      type="text"
      className="parameter-binding-value"
      value={draft}
      disabled={disabled}
      aria-label={ariaLabel}
      data-parameter={declaration.name}
      onChange={(e) => {
        setDraft(e.target.value);
        onChange(e.target.value);
      }}
    />
  );
};

/**
 * Picks the control for a parameter by its kind (feature 168): an input box for text, a number field,
 * buttons or a list for a choice, and an image picker for an image. A value that does not fit the
 * declaration shows an inline message with a hint, and the form does not accept it as a new value (FR-012).
 */
export const ParameterValueField: React.FC<ParameterValueFieldProps> = ({
  declaration,
  value,
  onChange,
  disabled,
  ariaLabel,
  sequenceDeclarations,
}) => {
  const kind = effectiveKind(declaration);
  const name = ariaLabel ?? `Value for ${declaration.name}`;
  const passing = passThroughName(value) !== null;
  const canPassThrough = sequenceDeclarations !== undefined
    && passThroughCandidates(declaration, sequenceDeclarations).length > 0;

  const props = { declaration, value, onChange, disabled, ariaLabel: name };
  let control: React.ReactNode;
  switch (kind) {
    case 'choice':
      control = <ChoiceField {...props} />;
      break;
    case 'image':
      control = <ImagePickerField {...props} />;
      break;
    case 'number':
      control = <NumberField {...props} />;
      break;
    default:
      control = <TextField {...props} />;
  }

  return (
    <span className="parameter-value-field">
      {canPassThrough && (
        <ParameterPassThrough
          declaration={declaration}
          sequenceDeclarations={sequenceDeclarations}
          value={value}
          onChange={onChange}
          disabled={disabled}
        />
      )}
      {!passing && control}
    </span>
  );
};

/** The title of a row: the label when the author wrote one, otherwise the technical name. */
export const parameterRowTitle = (declaration: ParameterDeclaration): string => displayLabel(declaration);

/** True when a stored value does not fit its declaration. Used for the "stale value" state. */
export const isStaleValue = (declaration: ParameterDeclaration, value: string | null | undefined): boolean =>
  !!value && checkParameterValue(declaration, value) !== null;
