import { validatePerStepConditions } from '../validation';
import type { SequenceLinearStep } from '../../types/sequenceFlow';

// Feature 167: the editor shows the same errors as the service for a commandStepOutcome condition.

const commandStep = (stepId: string, condition?: SequenceLinearStep['condition']): SequenceLinearStep => ({
  stepId,
  stepType: 'Action',
  primitiveAction: { type: 'command', schemaVersion: 'v1', payload: { commandId: 'cmd-1' } },
  ...(condition ? { condition } : {})
}) as SequenceLinearStep;

const tapStep = (stepId: string): SequenceLinearStep => ({
  stepId,
  stepType: 'Action',
  primitiveAction: { type: 'tap', schemaVersion: 'v1', payload: { x: 1, y: 1 } }
}) as SequenceLinearStep;

const stepOutcome = (
  stepRef: string,
  commandStepOrder: number,
  expectedState: string
) => ({ type: 'commandStepOutcome', stepRef, commandStepOrder, expectedState }) as SequenceLinearStep['condition'];

describe('validatePerStepConditions - commandStepOutcome', () => {
  it('accepts a valid condition', () => {
    const errors = validatePerStepConditions([
      commandStep('send'),
      commandStep('check', stepOutcome('send', 12, 'executed'))
    ]);
    expect(errors).toEqual([]);
  });

  it('accepts each of the four states in any case', () => {
    for (const state of ['executed', 'skipped_detection_failed', 'completed_timeout', 'image_detected', 'EXECUTED']) {
      const errors = validatePerStepConditions([
        commandStep('send'),
        commandStep('check', stepOutcome('send', 1, state))
      ]);
      expect(errors).toEqual([]);
    }
  });

  it('rejects an empty stepRef', () => {
    const errors = validatePerStepConditions([commandStep('send'), commandStep('check', stepOutcome('', 1, 'executed'))]);
    expect(errors).toContain("Step 'check' commandStepOutcome condition requires stepRef.");
  });

  it('rejects a stepRef that names no step', () => {
    const errors = validatePerStepConditions([commandStep('send'), commandStep('check', stepOutcome('none', 1, 'executed'))]);
    expect(errors).toContain("Step 'check' commandStepOutcome references unknown prior step 'none'.");
  });

  it('rejects a stepRef that is not before the step', () => {
    const errors = validatePerStepConditions([commandStep('check', stepOutcome('send', 1, 'executed')), commandStep('send')]);
    expect(errors).toContain("Step 'check' commandStepOutcome stepRef 'send' must reference a prior step.");
  });

  it('rejects a stepRef that is not a command step', () => {
    const errors = validatePerStepConditions([tapStep('tap1'), commandStep('check', stepOutcome('tap1', 1, 'executed'))]);
    expect(errors).toContain("Step 'check' commandStepOutcome stepRef 'tap1' must reference a step that runs a command.");
  });

  it.each([0, -3, 1.5, Number.NaN])('rejects the order %p', (order) => {
    const errors = validatePerStepConditions([commandStep('send'), commandStep('check', stepOutcome('send', order, 'executed'))]);
    expect(errors).toContain("Step 'check' commandStepOutcome commandStepOrder must be 1 or more.");
  });

  it.each(['', 'success', 'detection_failed_after_3_retries'])('rejects the state %p', (state) => {
    const errors = validatePerStepConditions([commandStep('send'), commandStep('check', stepOutcome('send', 1, state))]);
    expect(errors).toContain(
      "Step 'check' commandStepOutcome expectedState must be one of executed|skipped_detection_failed|completed_timeout|image_detected."
    );
  });

  it('checks the condition of a break step', () => {
    const loop = {
      stepId: 'loop1',
      stepType: 'Loop',
      loop: { loopType: 'count', count: 3 },
      body: [
        commandStep('send'),
        { stepId: 'stop', stepType: 'Break', breakCondition: stepOutcome('send', 0, 'executed') }
      ]
    } as SequenceLinearStep;
    const errors = validatePerStepConditions([loop]);
    expect(errors).toContain("Step 'stop' commandStepOutcome commandStepOrder must be 1 or more.");
  });
});
