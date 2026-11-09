import React from 'react';
import { fireEvent, render, screen, within } from '@testing-library/react';
import { QueueSchedulingAreas } from '../QueueSchedulingAreas';
import { EntrySchedule } from '../QueueEntryList';
import { describeRunNext, RunNextSupport } from '../runNext';
import { EntryState, QueueEntryDto } from '../../../services/queues';
import { SequenceDto } from '../../../services/sequences';

const sequences = [{ id: 'seq-a', name: 'Alpha', steps: [] }] as unknown as SequenceDto[];

const entry = (entryId: string): QueueEntryDto => ({ entryId, sequenceId: `seq-${entryId}`, sequenceName: entryId.toUpperCase(), stale: false });

const baseProps = () => ({
  sequences,
  onAdd: jest.fn(),
  onRemove: jest.fn(),
  onReorderAndReassign: jest.fn(),
});

const schedule = (enabled = true): Record<string, EntrySchedule> => ({
  a: { scheduleType: 'OncePerRun', timerTimeOfDay: '', enabled },
});

const renderRow = (entryId: string, runNext: RunNextSupport | undefined, enabled = true) =>
  render(<QueueSchedulingAreas entries={[entry(entryId)]} entrySchedule={{ [entryId]: schedule(enabled).a }} runNext={runNext} {...baseProps()} />);

const support = (state: EntryState | undefined, over: Partial<RunNextSupport> = {}): RunNextSupport => ({
  queueRunning: true,
  states: state ? { a: state } : {},
  onRunNext: jest.fn(),
  ...over,
});

describe('Run next button of a row (feature 184, contracts/editor-icons-contract.md)', () => {
  it('shows an on button with the name "Run next <label>" for an idle entry', () => {
    renderRow('a', support('idle'));

    const button = screen.getByRole('button', { name: 'Run next A' });
    expect(button).toBeEnabled();
    expect(button).toHaveAttribute('title', 'Run next');
    expect(button.textContent).toBe('');
  });

  it.each([
    ['queued', 'Queued to run next'],
    ['running', 'Running'],
  ] as const)('shows an off button and the row text "%s" for the state', (state, text) => {
    renderRow('a', support(state));

    const button = screen.getByRole('button', { name: 'Run next A' });
    expect(button).toBeDisabled();
    expect(button).toHaveAttribute('title', text);
    expect(within(screen.getByTestId('queue-entry')).getByText(text)).toBeInTheDocument();
  });

  it('shows an off button and the text "Scheduled" for a scheduled entry', () => {
    renderRow('a', support('scheduled'));

    const button = screen.getByRole('button', { name: 'Run next A' });
    expect(button).toBeDisabled();
    expect(button).toHaveAttribute('title', 'Scheduled. Use the timer to change it.');
    expect(within(screen.getByTestId('queue-entry')).getByText('Scheduled')).toBeInTheDocument();
  });

  it('calls onRunNext once with the entry ID for a press', () => {
    const runNext = support('idle');
    renderRow('a', runNext);

    fireEvent.click(screen.getByRole('button', { name: 'Run next A' }));

    expect(runNext.onRunNext).toHaveBeenCalledTimes(1);
    expect(runNext.onRunNext).toHaveBeenCalledWith('a');
  });

  it('keeps the button off while a call runs and when the connection is lost', () => {
    const { unmount } = renderRow('a', support('idle', { busy: true }));
    expect(screen.getByRole('button', { name: 'Run next A' })).toBeDisabled();
    unmount();

    renderRow('a', support('idle', { lost: true }));
    const button = screen.getByRole('button', { name: 'Run next A' });
    expect(button).toBeDisabled();
    expect(button).toHaveAttribute('title', 'Connection lost');
  });

  it('shows the reason as text for a new row, a row with no entry ID, and an entry that is not in the plan', () => {
    const { unmount } = renderRow('new-0', support('idle'));
    expect(screen.getByRole('button', { name: 'Run next NEW-0' })).toBeDisabled();
    expect(screen.getAllByText('Save the template first').length).toBeGreaterThan(0);
    unmount();

    const second = renderRow('row-1', support('idle'));
    expect(screen.getByRole('button', { name: 'Run next ROW-1' })).toBeDisabled();
    expect(screen.getAllByText('Upgrade the template to use Run next').length).toBeGreaterThan(0);
    second.unmount();

    renderRow('a', support(undefined));
    expect(screen.getByRole('button', { name: 'Run next A' })).toBeDisabled();
    expect(screen.getAllByText('Apply the template changes first').length).toBeGreaterThan(0);
  });

  it('keeps the button off for a disabled entry', () => {
    renderRow('a', support('idle'), false);

    const button = screen.getByRole('button', { name: 'Run next A' });
    expect(button).toBeDisabled();
    expect(button).toHaveAttribute('title', 'The entry is disabled');
  });

  it('shows the button off and one hint on the screen when the queue does not run', () => {
    renderRow('a', { queueRunning: false });

    const button = screen.getByRole('button', { name: 'Run next A' });
    expect(button).toBeDisabled();
    expect(button).toHaveAttribute('title', 'Start the queue to use Run next');
    expect(screen.getByText('Start the queue to use Run next.')).toBeInTheDocument();
  });

  it('shows no Run next button and no hint when the editor gives no support', () => {
    renderRow('a', undefined);

    expect(screen.queryByRole('button', { name: /Run next/ })).toBeNull();
    expect(screen.queryByText(/Start the queue to use Run next/)).toBeNull();
  });
});

describe('describeRunNext', () => {
  it('gives the reason of each off case, and turns the button on only for an idle entry with a handler', () => {
    const base = { queueRunning: true, states: { a: 'idle' as const }, onRunNext: jest.fn() };

    expect(describeRunNext(base, 'a', true)).toEqual({ enabled: true, tooltip: 'Run next', note: null });
    expect(describeRunNext({ ...base, onRunNext: undefined }, 'a', true).enabled).toBe(false);
    expect(describeRunNext({ queueRunning: false }, 'a', true).tooltip).toBe('Start the queue to use Run next');
    expect(describeRunNext(base, 'new-3', true).note).toBe('Save the template first');
    expect(describeRunNext(base, 'row-3', true).note).toBe('Upgrade the template to use Run next');
    expect(describeRunNext(base, 'a', false).tooltip).toBe('The entry is disabled');
    expect(describeRunNext({ ...base, states: { a: 'queued' } }, 'a', true).note).toBe('Queued to run next');
    expect(describeRunNext({ ...base, states: {} }, 'a', true).note).toBe('Apply the template changes first');
  });
});
