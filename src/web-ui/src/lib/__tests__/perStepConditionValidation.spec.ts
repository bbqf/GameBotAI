import { validatePerStepConditions } from '../validation';
import { SequenceLinearStep } from '../../types/sequenceFlow';

/**
 * Client-side per-step condition validation (feature 103, issue #193).
 *
 * The service has accepted a `stepRef` naming a step nested inside a `Loop` body or an `If` branch,
 * and the `break`/`no_break` outcome states, since feature 081. The editor's own validator was never
 * updated, so it kept rejecting both — and it runs before the request is sent, which means an author
 * working in the UI still hit the exact ceiling issue #193 describes, with the exact original
 * message, long after the API stopped producing it.
 *
 * The rule these tests encode: the UI must not reject what the service accepts. It may still reject
 * what the service rejects, and the tests for a genuinely absent or out-of-order reference are as
 * important as the acceptance ones — widening the walk must not lose either rule.
 */

const tap = (stepId: string, condition?: SequenceLinearStep['condition']): SequenceLinearStep => ({
  stepId,
  stepType: 'Action',
  primitiveAction: { type: 'tap', schemaVersion: 'v1', payload: { x: 10, y: 10 } },
  ...(condition ? { condition } : {})
}) as SequenceLinearStep;

const outcomeRef = (stepRef: string, expectedState = 'success') =>
  ({ type: 'commandOutcome', stepRef, expectedState }) as SequenceLinearStep['condition'];

const imageRef = (imageId: string) =>
  ({ type: 'imageVisible', imageId }) as SequenceLinearStep['condition'];

/** A loop whose body ends in a Break, giving later steps a nested step to reference. */
const loopWithNestedBreak = (): SequenceLinearStep => ({
  stepId: 'loop1',
  stepType: 'Loop',
  loop: { loopType: 'count', count: 3 },
  body: [
    tap('probe'),
    { stepId: 'nested-break', stepType: 'Break', breakCondition: imageRef('done-marker') } as SequenceLinearStep
  ]
}) as SequenceLinearStep;

describe('validatePerStepConditions — reference resolution', () => {
  it('rejects a reference to a step that is nowhere in the sequence', () => {
    const errors = validatePerStepConditions([tap('first'), tap('gate', outcomeRef('no-such-step'))]);

    expect(errors).toContain("Step 'gate' commandOutcome references unknown prior step 'no-such-step'.");
  });

  it('rejects a reference to a later top-level step', () => {
    const errors = validatePerStepConditions([tap('gate', outcomeRef('later')), tap('later')]);

    expect(errors).toContain("Step 'gate' commandOutcome stepRef 'later' must reference a prior step.");
  });

  it('accepts a reference to a step nested inside an earlier loop body', () => {
    // The headline case. Before this feature the flat lookup reported
    // "references unknown prior step" — the UI reproducing the original ceiling on its own.
    const errors = validatePerStepConditions([
      loopWithNestedBreak(),
      tap('gate', outcomeRef('nested-break', 'break'))
    ]);

    expect(errors).toEqual([]);
  });

  it('accepts a reference to a step nested inside an earlier if branch', () => {
    const branch: SequenceLinearStep = {
      stepId: 'branch',
      stepType: 'If',
      if: { condition: imageRef('q') },
      body: [tap('then-step')],
      elseBody: [tap('else-step')]
    } as SequenceLinearStep;

    expect(validatePerStepConditions([branch, tap('gate', outcomeRef('then-step'))])).toEqual([]);
    expect(validatePerStepConditions([branch, tap('gate', outcomeRef('else-step'))])).toEqual([]);
  });

  it('still rejects a reference to a nested step that comes later in authored order', () => {
    // Reachable once the walk descends, but authored after the condition asking about it. Resolution
    // and ordering are separate rules and the tree-aware walk must keep both.
    const errors = validatePerStepConditions([
      tap('gate', outcomeRef('nested-break', 'break')),
      loopWithNestedBreak()
    ]);

    expect(errors).toContain("Step 'gate' commandOutcome stepRef 'nested-break' must reference a prior step.");
  });
});

describe('validatePerStepConditions — accepted outcome states', () => {
  it.each(['success', 'failed', 'skipped', 'break', 'no_break'])(
    'accepts the expectedState %s',
    (expectedState) => {
      // Deliberately a plain top-level prior reference, so this isolates the outcome-state rule.
      // Pairing it with a nested reference would make the first three cases fail for the nesting
      // reason and hide which of the two rules is actually being measured.
      const errors = validatePerStepConditions([tap('first'), tap('gate', outcomeRef('first', expectedState))]);

      expect(errors).toEqual([]);
    }
  );

  it('still rejects an expectedState outside the five the service accepts', () => {
    const errors = validatePerStepConditions([tap('first'), tap('gate', outcomeRef('first', 'maybe'))]);

    expect(errors).toContain(
      "Step 'gate' commandOutcome expectedState must be one of success|failed|skipped|break|no_break."
    );
  });
});

describe('validatePerStepConditions — conditions on nested steps', () => {
  it('reports a malformed condition on a step inside a loop body', () => {
    // Behaviour the tree-aware walk newly gains: nested steps were skipped entirely, so a nested
    // step's broken guard was silently unvalidated in the editor.
    const loop: SequenceLinearStep = {
      stepId: 'loop1',
      stepType: 'Loop',
      loop: { loopType: 'count', count: 2 },
      body: [tap('inner', imageRef(''))]
    } as SequenceLinearStep;

    const errors = validatePerStepConditions([loop]);

    expect(errors).toContain("Step 'inner' imageVisible condition requires imageId.");
  });

  it('reports a dangling reference on a step inside a loop body', () => {
    const loop: SequenceLinearStep = {
      stepId: 'loop1',
      stepType: 'Loop',
      loop: { loopType: 'count', count: 2 },
      body: [tap('inner', outcomeRef('no-such-step'))]
    } as SequenceLinearStep;

    const errors = validatePerStepConditions([loop]);

    expect(errors).toContain("Step 'inner' commandOutcome references unknown prior step 'no-such-step'.");
  });
});
