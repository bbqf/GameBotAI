import React, { useEffect, useMemo, useState } from 'react';
import type { StepThroughNodeDto, StepThroughParameterDto, StepThroughValuesRequest } from '../../services/stepThrough';
import type { ParameterDeclaration } from '../parameters/types';
import { displayLabel, effectiveKind } from '../parameters/types';
import { ParameterValueField } from '../parameters/fields/ParameterValueField';

type ValuesFormProps = {
  parameters: StepThroughParameterDto[];
  outcomes: Record<string, string>;
  nodes: StepThroughNodeDto[];
  /** True while a step runs. The author cannot change values then (FR-017). */
  disabled: boolean;
  onApply: (values: StepThroughValuesRequest) => void;
  /**
   * What the sequence declares (feature 168). A parameter with a friendly kind shows the same field as
   * the other value forms: an image picker, a list of options, or a number field.
   */
  declarations?: ParameterDeclaration[];
};

const OUTCOME_CHOICES = ['success', 'failed', 'skipped', 'not_executed', 'break', 'no_break'];

/** The values that the author gives for the parameters and for the step outcomes that conditions read (FR-017). */
export const ValuesForm: React.FC<ValuesFormProps> = ({ parameters, outcomes, nodes, disabled, onApply, declarations }) => {
  const [parameterDraft, setParameterDraft] = useState<Record<string, string>>({});
  const [outcomeDraft, setOutcomeDraft] = useState<Record<string, string>>({});

  // New values from the server (for example after a step) reset the form to the values of the server. A read
  // with the same values does not reset it, so the text that the author types stays.
  const parametersKey = JSON.stringify(parameters);
  const outcomesKey = JSON.stringify(outcomes);
  useEffect(() => {
    setParameterDraft({});
    setOutcomeDraft({});
  }, [parametersKey, outcomesKey]);

  const stepIds = useMemo(() => {
    const seen = new Set<string>();
    nodes.forEach((node) => {
      if (node.stepId) seen.add(node.stepId);
    });
    return Array.from(seen);
  }, [nodes]);

  if (parameters.length === 0 && stepIds.length === 0) return null;

  const apply = () => {
    const parameterValues: Record<string, string> = {};
    parameters.forEach((parameter) => {
      const text = parameterDraft[parameter.name] ?? (parameter.isSet ? parameter.value ?? '' : '');
      if (text !== '') parameterValues[parameter.name] = text;
    });
    const changedOutcomes: Record<string, string> = {};
    Object.entries(outcomeDraft).forEach(([stepId, value]) => {
      if ((outcomes[stepId] ?? '') !== value) changedOutcomes[stepId] = value;
    });
    onApply({ parameterValues, outcomes: changedOutcomes });
  };

  return (
    <details className="step-through-values">
      <summary>Values</summary>
      {parameters.length > 0 && (
        <fieldset disabled={disabled}>
          <legend>Parameters</legend>
          {parameters.map((parameter) => {
            const declaration = declarations?.find((d) => d.name === parameter.name);
            const friendly = declaration !== undefined && (effectiveKind(declaration) !== 'text' || !!declaration.label);
            return (
              <div className="field" key={parameter.name}>
                <label htmlFor={`step-through-param-${parameter.name}`}>
                  {declaration ? displayLabel(declaration) : parameter.name}
                </label>
                {friendly && declaration ? (
                  <ParameterValueField
                    declaration={declaration}
                    value={parameterDraft[parameter.name] ?? parameter.value ?? ''}
                    ariaLabel={`Value for ${parameter.name}`}
                    onChange={(next) => setParameterDraft((draft) => ({ ...draft, [parameter.name]: next }))}
                  />
                ) : (
                  <input
                    id={`step-through-param-${parameter.name}`}
                    value={parameterDraft[parameter.name] ?? parameter.value ?? ''}
                    onChange={(e) => setParameterDraft((draft) => ({ ...draft, [parameter.name]: e.target.value }))}
                  />
                )}
                {declaration?.helpText && <span className="form-hint">{declaration.helpText}</span>}
                {!parameter.isSet && parameter.value != null && <span className="form-hint">default value</span>}
              </div>
            );
          })}
        </fieldset>
      )}
      {stepIds.length > 0 && (
        <fieldset disabled={disabled}>
          <legend>Step outcomes that conditions read</legend>
          {stepIds.map((stepId) => (
            <div className="field" key={stepId}>
              <label htmlFor={`step-through-outcome-${stepId}`}>{stepId}</label>
              <select
                id={`step-through-outcome-${stepId}`}
                value={outcomeDraft[stepId] ?? outcomes[stepId] ?? ''}
                onChange={(e) => setOutcomeDraft((draft) => ({ ...draft, [stepId]: e.target.value }))}
              >
                <option value="">not set</option>
                {OUTCOME_CHOICES.map((choice) => (
                  <option key={choice} value={choice}>{choice}</option>
                ))}
              </select>
            </div>
          ))}
        </fieldset>
      )}
      <button type="button" onClick={apply} disabled={disabled}>Apply values</button>
    </details>
  );
};
