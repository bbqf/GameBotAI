import { validatePerStepConditions } from '../validation';
import type { SequenceLinearStep } from '../../types/sequenceFlow';

// Feature 181: the editor shows the same errors as the service for a matchedImage condition.

const waitStep = (stepId: string, condition?: SequenceLinearStep['condition']): SequenceLinearStep => ({
  stepId,
  stepType: 'Action',
  primitiveAction: { type: 'WaitForImage', schemaVersion: 'v1', payload: {} },
  ...(condition ? { condition } : {})
}) as SequenceLinearStep;

const matched = (stepRef: string, imageId: string) =>
  ({ type: 'matchedImage', stepRef, imageId }) as SequenceLinearStep['condition'];

describe('validatePerStepConditions - matchedImage', () => {
  it('accepts a valid condition', () => {
    expect(validatePerStepConditions([waitStep('wait'), waitStep('next', matched('wait', 'quest-a'))])).toEqual([]);
  });

  it('rejects an empty stepRef', () => {
    const errors = validatePerStepConditions([waitStep('wait'), waitStep('next', matched('', 'quest-a'))]);
    expect(errors).toContain("Step 'next' condition at $: matchedImage condition requires stepRef.");
  });

  it('rejects an unknown step', () => {
    const errors = validatePerStepConditions([waitStep('wait'), waitStep('next', matched('none', 'quest-a'))]);
    expect(errors).toContain("Step 'next' condition at $: matchedImage references unknown prior step 'none'.");
  });

  it('rejects a step that is not prior', () => {
    const errors = validatePerStepConditions([waitStep('next', matched('wait', 'quest-a')), waitStep('wait')]);
    expect(errors).toContain("Step 'next' condition at $: matchedImage stepRef 'wait' must reference a prior step.");
  });

  it('rejects an empty imageId', () => {
    const errors = validatePerStepConditions([waitStep('wait'), waitStep('next', matched('wait', ' '))]);
    expect(errors).toContain("Step 'next' condition at $: matchedImage condition requires imageId.");
  });
});
