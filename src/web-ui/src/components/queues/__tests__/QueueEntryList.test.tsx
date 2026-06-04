import React from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { QueueEntryList, EntrySchedule } from '../QueueEntryList';
import { QueueEntryDto } from '../../../services/queues';
import { SequenceDto } from '../../../services/sequences';
import { ScheduleType } from '../../../services/queueTemplates';

const sequences = [
  { id: 'seq-a', name: 'Alpha', steps: [] },
  { id: 'seq-b', name: 'Bravo', steps: [] },
] as unknown as SequenceDto[];

describe('QueueEntryList', () => {
  it('renders entries in order with a stale badge for unresolved references', () => {
    const entries: QueueEntryDto[] = [
      { entryId: 'e1', sequenceId: 'seq-a', sequenceName: 'Alpha', stale: false },
      { entryId: 'e2', sequenceId: 'seq-gone', sequenceName: null, stale: true },
    ];
    render(<QueueEntryList entries={entries} sequences={sequences} onAdd={jest.fn()} onRemove={jest.fn()} />);

    const rows = screen.getAllByTestId('queue-entry');
    expect(rows).toHaveLength(2);
    expect(rows[0]).toHaveTextContent('Alpha');
    expect(rows[1]).toHaveTextContent('(stale)');
  });

  it('adds the selected sequence and clears selection', () => {
    const onAdd = jest.fn();
    render(<QueueEntryList entries={[]} sequences={sequences} onAdd={onAdd} onRemove={jest.fn()} />);

    fireEvent.change(screen.getByLabelText('Add sequence'), { target: { value: 'seq-b' } });
    fireEvent.click(screen.getByText('Add'));
    expect(onAdd).toHaveBeenCalledWith('seq-b');
  });

  it('removes an entry', () => {
    const onRemove = jest.fn();
    const entries: QueueEntryDto[] = [{ entryId: 'e1', sequenceId: 'seq-a', sequenceName: 'Alpha', stale: false }];
    render(<QueueEntryList entries={entries} sequences={sequences} onAdd={jest.fn()} onRemove={onRemove} />);

    fireEvent.click(screen.getByLabelText('Remove Alpha'));
    expect(onRemove).toHaveBeenCalledWith('e1');
  });

  it('shows an empty hint when there are no entries', () => {
    render(<QueueEntryList entries={[]} sequences={sequences} onAdd={jest.fn()} onRemove={jest.fn()} />);
    expect(screen.getByText('No sequences in this queue yet.')).toBeInTheDocument();
  });

  // Schedule type tests (FR-022, FR-023, FR-024)

  it('renders schedule type dropdown when onScheduleTypeChange is provided', () => {
    const entries: QueueEntryDto[] = [
      { entryId: 'e1', sequenceId: 'seq-a', sequenceName: 'Alpha', stale: false },
    ];
    const entrySchedule: Record<string, EntrySchedule> = {
      e1: { scheduleType: 'OncePerRun', timerTimeOfDay: '' },
    };
    render(
      <QueueEntryList
        entries={entries}
        sequences={sequences}
        onAdd={jest.fn()}
        onRemove={jest.fn()}
        entrySchedule={entrySchedule}
        onScheduleTypeChange={jest.fn()}
        onTimerTimeChange={jest.fn()}
      />
    );

    expect(screen.getByLabelText('Schedule type for Alpha')).toBeInTheDocument();
  });

  it('shows timer time input when schedule type is Timer', () => {
    const entries: QueueEntryDto[] = [
      { entryId: 'e1', sequenceId: 'seq-a', sequenceName: 'Alpha', stale: false },
    ];
    const entrySchedule: Record<string, EntrySchedule> = {
      e1: { scheduleType: 'Timer', timerTimeOfDay: '15:30' },
    };
    render(
      <QueueEntryList
        entries={entries}
        sequences={sequences}
        onAdd={jest.fn()}
        onRemove={jest.fn()}
        entrySchedule={entrySchedule}
        onScheduleTypeChange={jest.fn()}
        onTimerTimeChange={jest.fn()}
      />
    );

    expect(screen.getByLabelText('Timer time for Alpha')).toBeInTheDocument();
  });

  it('does not show timer time input when schedule type is EveryStep', () => {
    const entries: QueueEntryDto[] = [
      { entryId: 'e1', sequenceId: 'seq-a', sequenceName: 'Alpha', stale: false },
    ];
    const entrySchedule: Record<string, EntrySchedule> = {
      e1: { scheduleType: 'EveryStep', timerTimeOfDay: '' },
    };
    render(
      <QueueEntryList
        entries={entries}
        sequences={sequences}
        onAdd={jest.fn()}
        onRemove={jest.fn()}
        entrySchedule={entrySchedule}
        onScheduleTypeChange={jest.fn()}
        onTimerTimeChange={jest.fn()}
      />
    );

    expect(screen.queryByLabelText('Timer time for Alpha')).not.toBeInTheDocument();
  });

  it('shows EveryStep badge for EveryStep entries', () => {
    const entries: QueueEntryDto[] = [
      { entryId: 'e1', sequenceId: 'seq-a', sequenceName: 'Alpha', stale: false },
    ];
    const entrySchedule: Record<string, EntrySchedule> = {
      e1: { scheduleType: 'EveryStep', timerTimeOfDay: '' },
    };
    render(
      <QueueEntryList
        entries={entries}
        sequences={sequences}
        onAdd={jest.fn()}
        onRemove={jest.fn()}
        entrySchedule={entrySchedule}
      />
    );

    expect(screen.getByLabelText('Every Step')).toBeInTheDocument();
  });

  it('shows Timer badge for Timer entries', () => {
    const entries: QueueEntryDto[] = [
      { entryId: 'e1', sequenceId: 'seq-a', sequenceName: 'Alpha', stale: false },
    ];
    const entrySchedule: Record<string, EntrySchedule> = {
      e1: { scheduleType: 'Timer', timerTimeOfDay: '15:30' },
    };
    render(
      <QueueEntryList
        entries={entries}
        sequences={sequences}
        onAdd={jest.fn()}
        onRemove={jest.fn()}
        entrySchedule={entrySchedule}
      />
    );

    expect(screen.getByLabelText('Timer')).toBeInTheDocument();
  });

  it('does not show any schedule badge for OncePerRun entries', () => {
    const entries: QueueEntryDto[] = [
      { entryId: 'e1', sequenceId: 'seq-a', sequenceName: 'Alpha', stale: false },
    ];
    const entrySchedule: Record<string, EntrySchedule> = {
      e1: { scheduleType: 'OncePerRun', timerTimeOfDay: '' },
    };
    render(
      <QueueEntryList
        entries={entries}
        sequences={sequences}
        onAdd={jest.fn()}
        onRemove={jest.fn()}
        entrySchedule={entrySchedule}
      />
    );

    expect(screen.queryByLabelText('Every Step')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Timer')).not.toBeInTheDocument();
  });

  it('calls onScheduleTypeChange when dropdown changes', () => {
    const onScheduleTypeChange = jest.fn();
    const entries: QueueEntryDto[] = [
      { entryId: 'e1', sequenceId: 'seq-a', sequenceName: 'Alpha', stale: false },
    ];
    const entrySchedule: Record<string, EntrySchedule> = {
      e1: { scheduleType: 'OncePerRun', timerTimeOfDay: '' },
    };
    render(
      <QueueEntryList
        entries={entries}
        sequences={sequences}
        onAdd={jest.fn()}
        onRemove={jest.fn()}
        entrySchedule={entrySchedule}
        onScheduleTypeChange={onScheduleTypeChange}
        onTimerTimeChange={jest.fn()}
      />
    );

    fireEvent.change(screen.getByLabelText('Schedule type for Alpha'), { target: { value: 'EveryStep' } });
    expect(onScheduleTypeChange).toHaveBeenCalledWith('e1', 'EveryStep' as ScheduleType);
  });
});
