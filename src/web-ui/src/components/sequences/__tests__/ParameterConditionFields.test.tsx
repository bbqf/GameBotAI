import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { ConditionFields, ConditionParametersContext, toParameterConditionValue } from '../ConditionFields';
import type { ParameterStepCondition } from '../../../types/sequenceFlow';

jest.mock('../../images/ImageSelectorDropdown', () => ({
  ImageSelectorDropdown: ({ value, 'data-testid': testId }: { value: string; 'data-testid'?: string }) => (
    <input data-testid={testId} value={value} readOnly />
  ),
}));

// Feature 193: the parameter condition in the shared condition editor.

const parameters = [
  { name: 'fighterBatch', type: 'number' as const },
  { name: 'fighterTier', type: 'text' as const }
];

const batchCondition: ParameterStepCondition = { type: 'parameter', name: 'fighterBatch', operator: 'equals', value: 0 };

describe('ConditionFields - parameter', () => {
  it('lists parameter as a condition kind and makes an empty parameter condition', () => {
    const onConditionChange = jest.fn();
    render(<ConditionFields onConditionChange={onConditionChange} parameters={parameters} testIdPrefix="if-condition" />);

    const typeSelect = screen.getByTestId('if-condition-type');
    expect(Array.from((typeSelect as HTMLSelectElement).options).map((o) => o.value)).toContain('parameter');
    fireEvent.change(typeSelect, { target: { value: 'parameter' } });
    expect(onConditionChange).toHaveBeenCalledWith({ type: 'parameter', name: '', operator: 'equals', value: '' });
  });

  it('shows a select of the parameter names, a select of the operators, and a value input', () => {
    render(<ConditionFields condition={batchCondition} parameters={parameters} testIdPrefix="if-condition" />);

    const names = screen.getByTestId('if-condition-parameterName') as HTMLSelectElement;
    expect(names.value).toBe('fighterBatch');
    expect(Array.from(names.options).map((o) => o.value)).toEqual(['', 'fighterBatch', 'fighterTier']);

    const operators = screen.getByTestId('if-condition-operator') as HTMLSelectElement;
    expect(Array.from(operators.options).map((o) => o.value)).toEqual(['equals', 'notEquals', 'greaterThan', 'lessThan']);

    expect((screen.getByTestId('if-condition-value') as HTMLInputElement).value).toBe('0');
  });

  it('calls onConditionChange with the new condition', () => {
    const onConditionChange = jest.fn();
    render(<ConditionFields condition={batchCondition} parameters={parameters} onConditionChange={onConditionChange} testIdPrefix="if-condition" />);

    fireEvent.change(screen.getByTestId('if-condition-operator'), { target: { value: 'greaterThan' } });
    expect(onConditionChange).toHaveBeenLastCalledWith({ ...batchCondition, operator: 'greaterThan' });

    fireEvent.change(screen.getByTestId('if-condition-value'), { target: { value: '250' } });
    expect(onConditionChange).toHaveBeenLastCalledWith({ ...batchCondition, value: 250 });

    fireEvent.change(screen.getByTestId('if-condition-parameterName'), { target: { value: 'fighterTier' } });
    expect(onConditionChange).toHaveBeenLastCalledWith({ ...batchCondition, name: 'fighterTier', value: '0' });
  });

  it('sends a number only for a number parameter and a whole number', () => {
    expect(toParameterConditionValue('0', 'fighterBatch', parameters)).toBe(0);
    expect(toParameterConditionValue('-5', 'fighterBatch', parameters)).toBe(-5);
    expect(toParameterConditionValue('2.5', 'fighterBatch', parameters)).toBe('2.5');
    expect(toParameterConditionValue('abc', 'fighterBatch', parameters)).toBe('abc');
    expect(toParameterConditionValue('0', 'fighterTier', parameters)).toBe('0');
    expect(toParameterConditionValue('0', 'unknown', parameters)).toBe('0');
  });

  it('reads the parameters from the context when no prop is given', () => {
    render(
      <ConditionParametersContext.Provider value={parameters}>
        <ConditionFields condition={batchCondition} testIdPrefix="loop-condition" />
      </ConditionParametersContext.Provider>
    );

    const names = screen.getByTestId('loop-condition-parameterName') as HTMLSelectElement;
    expect(Array.from(names.options).map((o) => o.value)).toEqual(['', 'fighterBatch', 'fighterTier']);
  });
});
