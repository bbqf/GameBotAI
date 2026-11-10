import React from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { EntrySettingsPanel, EntrySettingsPanelProps } from '../EntrySettingsPanel';
import type { ParameterDeclaration } from '../../parameters/types';

const declared: ParameterDeclaration[] = [
  { name: 'count', type: 'number', default: '3', required: false } as ParameterDeclaration,
];

const baseProps = (over: Partial<EntrySettingsPanelProps> = {}): EntrySettingsPanelProps => ({
  entryId: 'e1',
  label: 'Mine',
  areaId: 'oncePerRun',
  schedule: { scheduleType: 'OncePerRun', timerTimeOfDay: '' },
  onReassign: jest.fn(),
  onTimerTimeChange: jest.fn(),
  onTimerModeChange: jest.fn(),
  onTimerRelativeOffsetChange: jest.fn(),
  sequenceParameters: declared,
  onParameterValuesChange: jest.fn(),
  ...over,
});

describe('EntrySettingsPanel (feature 185, rules D-04, D-05, D-10)', () => {
  it('shows the Schedule part before the Parameters part', () => {
    render(<EntrySettingsPanel {...baseProps()} />);

    const schedule = screen.getByLabelText('Schedule settings for Mine');
    const parameters = screen.getByLabelText('Parameter values');
    expect(schedule.compareDocumentPosition(parameters) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(screen.getByLabelText('Schedule type for Mine')).toHaveValue('OncePerRun');
  });

  it('shows no timer control for a schedule type that is not Timer', () => {
    render(<EntrySettingsPanel {...baseProps()} />);

    expect(screen.queryByLabelText('Timer mode for Mine')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Timer time for Mine')).not.toBeInTheDocument();
  });

  it('shows the mode select and the time input of a Timer entry in time-of-day mode', () => {
    render(<EntrySettingsPanel {...baseProps({
      areaId: 'scheduled',
      schedule: { scheduleType: 'Timer', timerTimeOfDay: '14:30', timerMode: 'timeOfDay' },
    })} />);

    expect(screen.getByLabelText('Timer mode for Mine')).toHaveValue('timeOfDay');
    expect(screen.getByLabelText('Timer time for Mine')).toHaveValue('14:30');
    expect(screen.queryByLabelText('Offset hours for Mine')).not.toBeInTheDocument();
  });

  it('shows the offset inputs of a Timer entry in relative mode', () => {
    const onTimerRelativeOffsetChange = jest.fn();
    render(<EntrySettingsPanel {...baseProps({
      areaId: 'scheduled',
      schedule: { scheduleType: 'Timer', timerTimeOfDay: '', timerMode: 'relative', timerRelativeOffset: '01:02:03' },
      onTimerRelativeOffsetChange,
    })} />);

    expect(screen.getByLabelText('Offset hours for Mine')).toHaveValue(1);
    expect(screen.getByLabelText('Offset minutes for Mine')).toHaveValue(2);
    expect(screen.getByLabelText('Offset seconds for Mine')).toHaveValue(3);
    expect(screen.queryByLabelText('Timer time for Mine')).not.toBeInTheDocument();

    fireEvent.change(screen.getByLabelText('Offset minutes for Mine'), { target: { value: '15' } });
    expect(onTimerRelativeOffsetChange).toHaveBeenCalledWith('e1', '01:15:03');
  });

  it('reports a schedule type change', () => {
    const onReassign = jest.fn();
    render(<EntrySettingsPanel {...baseProps({ onReassign })} />);

    fireEvent.change(screen.getByLabelText('Schedule type for Mine'), { target: { value: 'Timer' } });

    expect(onReassign).toHaveBeenCalledWith('e1', 'Timer');
  });

  it('shows no Parameters part but the Add a value button when nothing is declared and no value is set', () => {
    render(<EntrySettingsPanel {...baseProps({ sequenceParameters: [] })} />);

    expect(screen.queryByLabelText('Parameter values')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Add a value' })).toBeInTheDocument();
  });

  it('shows the ad-hoc add field after a press of Add a value', () => {
    render(<EntrySettingsPanel {...baseProps({ sequenceParameters: [] })} />);

    fireEvent.click(screen.getByRole('button', { name: 'Add a value' }));

    expect(screen.getByLabelText('Parameter values')).toBeInTheDocument();
    expect(screen.getByLabelText('New parameter value name')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Add a value' })).not.toBeInTheDocument();
  });

  it('shows the Parameters part when the entry has a value, even with no declaration', () => {
    render(<EntrySettingsPanel {...baseProps({
      sequenceParameters: [],
      schedule: { scheduleType: 'OncePerRun', timerTimeOfDay: '', parameterValues: [{ name: 'x', value: '1' }] },
    })} />);

    expect(screen.getByLabelText('Parameter values')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Add a value' })).not.toBeInTheDocument();
  });

  it('has no Parameters part and no Add a value button when no change handler is given', () => {
    render(<EntrySettingsPanel {...baseProps({ onParameterValuesChange: undefined, sequenceParameters: [] })} />);

    expect(screen.queryByLabelText('Parameter values')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Add a value' })).not.toBeInTheDocument();
  });

  it('disables the controls when the editor is disabled', () => {
    render(<EntrySettingsPanel {...baseProps({
      disabled: true,
      areaId: 'scheduled',
      schedule: { scheduleType: 'Timer', timerTimeOfDay: '14:30', timerMode: 'timeOfDay' },
    })} />);

    expect(screen.getByLabelText('Schedule type for Mine')).toBeDisabled();
    expect(screen.getByLabelText('Timer mode for Mine')).toBeDisabled();
    expect(screen.getByLabelText('Timer time for Mine')).toBeDisabled();
    expect(screen.getByLabelText('Use default count')).toBeDisabled();
  });

  it('disables the Add a value button when the editor is disabled', () => {
    render(<EntrySettingsPanel {...baseProps({ disabled: true, sequenceParameters: [] })} />);

    expect(screen.getByRole('button', { name: 'Add a value' })).toBeDisabled();
  });
});
