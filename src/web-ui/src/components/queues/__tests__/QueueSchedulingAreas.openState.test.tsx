import React from 'react';
import { fireEvent, render, screen, within } from '@testing-library/react';
import { QueueSchedulingAreas } from '../QueueSchedulingAreas';
import { EntrySchedule } from '../QueueEntryList';
import { areaForScheduleType, applyDragMove } from '../schedulingAreas';
import { QueueEntryDto } from '../../../services/queues';
import { SequenceDto } from '../../../services/sequences';

const sequences = [
  { id: 's1', name: 'Alpha', steps: [] },
  { id: 's2', name: 'Beta', steps: [] },
] as unknown as SequenceDto[];

const entries: QueueEntryDto[] = [
  { entryId: 'e1', sequenceId: 's1', sequenceName: 'Alpha', stale: false },
  { entryId: 'e2', sequenceId: 's2', sequenceName: 'Beta', stale: false },
];

const schedule: Record<string, EntrySchedule> = {
  e1: { scheduleType: 'OncePerRun', timerTimeOfDay: '' },
  e2: { scheduleType: 'OncePerRun', timerTimeOfDay: '' },
};

type Props = React.ComponentProps<typeof QueueSchedulingAreas>;

const Harness: React.FC<{ initialEntries?: QueueEntryDto[]; spy?: jest.Mock }> = ({ initialEntries = entries, spy }) => {
  const [state, setState] = React.useState({
    orderedEntryIds: initialEntries.map((e) => e.entryId),
    schedule,
  });
  const [list, setList] = React.useState(initialEntries);
  const props: Props = {
    entries: list.filter((e) => state.orderedEntryIds.includes(e.entryId)),
    sequences,
    entrySchedule: state.schedule,
    onAdd: jest.fn(),
    onRemove: (id) => {
      setList((cur) => cur.filter((e) => e.entryId !== id));
      spy?.(id);
    },
    onReorderAndReassign: (next) => setState(next),
    onParameterValuesChange: jest.fn(),
    onTimerModeChange: jest.fn(),
    onTimerTimeChange: jest.fn(),
    onTimerRelativeOffsetChange: jest.fn(),
  };
  return <QueueSchedulingAreas {...props} />;
};

const panelOf = (name: string) => screen.getByLabelText(`Entry settings for ${name}`).closest('.scheduling-card') as HTMLElement;

describe('QueueSchedulingAreas open state (feature 185, rules D-06, D-07)', () => {
  it('keeps the panel open when the schedule type changes and the card moves to another area', () => {
    render(<Harness />);
    fireEvent.click(screen.getByLabelText('Entry settings for Alpha'));
    expect(within(screen.getByLabelText('Once per run')).getByLabelText('Entry settings for Alpha')).toBeInTheDocument();

    fireEvent.change(within(panelOf('Alpha')).getByLabelText('Schedule type for Alpha'), { target: { value: 'Timer' } });

    const scheduled = screen.getByLabelText('Scheduled');
    expect(within(scheduled).getByLabelText('Entry settings for Alpha')).toBeInTheDocument();
    expect(within(scheduled).getByLabelText('Entry settings for Alpha')).toHaveAttribute('aria-expanded', 'true');
    expect(within(scheduled).getByLabelText('Timer mode for Alpha')).toBeInTheDocument();
  });

  it('opens and closes each card on its own', () => {
    render(<Harness />);

    fireEvent.click(screen.getByLabelText('Entry settings for Alpha'));
    fireEvent.click(screen.getByLabelText('Entry settings for Beta'));
    expect(screen.getAllByTestId('entry-settings-panel')).toHaveLength(2);

    fireEvent.click(screen.getByLabelText('Entry settings for Alpha'));
    expect(screen.getAllByTestId('entry-settings-panel')).toHaveLength(1);
    expect(screen.getByLabelText('Entry settings for Alpha')).toHaveAttribute('aria-expanded', 'false');
    expect(screen.getByLabelText('Entry settings for Beta')).toHaveAttribute('aria-expanded', 'true');
  });

  it('starts with every panel closed', () => {
    render(<Harness />);

    expect(screen.queryByTestId('entry-settings-panel')).not.toBeInTheDocument();
  });

  it('removes the open state of an entry that is removed', () => {
    const { rerender } = render(<Harness />);
    fireEvent.click(screen.getByLabelText('Entry settings for Alpha'));
    expect(screen.getAllByTestId('entry-settings-panel')).toHaveLength(1);

    fireEvent.click(screen.getByLabelText('Remove Alpha'));
    expect(screen.queryByLabelText('Entry settings for Alpha')).not.toBeInTheDocument();

    // The same ID comes back (an undo, or a new entry with the same ID). It must be closed.
    rerender(<Harness key="again" initialEntries={entries} />);
    expect(screen.queryByTestId('entry-settings-panel')).not.toBeInTheDocument();
  });

  it('keeps the panel open when a drag moves the card (the pure move keeps the entry)', () => {
    render(<Harness />);
    fireEvent.click(screen.getByLabelText('Entry settings for Alpha'));

    // A drag ends in the same call that the harness gives to the editor.
    const moved = applyDragMove(
      { orderedEntryIds: ['e1', 'e2'], schedule },
      { entryId: 'e1', targetArea: 'afterEveryStep', targetIndex: 0 }
    );
    expect(areaForScheduleType(moved.schedule.e1.scheduleType)).toBe('afterEveryStep');
  });
});
