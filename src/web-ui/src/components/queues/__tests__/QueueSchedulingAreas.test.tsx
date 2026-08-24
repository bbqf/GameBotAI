import React from 'react';
import { fireEvent, render, screen, within } from '@testing-library/react';
import { QueueSchedulingAreas } from '../QueueSchedulingAreas';
import { EntrySchedule } from '../QueueEntryList';
import { QueueEntryDto } from '../../../services/queues';
import { SequenceDto } from '../../../services/sequences';

const sequences = [
  { id: 'seq-a', name: 'Alpha', steps: [] },
  { id: 'seq-b', name: 'Bravo', steps: [] },
] as unknown as SequenceDto[];

const entry = (entryId: string, over: Partial<QueueEntryDto> = {}): QueueEntryDto => ({
  entryId,
  sequenceId: `seq-${entryId}`,
  sequenceName: entryId.toUpperCase(),
  stale: false,
  ...over,
});

const baseProps = () => ({
  sequences,
  onAdd: jest.fn(),
  onRemove: jest.fn(),
  onReorderAndReassign: jest.fn(),
  onTimerTimeChange: jest.fn(),
  onTimerModeChange: jest.fn(),
  onTimerRelativeOffsetChange: jest.fn(),
});

const AREA_LABELS = ['Start of execution', 'Once per run', 'Scheduled', 'After every step'];

describe('QueueSchedulingAreas', () => {
  it('C1: renders exactly four labeled areas', () => {
    render(<QueueSchedulingAreas entries={[]} entrySchedule={{}} {...baseProps()} />);
    for (const label of AREA_LABELS) {
      expect(screen.getByRole('region', { name: label })).toBeInTheDocument();
    }
  });

  it('C2: places each entry in the area matching its scheduleType (default OncePerRun)', () => {
    const entries = [entry('a'), entry('b'), entry('c'), entry('d'), entry('e')];
    const entrySchedule: Record<string, EntrySchedule> = {
      a: { scheduleType: 'AtQueueStart', timerTimeOfDay: '' },
      b: { scheduleType: 'OncePerRun', timerTimeOfDay: '' },
      c: { scheduleType: 'Timer', timerTimeOfDay: '15:30' },
      d: { scheduleType: 'EveryStep', timerTimeOfDay: '' },
      // e has no schedule → defaults to OncePerRun
    };
    render(<QueueSchedulingAreas entries={entries} entrySchedule={entrySchedule} {...baseProps()} />);

    expect(within(screen.getByRole('region', { name: 'Start of execution' })).getByText('A')).toBeInTheDocument();
    const oncePerRun = screen.getByRole('region', { name: 'Once per run' });
    expect(within(oncePerRun).getByText('B')).toBeInTheDocument();
    expect(within(oncePerRun).getByText('E')).toBeInTheDocument();
    expect(within(screen.getByRole('region', { name: 'Scheduled' })).getByText('C')).toBeInTheDocument();
    expect(within(screen.getByRole('region', { name: 'After every step' })).getByText('D')).toBeInTheDocument();
  });

  it('C3: lays out the areas (full-width top + left stack + right column)', () => {
    const { container } = render(<QueueSchedulingAreas entries={[]} entrySchedule={{}} {...baseProps()} />);
    expect(container.querySelector('.scheduling-areas__top')).toBeInTheDocument();
    expect(container.querySelector('.scheduling-areas__left')).toBeInTheDocument();
    expect(container.querySelector('.scheduling-areas__right')).toBeInTheDocument();
  });

  it('C4: an empty area still shows its label and an empty-state hint', () => {
    render(<QueueSchedulingAreas entries={[]} entrySchedule={{}} {...baseProps()} />);
    const scheduled = screen.getByRole('region', { name: 'Scheduled' });
    expect(within(scheduled).getByText(/Drop sequences here/i)).toBeInTheDocument();
  });

  it('C5: shows badges consistent with the area; OncePerRun shows none; stale preserved', () => {
    const entries = [entry('a', { stale: true }), entry('b'), entry('c'), entry('d')];
    const entrySchedule: Record<string, EntrySchedule> = {
      a: { scheduleType: 'AtQueueStart', timerTimeOfDay: '' },
      b: { scheduleType: 'OncePerRun', timerTimeOfDay: '' },
      c: { scheduleType: 'Timer', timerTimeOfDay: '15:30' },
      d: { scheduleType: 'EveryStep', timerTimeOfDay: '' },
    };
    render(<QueueSchedulingAreas entries={entries} entrySchedule={entrySchedule} {...baseProps()} />);

    expect(screen.getByLabelText('At Queue Start')).toBeInTheDocument();
    expect(screen.getByLabelText('After Every Step')).toBeInTheDocument();
    expect(screen.getByLabelText('Timer')).toBeInTheDocument();
    expect(screen.getByText('(stale)')).toBeInTheDocument();
    // OncePerRun (B) carries no schedule badge.
    const oncePerRun = screen.getByRole('region', { name: 'Once per run' });
    expect(within(oncePerRun).queryByLabelText('At Queue Start')).not.toBeInTheDocument();
    expect(within(oncePerRun).queryByLabelText('Timer')).not.toBeInTheDocument();
  });

  it('C5b: a disabled entry renders an unchecked switch, an Off badge, and the disabled style (077)', () => {
    const entries = [entry('a'), entry('b')];
    const entrySchedule: Record<string, EntrySchedule> = {
      a: { scheduleType: 'OncePerRun', timerTimeOfDay: '', enabled: false },
      b: { scheduleType: 'OncePerRun', timerTimeOfDay: '', enabled: true },
    };
    const { container } = render(
      <QueueSchedulingAreas entries={entries} entrySchedule={entrySchedule} {...baseProps()} onToggleEnabled={jest.fn()} />
    );

    // A is off: switch unchecked (labeled "Enable A"), Off badge present, disabled style applied.
    const enableA = screen.getByRole('switch', { name: 'Enable A' }) as HTMLInputElement;
    expect(enableA.checked).toBe(false);
    expect(screen.getByLabelText('Disabled')).toBeInTheDocument();
    expect(container.querySelector('.scheduling-card--disabled')).toBeInTheDocument();
    // B is on: switch checked (labeled "Disable B").
    const disableB = screen.getByRole('switch', { name: 'Disable B' }) as HTMLInputElement;
    expect(disableB.checked).toBe(true);
  });

  it('C5c: toggling the switch calls onToggleEnabled with the entry id and the new state (077)', () => {
    const entries = [entry('b')];
    const entrySchedule: Record<string, EntrySchedule> = {
      b: { scheduleType: 'OncePerRun', timerTimeOfDay: '', enabled: true },
    };
    const onToggleEnabled = jest.fn();
    render(
      <QueueSchedulingAreas entries={entries} entrySchedule={entrySchedule} {...baseProps()} onToggleEnabled={onToggleEnabled} />
    );

    fireEvent.click(screen.getByRole('switch', { name: 'Disable B' }));
    expect(onToggleEnabled).toHaveBeenCalledWith('b', false);
  });

  it('C6: only the Scheduled area exposes timer controls', () => {
    const entries = [entry('c'), entry('b')];
    const entrySchedule: Record<string, EntrySchedule> = {
      c: { scheduleType: 'Timer', timerTimeOfDay: '15:30', timerMode: 'timeOfDay' },
      b: { scheduleType: 'OncePerRun', timerTimeOfDay: '' },
    };
    render(<QueueSchedulingAreas entries={entries} entrySchedule={entrySchedule} {...baseProps()} />);

    expect(screen.getByLabelText('Timer mode for C')).toBeInTheDocument();
    expect(screen.getByLabelText('Timer time for C')).toBeInTheDocument();
    // The OncePerRun card has no timer controls.
    expect(screen.queryByLabelText('Timer mode for B')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Timer time for B')).not.toBeInTheDocument();
  });

  it('C7: when disabled, grouping renders but controls are disabled', () => {
    const entries = [entry('b')];
    const entrySchedule: Record<string, EntrySchedule> = {
      b: { scheduleType: 'OncePerRun', timerTimeOfDay: '' },
    };
    render(<QueueSchedulingAreas entries={entries} entrySchedule={entrySchedule} disabled {...baseProps()} />);

    expect(within(screen.getByRole('region', { name: 'Once per run' })).getByText('B')).toBeInTheDocument();
    expect(screen.getByLabelText('Schedule type for B')).toBeDisabled();
    expect(screen.getByLabelText('Remove B')).toBeDisabled();
  });

  it('reassigns via the schedule selector (non-drag path)', () => {
    const props = baseProps();
    const entries = [entry('b')];
    const entrySchedule: Record<string, EntrySchedule> = {
      b: { scheduleType: 'OncePerRun', timerTimeOfDay: '' },
    };
    render(<QueueSchedulingAreas entries={entries} entrySchedule={entrySchedule} {...props} />);

    fireEvent.change(screen.getByLabelText('Schedule type for B'), { target: { value: 'EveryStep' } });
    expect(props.onReorderAndReassign).toHaveBeenCalledTimes(1);
    const next = props.onReorderAndReassign.mock.calls[0][0];
    expect(next.schedule.b.scheduleType).toBe('EveryStep');
  });

  it('adds a sequence through the Add control', () => {
    const props = baseProps();
    render(<QueueSchedulingAreas entries={[]} entrySchedule={{}} {...props} />);
    fireEvent.change(screen.getByLabelText('Add sequence'), { target: { value: 'seq-b' } });
    fireEvent.click(screen.getByText('Add'));
    expect(props.onAdd).toHaveBeenCalledWith('seq-b');
  });
});
