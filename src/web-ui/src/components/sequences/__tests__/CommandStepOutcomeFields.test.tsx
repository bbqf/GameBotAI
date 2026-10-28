import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { ConditionFields } from '../ConditionFields';
import { BreakStepRow } from '../BreakStepRow';
import type { SequenceStepCondition } from '../../../types/sequenceFlow';

jest.mock('../../images/ImageSelectorDropdown', () => ({
  ImageSelectorDropdown: ({ value }: { value: string }) => <input data-testid="image" value={value} readOnly />,
}));

const saved: SequenceStepCondition = {
  type: 'commandStepOutcome',
  stepRef: 'send',
  commandStepOrder: 12,
  expectedState: 'skipped_detection_failed',
};

describe('ConditionFields - commandStepOutcome (feature 167)', () => {
  it('shows the saved values', () => {
    render(<ConditionFields condition={saved} />);
    expect((screen.getByTestId('loop-condition-stepRef') as HTMLInputElement).value).toBe('send');
    expect((screen.getByTestId('loop-condition-commandStepOrder') as HTMLInputElement).value).toBe('12');
    expect((screen.getByTestId('loop-condition-expectedState') as HTMLSelectElement).value).toBe('skipped_detection_failed');
  });

  it('lists the four states and an empty choice', () => {
    render(<ConditionFields condition={saved} />);
    const options = Array.from((screen.getByTestId('loop-condition-expectedState') as HTMLSelectElement).options).map((o) => o.value);
    expect(options).toEqual(['', 'executed', 'skipped_detection_failed', 'completed_timeout', 'image_detected']);
  });

  it('creates an empty condition when the type changes', () => {
    const onChange = jest.fn();
    render(<ConditionFields condition={{ type: 'imageVisible', imageId: 'a' }} onConditionChange={onChange} />);
    fireEvent.change(screen.getByTestId('loop-condition-type'), { target: { value: 'commandStepOutcome' } });
    expect(onChange).toHaveBeenCalledWith({ type: 'commandStepOutcome', stepRef: '', commandStepOrder: 0, expectedState: '' });
  });

  it('sends an order of 0 when the order field is empty', () => {
    const onChange = jest.fn();
    render(<ConditionFields condition={saved} onConditionChange={onChange} />);
    fireEvent.change(screen.getByTestId('loop-condition-commandStepOrder'), { target: { value: '' } });
    expect(onChange).toHaveBeenCalledWith({ ...saved, commandStepOrder: 0 });
  });

  it('sends the chosen state', () => {
    const onChange = jest.fn();
    render(<ConditionFields condition={saved} onConditionChange={onChange} />);
    fireEvent.change(screen.getByTestId('loop-condition-expectedState'), { target: { value: 'image_detected' } });
    expect(onChange).toHaveBeenCalledWith({ ...saved, expectedState: 'image_detected' });
  });
});

describe('BreakStepRow - commandStepOutcome (feature 167)', () => {
  it('shows the condition fields of a break step', () => {
    render(<BreakStepRow breakCondition={saved} onChange={jest.fn()} onRemove={jest.fn()} />);
    expect((screen.getByTestId('break-condition-type') as HTMLSelectElement).value).toBe('commandStepOutcome');
    expect((screen.getByTestId('break-step-ref') as HTMLInputElement).value).toBe('send');
    expect((screen.getByTestId('break-command-step-order') as HTMLInputElement).value).toBe('12');
    expect((screen.getByTestId('break-expected-state') as HTMLSelectElement).value).toBe('skipped_detection_failed');
  });

  it('creates an empty condition when the type changes', () => {
    const onChange = jest.fn();
    render(<BreakStepRow breakCondition={{ type: 'imageVisible', imageId: 'a' }} onChange={onChange} onRemove={jest.fn()} />);
    fireEvent.change(screen.getByTestId('break-condition-type'), { target: { value: 'commandStepOutcome' } });
    expect(onChange).toHaveBeenCalledWith({ type: 'commandStepOutcome', stepRef: '', commandStepOrder: 0, expectedState: '' });
  });
});
