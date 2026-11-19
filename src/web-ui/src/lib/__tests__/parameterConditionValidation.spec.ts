import { validatePerStepConditions } from '../validation';
import type { ConditionParameterDeclaration } from '../validation';
import type { SequenceLinearStep } from '../../types/sequenceFlow';

// Feature 193: the editor shows the same errors as the service for a parameter condition.

const homeStep = (stepId: string, condition?: SequenceLinearStep['condition']): SequenceLinearStep => ({
  stepId,
  stepType: 'Action',
  primitiveAction: { type: 'go-to-home-screen', schemaVersion: 'v1', payload: {} },
  ...(condition ? { condition } : {})
}) as SequenceLinearStep;

const parameter = (name: string, operator: string, value: unknown) =>
  ({ type: 'parameter', name, operator, value }) as unknown as SequenceLinearStep['condition'];

const declarations: ConditionParameterDeclaration[] = [
  { name: 'fighterBatch', type: 'number' },
  { name: 'fighterTier', type: 'text' },
  { name: 'tiers', type: 'text', multiple: true }
];

describe('validatePerStepConditions - parameter', () => {
  it('accepts a valid condition', () => {
    expect(validatePerStepConditions([homeStep('train', parameter('fighterBatch', 'equals', 0))], declarations)).toEqual([]);
    expect(validatePerStepConditions([homeStep('train', parameter('fighterBatch', 'equals', '0'))], declarations)).toEqual([]);
    expect(validatePerStepConditions([homeStep('tier', parameter('fighterTier', 'notEquals', 'max'))], declarations)).toEqual([]);
  });

  it('gives the shape messages of the service', () => {
    expect(validatePerStepConditions([homeStep('train', parameter('', 'equals', 0))], declarations))
      .toContain("Step 'train' condition at $: parameter condition requires name.");
    expect(validatePerStepConditions([homeStep('train', parameter('fighterBatch', 'contains', 0))], declarations))
      .toContain("Step 'train' condition at $: parameter operator must be one of equals, notEquals, greaterThan, lessThan.");
    expect(validatePerStepConditions([homeStep('train', parameter('fighterBatch', 'equals', ''))], declarations))
      .toContain("Step 'train' condition at $: parameter condition requires value.");
  });

  it('gives the unknown-name message of the service with the slot path', () => {
    expect(validatePerStepConditions([homeStep('train', parameter('fighterBach', 'equals', 0))], declarations))
      .toEqual(["Step 'train' condition at condition: parameter 'fighterBach' is not a parameter of this sequence."]);

    const ifStep = {
      stepId: 'check',
      stepType: 'If',
      if: { condition: parameter('nope', 'equals', 'x') },
      body: []
    } as unknown as SequenceLinearStep;
    expect(validatePerStepConditions([ifStep], declarations))
      .toEqual(["Step 'check' condition at if.condition: parameter 'nope' is not a parameter of this sequence."]);
  });

  it('gives the number and the list messages of the service', () => {
    expect(validatePerStepConditions([homeStep('train', parameter('fighterBatch', 'equals', 'abc'))], declarations))
      .toEqual(["Step 'train' condition at condition: parameter 'fighterBatch' is a number parameter, and value 'abc' is not a whole number."]);
    expect(validatePerStepConditions([homeStep('train', parameter('tiers', 'equals', 'max'))], declarations))
      .toEqual(["Step 'train' condition at condition: parameter 'tiers' holds a list, and a parameter condition needs one value."]);
  });

  it('skips the declaration rules when no declarations are given', () => {
    expect(validatePerStepConditions([homeStep('train', parameter('fighterBach', 'equals', 0))])).toEqual([]);
  });
});
