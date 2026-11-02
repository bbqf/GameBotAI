import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { PrimitiveActionFields } from '../PrimitiveActionFields';
import {
  WaitForStillFields,
  WAIT_FOR_STILL_DEFAULTS,
  WAIT_FOR_STILL_TYPE,
  createDefaultWaitForStillAction,
  isWaitForStillActionType,
} from '../WaitForStillFields';
import type { SequencePrimitiveActionPayload } from '../../../types/sequenceFlow';
import { validatePerStepConditions } from '../../../lib/validation';

const defaultAction = (): SequencePrimitiveActionPayload => createDefaultWaitForStillAction();

const renderFields = (action: SequencePrimitiveActionPayload, onChange = jest.fn()) => {
  render(<WaitForStillFields action={action} onChange={onChange} scope={[]} idPrefix="w" />);
  return onChange;
};

const lastPayload = (onChange: jest.Mock, call = 0) =>
  (onChange.mock.calls[call][0] as SequencePrimitiveActionPayload).payload;

describe('createDefaultWaitForStillAction', () => {
  it('has the default values and no region', () => {
    const action = createDefaultWaitForStillAction();
    expect(action.type).toBe(WAIT_FOR_STILL_TYPE);
    expect(action.payload).toEqual({ stableMs: 300, maxDiff: 0.01, timeoutMs: 5000 });
    expect(WAIT_FOR_STILL_DEFAULTS.stableMs).toBe(300);
    expect('region' in action.payload).toBe(false);
  });

  it('knows its own type without case', () => {
    expect(isWaitForStillActionType('WaitForStill')).toBe(true);
    expect(isWaitForStillActionType('waitforstill')).toBe(true);
    expect(isWaitForStillActionType('WaitForImage')).toBe(false);
  });
});

describe('WaitForStillFields', () => {
  it('shows every field with its default', () => {
    renderFields(defaultAction());
    expect(screen.getByLabelText('stableMs')).toHaveValue('300');
    expect(screen.getByLabelText('maxDiff')).toHaveValue('0.01');
    expect(screen.getByLabelText('timeoutMs')).toHaveValue('5000');
    for (const key of ['x', 'y', 'width', 'height']) {
      expect(screen.getByLabelText(`region ${key}`)).toHaveValue('');
    }
  });

  it('writes a changed number back as a number', () => {
    const onChange = renderFields(defaultAction());
    fireEvent.change(screen.getByLabelText('stableMs'), { target: { value: '450' } });
    const payload = lastPayload(onChange);
    expect(payload.stableMs).toBe(450);
    expect(payload.timeoutMs).toBe(5000);
  });

  it('writes a decimal maxDiff as a number', () => {
    const onChange = renderFields(defaultAction());
    fireEvent.change(screen.getByLabelText('maxDiff'), { target: { value: '0.05' } });
    expect(lastPayload(onChange).maxDiff).toBe(0.05);
  });

  it('accepts a parameter reference and keeps it as text', () => {
    const onChange = renderFields(defaultAction());
    fireEvent.change(screen.getByLabelText('timeoutMs'), { target: { value: '{{limit}}' } });
    expect(lastPayload(onChange).timeoutMs).toBe('{{limit}}');
  });

  it('removes a field that the author empties, so that the default applies', () => {
    const onChange = renderFields(defaultAction());
    fireEvent.change(screen.getByLabelText('stableMs'), { target: { value: '' } });
    expect('stableMs' in lastPayload(onChange)).toBe(false);
  });

  it('adds a region field by field and removes the region when all four are empty', () => {
    const onChange = renderFields(defaultAction());
    fireEvent.change(screen.getByLabelText('region width'), { target: { value: '720' } });
    expect(lastPayload(onChange).region).toEqual({ width: 720 });

    const withRegion: SequencePrimitiveActionPayload = {
      ...defaultAction(),
      payload: { ...defaultAction().payload, region: { x: 0, y: 0, width: 720, height: 900 } },
    };
    const onChange2 = jest.fn();
    render(<WaitForStillFields action={withRegion} onChange={onChange2} scope={[]} idPrefix="v" />);
    expect(document.getElementById('v-region-width')).toHaveValue('720');
    for (const key of ['x', 'y', 'width']) {
      fireEvent.change(document.getElementById(`v-region-${key}`) as HTMLInputElement, { target: { value: '' } });
    }
    fireEvent.change(document.getElementById('v-region-height') as HTMLInputElement, { target: { value: '' } });
    // Each call is built from the same unchanged props, so check the last one alone.
    const last = onChange2.mock.calls[onChange2.mock.calls.length - 1][0] as SequencePrimitiveActionPayload;
    expect((last.payload.region as Record<string, unknown>).height).toBeUndefined();
  });

  it('removes the region when its only field is emptied', () => {
    const action: SequencePrimitiveActionPayload = {
      ...defaultAction(),
      payload: { ...defaultAction().payload, region: { width: 10 } },
    };
    const onChange = renderFields(action);
    fireEvent.change(screen.getByLabelText('region width'), { target: { value: '' } });
    expect('region' in lastPayload(onChange)).toBe(false);
  });
});

describe('PrimitiveActionFields routing', () => {
  it('uses the WaitForStill form for the type', () => {
    render(<PrimitiveActionFields action={defaultAction()} onChange={jest.fn()} scope={[]} idPrefix="p" />);
    expect(screen.getByTestId('wait-for-still-fields')).toBeInTheDocument();
  });
});

describe('commandOutcome expected states for a WaitForStill step', () => {
  const steps = (expectedState: string) =>
    [
      { stepId: 'wait', stepType: 'Action', primitiveAction: createDefaultWaitForStillAction() },
      {
        stepId: 'next',
        stepType: 'Action',
        primitiveAction: { type: 'tap', payload: { x: 1, y: 1 } },
        condition: { type: 'commandOutcome', stepRef: 'wait', expectedState },
      },
    ] as never;

  it.each(['still', 'timeout_elapsed', 'success'])('accepts %s', (state) => {
    expect(validatePerStepConditions(steps(state))).toEqual([]);
  });

  it('still rejects an unknown state', () => {
    expect(validatePerStepConditions(steps('maybe')).join(' ')).toContain('expectedState must be one of');
  });
});
