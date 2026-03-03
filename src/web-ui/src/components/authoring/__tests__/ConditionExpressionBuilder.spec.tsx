import React, { useState } from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { ConditionExpressionBuilder } from '../ConditionExpressionBuilder';
import type { ConditionExpression } from '../../../types/sequenceFlow';

const operandNode: ConditionExpression = {
  nodeType: 'operand',
  operand: {
    operandType: 'command-outcome',
    targetRef: 'cmd-1',
    expectedState: 'success'
  }
};

describe('ConditionExpressionBuilder', () => {
  it('supports switching to AND and editing nested child operands', () => {
    const Harness: React.FC = () => {
      const [value, setValue] = useState<ConditionExpression>(operandNode);
      return <ConditionExpressionBuilder value={value} onChange={setValue} />;
    };

    render(<Harness />);

    fireEvent.change(screen.getByLabelText('Node Type'), { target: { value: 'and' } });

    expect(screen.getAllByLabelText(/Operand Target \(/)).toHaveLength(2);

    fireEvent.change(screen.getByLabelText('Operand Target (0)'), { target: { value: 'cmd-2' } });
    expect(screen.getByLabelText('Operand Target (0)')).toHaveValue('cmd-2');
  });

  it('normalizes NOT to a single child expression', () => {
    const onChange = jest.fn();
    const andExpression: ConditionExpression = {
      nodeType: 'and',
      children: [operandNode, operandNode]
    };

    render(<ConditionExpressionBuilder value={andExpression} onChange={onChange} />);

    fireEvent.change(screen.getByLabelText('Node Type'), { target: { value: 'not' } });

    const updatedValue = onChange.mock.calls[onChange.mock.calls.length - 1][0] as ConditionExpression;
    expect(updatedValue.nodeType).toBe('not');
    expect(updatedValue.children).toHaveLength(1);
  });

  it('exposes image detection controls and updates threshold', () => {
    const Harness: React.FC = () => {
      const [value, setValue] = useState<ConditionExpression>(operandNode);
      return <ConditionExpressionBuilder value={value} onChange={setValue} />;
    };

    render(<Harness />);

    fireEvent.change(screen.getByLabelText('Operand Type'), { target: { value: 'image-detection' } });
    fireEvent.change(screen.getByLabelText('Expected State'), { target: { value: 'present' } });

    const thresholdInput = screen.getByLabelText('Threshold');
    fireEvent.change(thresholdInput, { target: { value: '0.92' } });

    expect(screen.getByLabelText('Operand Type')).toHaveValue('image-detection');
    expect(screen.getByLabelText('Expected State')).toHaveValue('present');
    expect(screen.getByLabelText('Threshold')).toHaveValue(0.92);
  });
});
