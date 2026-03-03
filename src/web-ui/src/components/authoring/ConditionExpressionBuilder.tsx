import React from 'react';
import type { ConditionExpression, ConditionNodeType, ConditionOperand } from '../../types/sequenceFlow';

type ConditionExpressionBuilderProps = {
  value: ConditionExpression;
  onChange: (value: ConditionExpression) => void;
};

const defaultOperand = (): ConditionOperand => ({
  operandType: 'command-outcome',
  targetRef: '',
  expectedState: 'success'
});

const toNodeShape = (nodeType: ConditionNodeType, previous: ConditionExpression): ConditionExpression => {
  if (nodeType === 'operand') {
    return { nodeType: 'operand', operand: previous.operand ?? defaultOperand() };
  }

  if (nodeType === 'not') {
    return { nodeType: 'not', children: [previous.children?.[0] ?? { nodeType: 'operand', operand: defaultOperand() }] };
  }

  return {
    nodeType,
    children: previous.children && previous.children.length >= 2
      ? previous.children
      : [{ nodeType: 'operand', operand: defaultOperand() }, { nodeType: 'operand', operand: defaultOperand() }]
  };
};

const updateChild = (value: ConditionExpression, index: number, child: ConditionExpression): ConditionExpression => ({
  ...value,
  children: (value.children ?? []).map((item, itemIndex) => (itemIndex === index ? child : item))
});

export const ConditionExpressionBuilder: React.FC<ConditionExpressionBuilderProps> = ({ value, onChange }) => {
  return (
    <fieldset className="field">
      <legend>Condition Expression</legend>
      <label htmlFor="condition-node-type">Node Type</label>
      <select
        id="condition-node-type"
        aria-label="Node Type"
        value={value.nodeType}
        onChange={(event) => onChange(toNodeShape(event.target.value as ConditionNodeType, value))}
      >
        <option value="operand">Operand</option>
        <option value="and">AND</option>
        <option value="or">OR</option>
        <option value="not">NOT</option>
      </select>

      {value.nodeType === 'operand' && (
        <>
          <label htmlFor="condition-operand-target">Operand Target (0)</label>
          <input
            id="condition-operand-target"
            aria-label="Operand Target (0)"
            value={value.operand?.targetRef ?? ''}
            onChange={(event) => {
              onChange({
                nodeType: 'operand',
                operand: {
                  ...(value.operand ?? defaultOperand()),
                  targetRef: event.target.value
                }
              });
            }}
          />
        </>
      )}

      {value.nodeType !== 'operand' && (value.children ?? []).map((child, index) => (
        <div key={index} className="field">
          <label htmlFor={`condition-child-target-${index}`}>Operand Target ({index})</label>
          <input
            id={`condition-child-target-${index}`}
            aria-label={`Operand Target (${index})`}
            value={child.operand?.targetRef ?? ''}
            onChange={(event) => {
              const updatedChild: ConditionExpression = {
                nodeType: 'operand',
                operand: {
                  ...(child.operand ?? defaultOperand()),
                  targetRef: event.target.value
                }
              };
              onChange(updateChild(value, index, updatedChild));
            }}
          />
        </div>
      ))}
    </fieldset>
  );
};
