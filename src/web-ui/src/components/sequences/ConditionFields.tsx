import React from 'react';
import type {
  CommandStepOutcomeExpectedState,
  ParameterConditionOperator,
  SequenceStepCondition
} from '../../types/sequenceFlow';
import { COMMAND_STEP_OUTCOME_STATES, PARAMETER_CONDITION_OPERATORS } from '../../types/sequenceFlow';
import { SimilarityInput } from './SimilarityInput';
import { ImageSelectorDropdown } from '../images/ImageSelectorDropdown';

/** A declared parameter of the sequence, as the parameter condition needs it (feature 193). */
export type ConditionParameter = { name: string; type: 'text' | 'number' };

export type ConditionFieldsProps = {
  condition?: SequenceStepCondition;
  disabled?: boolean;
  onConditionChange?: (condition: SequenceStepCondition) => void;
  /** Prefix for data-testid attributes; defaults to the loop editor's historical ids. */
  testIdPrefix?: string;
  /** The declared parameters of the sequence, for the name of a parameter condition (feature 193). */
  parameters?: ConditionParameter[];
};

/**
 * Feature 193: the declared parameters of the sequence that the editor shows. The sequence editor sets
 * it, so each ConditionFields in an If or a Loop header gets the list with no prop at each level.
 * The `parameters` prop has priority over this context.
 */
export const ConditionParametersContext = React.createContext<ConditionParameter[] | undefined>(undefined);

const WHOLE_NUMBER = /^\s*[+-]?\d+\s*$/;

/**
 * Feature 193: the value of a parameter condition is a number when the parameter is a number parameter
 * and the text is a whole number. In all other cases, the value is the text.
 */
export const toParameterConditionValue = (
  text: string,
  parameterName: string,
  parameters: ConditionParameter[] | undefined
): string | number => {
  const declared = parameters?.find((p) => p.name === parameterName);
  if (declared?.type === 'number' && WHOLE_NUMBER.test(text)) {
    return Number.parseInt(text.trim(), 10);
  }
  return text;
};

/**
 * Shared condition editor used by while/repeat-until loop headers and if-block headers,
 * guaranteeing identical condition controls across both block types (feature 067, FR-008).
 */
export const ConditionFields: React.FC<ConditionFieldsProps> = ({
  condition, disabled, onConditionChange, testIdPrefix = 'loop-condition', parameters: parametersProp,
}) => {
  const parametersFromContext = React.useContext(ConditionParametersContext);
  const parameters = parametersProp ?? parametersFromContext;
  return (
  <div className="loop-block-header__condition-fields">
    <label className="loop-block-header__negate-toggle">
      <input
        type="checkbox"
        data-testid={`${testIdPrefix}-negate`}
        checked={condition?.negate ?? false}
        disabled={disabled}
        onChange={(e) => {
          if (condition) onConditionChange?.({ ...condition, negate: e.target.checked });
        }}
      />
      NOT
    </label>
    <select
      data-testid={`${testIdPrefix}-type`}
      value={condition?.type ?? 'imageVisible'}
      disabled={disabled}
      onChange={(e) => {
        const type = e.target.value as 'imageVisible' | 'commandOutcome' | 'commandStepOutcome' | 'matchedImage' | 'parameter';
        if (type === 'parameter') {
          onConditionChange?.(
            condition?.type === 'parameter'
              ? condition
              : { type: 'parameter', name: '', operator: 'equals', value: '' }
          );
        } else if (type === 'imageVisible') {
          onConditionChange?.({ type: 'imageVisible', imageId: condition?.type === 'imageVisible' ? condition.imageId : '', minSimilarity: null });
        } else if (type === 'matchedImage') {
          onConditionChange?.(
            condition?.type === 'matchedImage'
              ? condition
              : { type: 'matchedImage', stepRef: '', imageId: '' }
          );
        } else if (type === 'commandStepOutcome') {
          onConditionChange?.(
            condition?.type === 'commandStepOutcome'
              ? condition
              : { type: 'commandStepOutcome', stepRef: '', commandStepOrder: 0, expectedState: '' }
          );
        } else {
          onConditionChange?.({ type: 'commandOutcome', stepRef: condition?.type === 'commandOutcome' ? condition.stepRef : '', expectedState: condition?.type === 'commandOutcome' ? condition.expectedState : 'success' });
        }
      }}
    >
      <option value="imageVisible">imageVisible</option>
      <option value="commandOutcome">commandOutcome</option>
      <option value="commandStepOutcome">commandStepOutcome</option>
      <option value="matchedImage">matchedImage</option>
      <option value="parameter">parameter</option>
    </select>

    {condition?.type === 'imageVisible' && (
      <>
        <ImageSelectorDropdown
          data-testid={`${testIdPrefix}-imageId`}
          value={condition.imageId}
          onChange={(id) => onConditionChange?.({ ...condition, imageId: id })}
          disabled={disabled}
        />
        <SimilarityInput
          data-testid={`${testIdPrefix}-minSimilarity`}
          value={condition.minSimilarity}
          disabled={disabled}
          onChange={(v) => onConditionChange?.({ ...condition, minSimilarity: v })}
          style={{ width: '90px' }}
        />
      </>
    )}
    {condition?.type === 'commandOutcome' && (
      <>
        <input
          data-testid={`${testIdPrefix}-stepRef`}
          placeholder="Step ref"
          value={condition.stepRef}
          disabled={disabled}
          onChange={(e) => onConditionChange?.({ ...condition, stepRef: e.target.value })}
        />
        <select
          data-testid={`${testIdPrefix}-expectedState`}
          value={condition.expectedState}
          disabled={disabled}
          onChange={(e) => onConditionChange?.({ ...condition, expectedState: e.target.value as 'success' | 'failed' | 'skipped' })}
        >
          <option value="success">success</option>
          <option value="failed">failed</option>
          <option value="skipped">skipped</option>
        </select>
      </>
    )}
    {condition?.type === 'matchedImage' && (
      <>
        <input
          data-testid={`${testIdPrefix}-stepRef`}
          placeholder="Step ref"
          value={condition.stepRef}
          disabled={disabled}
          onChange={(e) => onConditionChange?.({ ...condition, stepRef: e.target.value })}
        />
        <ImageSelectorDropdown
          data-testid={`${testIdPrefix}-imageId`}
          value={condition.imageId}
          onChange={(id) => onConditionChange?.({ ...condition, imageId: id })}
          disabled={disabled}
        />
      </>
    )}
    {condition?.type === 'parameter' && (
      <>
        <select
          data-testid={`${testIdPrefix}-parameterName`}
          value={condition.name}
          disabled={disabled}
          onChange={(e) => {
            const name = e.target.value;
            onConditionChange?.({
              ...condition,
              name,
              value: toParameterConditionValue(String(condition.value), name, parameters)
            });
          }}
        >
          <option value="">Select parameter</option>
          {(parameters ?? []).map((p) => (
            <option key={p.name} value={p.name}>{p.name}</option>
          ))}
          {condition.name !== '' && !(parameters ?? []).some((p) => p.name === condition.name) && (
            <option value={condition.name}>{condition.name}</option>
          )}
        </select>
        <select
          data-testid={`${testIdPrefix}-operator`}
          value={condition.operator}
          disabled={disabled}
          onChange={(e) => onConditionChange?.({ ...condition, operator: e.target.value as ParameterConditionOperator })}
        >
          {PARAMETER_CONDITION_OPERATORS.map((op) => (
            <option key={op} value={op}>{op}</option>
          ))}
        </select>
        <input
          data-testid={`${testIdPrefix}-value`}
          placeholder="Value"
          value={String(condition.value)}
          disabled={disabled}
          onChange={(e) => onConditionChange?.({
            ...condition,
            value: toParameterConditionValue(e.target.value, condition.name, parameters)
          })}
        />
      </>
    )}
    {condition?.type === 'commandStepOutcome' && (
      <>
        <input
          data-testid={`${testIdPrefix}-stepRef`}
          placeholder="Step ref"
          value={condition.stepRef}
          disabled={disabled}
          onChange={(e) => onConditionChange?.({ ...condition, stepRef: e.target.value })}
        />
        <input
          type="number"
          min={1}
          step={1}
          data-testid={`${testIdPrefix}-commandStepOrder`}
          placeholder="Step order"
          value={condition.commandStepOrder > 0 ? condition.commandStepOrder : ''}
          disabled={disabled}
          onChange={(e) => {
            const parsed = Number.parseInt(e.target.value, 10);
            onConditionChange?.({ ...condition, commandStepOrder: Number.isNaN(parsed) ? 0 : parsed });
          }}
        />
        <select
          data-testid={`${testIdPrefix}-expectedState`}
          value={condition.expectedState}
          disabled={disabled}
          onChange={(e) => onConditionChange?.({ ...condition, expectedState: e.target.value as CommandStepOutcomeExpectedState | '' })}
        >
          <option value="">Select state</option>
          {COMMAND_STEP_OUTCOME_STATES.map((state) => (
            <option key={state} value={state}>{state}</option>
          ))}
        </select>
      </>
    )}
  </div>
  );
};
