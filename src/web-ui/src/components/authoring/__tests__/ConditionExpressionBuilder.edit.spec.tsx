import React, { useState } from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { ConditionExpressionBuilder } from '../ConditionExpressionBuilder';
import type { ConditionExpression } from '../../../types/sequenceFlow';

describe('ConditionExpressionBuilder edit behavior', () => {
  it('edits image detection operand target, expected state, and threshold', () => {
    const initialValue: ConditionExpression = {
      nodeType: 'operand',
      operand: {
        operandType: 'image-detection',
        targetRef: 'image-a',
        expectedState: 'present',
        threshold: 0.81
      }
    };

    const Harness: React.FC = () => {
      const [value, setValue] = useState<ConditionExpression>(initialValue);
      return <ConditionExpressionBuilder value={value} onChange={setValue} />;
    };

    render(<Harness />);

    fireEvent.change(screen.getByLabelText('Operand Target (0)'), { target: { value: 'image-b' } });
    fireEvent.change(screen.getByLabelText('Expected State'), { target: { value: 'absent' } });
    fireEvent.change(screen.getByLabelText('Threshold'), { target: { value: '0.95' } });

    expect(screen.getByLabelText('Operand Target (0)')).toHaveValue('image-b');
    expect(screen.getByLabelText('Expected State')).toHaveValue('absent');
    expect(screen.getByLabelText('Threshold')).toHaveValue(0.95);
  });
});
