import React from 'react';
import { fireEvent, render, screen, within } from '@testing-library/react';
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

  it('shows the After Every Step badge for EveryStep entries', () => {
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

    // FR-002/FR-012: the badge is relabeled "After Every Step" (wire value stays EveryStep).
    expect(screen.getByLabelText('After Every Step')).toBeInTheDocument();
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

    expect(screen.queryByLabelText('After Every Step')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Timer')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('At Queue Start')).not.toBeInTheDocument();
  });

  // feature 060: "After Every Step" rename (US2) + "At Queue Start" option (US3)

  it('renders the per-step schedule option labeled "After Every Step" (T009)', () => {
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

    const dropdown = screen.getByLabelText('Schedule type for Alpha');
    const everyStepOption = within(dropdown).getByRole('option', { name: 'After Every Step' }) as HTMLOptionElement;
    // The display label changed but the underlying value remains the EveryStep identifier (FR-010).
    expect(everyStepOption.value).toBe('EveryStep');
  });

  it('renders "At Queue Start" as a dropdown option and shows its badge when selected (T012)', () => {
    const entries: QueueEntryDto[] = [
      { entryId: 'e1', sequenceId: 'seq-a', sequenceName: 'Alpha', stale: false },
    ];
    const entrySchedule: Record<string, EntrySchedule> = {
      e1: { scheduleType: 'AtQueueStart', timerTimeOfDay: '' },
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

    const dropdown = screen.getByLabelText('Schedule type for Alpha');
    const option = within(dropdown).getByRole('option', { name: 'At Queue Start' }) as HTMLOptionElement;
    expect(option.value).toBe('AtQueueStart');
    // The badge renders for an at-queue-start entry.
    expect(screen.getByLabelText('At Queue Start')).toBeInTheDocument();
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

  // Relative-offset timer mode (feature 059, US4)

  const timerEntry: QueueEntryDto[] = [
    { entryId: 'e1', sequenceId: 'seq-a', sequenceName: 'Alpha', stale: false },
  ];

  it('renders the time-of-day/relative mode toggle for Timer entries', () => {
    const entrySchedule: Record<string, EntrySchedule> = {
      e1: { scheduleType: 'Timer', timerTimeOfDay: '15:30', timerMode: 'timeOfDay' },
    };
    render(
      <QueueEntryList
        entries={timerEntry}
        sequences={sequences}
        onAdd={jest.fn()}
        onRemove={jest.fn()}
        entrySchedule={entrySchedule}
        onScheduleTypeChange={jest.fn()}
        onTimerTimeChange={jest.fn()}
        onTimerModeChange={jest.fn()}
        onTimerRelativeOffsetChange={jest.fn()}
      />
    );

    expect(screen.getByLabelText('Timer mode for Alpha')).toBeInTheDocument();
  });

  it('shows offset inputs and the relative badge in relative mode', () => {
    const entrySchedule: Record<string, EntrySchedule> = {
      e1: { scheduleType: 'Timer', timerTimeOfDay: '', timerMode: 'relative', timerRelativeOffset: '00:10:00' },
    };
    render(
      <QueueEntryList
        entries={timerEntry}
        sequences={sequences}
        onAdd={jest.fn()}
        onRemove={jest.fn()}
        entrySchedule={entrySchedule}
        onScheduleTypeChange={jest.fn()}
        onTimerModeChange={jest.fn()}
        onTimerRelativeOffsetChange={jest.fn()}
      />
    );

    expect(screen.getByLabelText('Relative timer')).toBeInTheDocument();
    expect(screen.getByLabelText('Offset minutes for Alpha')).toBeInTheDocument();
    expect(screen.getByLabelText('Offset seconds for Alpha')).toBeInTheDocument();
    // Time-of-day input is hidden in relative mode.
    expect(screen.queryByLabelText('Timer time for Alpha')).not.toBeInTheDocument();
  });

  it('emits a composed offset and never a negative one', () => {
    const onTimerRelativeOffsetChange = jest.fn();
    const entrySchedule: Record<string, EntrySchedule> = {
      e1: { scheduleType: 'Timer', timerTimeOfDay: '', timerMode: 'relative', timerRelativeOffset: '00:00:00' },
    };
    render(
      <QueueEntryList
        entries={timerEntry}
        sequences={sequences}
        onAdd={jest.fn()}
        onRemove={jest.fn()}
        entrySchedule={entrySchedule}
        onScheduleTypeChange={jest.fn()}
        onTimerModeChange={jest.fn()}
        onTimerRelativeOffsetChange={onTimerRelativeOffsetChange}
      />
    );

    fireEvent.change(screen.getByLabelText('Offset minutes for Alpha'), { target: { value: '10' } });
    expect(onTimerRelativeOffsetChange).toHaveBeenCalledWith('e1', '00:10:00');

    onTimerRelativeOffsetChange.mockClear();
    fireEvent.change(screen.getByLabelText('Offset minutes for Alpha'), { target: { value: '-5' } });
    expect(onTimerRelativeOffsetChange).not.toHaveBeenCalled();
  });
});
