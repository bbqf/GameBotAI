import React from 'react';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { StepThroughPanel } from '../StepThroughPanel';
import { mergeState, HISTORY_CAP } from '../useStepThrough';
import * as api from '../../../services/stepThrough';
import { getRunningSessions } from '../../../services/sessionsApi';
import { entry, makeState, runningSession } from '../testFixtures';

jest.mock('../../../services/stepThrough', () => ({
  ...jest.requireActual('../../../services/stepThrough'),
  startStepThrough: jest.fn(),
  getStepThrough: jest.fn(),
  runNextStep: jest.fn(),
  restartStepThrough: jest.fn(),
  setStepThroughValues: jest.fn(),
  pauseStepThroughQueue: jest.fn(),
  endStepThrough: jest.fn(),
  endStepThroughOnClose: jest.fn()
}));
jest.mock('../../../services/sessionsApi');

const start = api.startStepThrough as jest.MockedFunction<typeof api.startStepThrough>;
const getState = api.getStepThrough as jest.MockedFunction<typeof api.getStepThrough>;
const runNext = api.runNextStep as jest.MockedFunction<typeof api.runNextStep>;
const restart = api.restartStepThrough as jest.MockedFunction<typeof api.restartStepThrough>;
const setValues = api.setStepThroughValues as jest.MockedFunction<typeof api.setStepThroughValues>;
const pauseQueue = api.pauseStepThroughQueue as jest.MockedFunction<typeof api.pauseStepThroughQueue>;
const end = api.endStepThrough as jest.MockedFunction<typeof api.endStepThrough>;
const sessions = getRunningSessions as jest.MockedFunction<typeof getRunningSessions>;

const open = async (initial = makeState(), pollIdleMs = 5000) => {
  start.mockResolvedValue(initial);
  getState.mockImplementation(async () => initial);
  render(<StepThroughPanel sequenceId="seq-1" sequenceName="Daily sequence" onClose={jest.fn()} pollIdleMs={pollIdleMs} pollRunningMs={20} />);
  await screen.findByRole('option', { name: /pns on emulator-5558/ });
  fireEvent.click(screen.getByRole('button', { name: 'Start' }));
  await screen.findByLabelText('Steps');
};

describe('StepThroughPanel history, restart, values, and queue', () => {
  beforeEach(() => {
    jest.resetAllMocks();
    sessions.mockResolvedValue([runningSession()]);
    end.mockResolvedValue(undefined);
  });

  it('shows each history entry with status, iteration, message, effects, and notes', async () => {
    await open(makeState({
      history: [
        entry({ seq: 1, path: '0', status: 'Succeeded', message: 'tap(10,20) sent to emulator' }),
        entry({ seq: 2, path: '1/body/0', iteration: 2, status: 'Failed', message: 'Command not found', notes: ['sequence would end here'] }),
        entry({ seq: 3, path: '2', status: 'Succeeded', outcome: 'previewed', effects: ['would reschedule at 14:30'] })
      ]
    }));

    const history = screen.getByLabelText('History');
    const items = within(history).getAllByRole('listitem');
    expect(items).toHaveLength(3);
    expect(within(items[0]).getByText('Succeeded')).toBeInTheDocument();
    expect(within(items[1]).getByText('Failed')).toBeInTheDocument();
    expect(within(items[1]).getByText('iteration 2')).toBeInTheDocument();
    expect(within(items[1]).getByText('sequence would end here')).toBeInTheDocument();
    expect(within(items[2]).getByText(/would reschedule at 14:30/)).toBeInTheDocument();
    expect(within(items[2]).getByText(/Previewed, not applied/)).toBeInTheDocument();
  });

  it('shows a hint while the history is empty', async () => {
    await open();

    expect(screen.getByText(/No step ran yet/)).toBeInTheDocument();
  });

  it('restarts and shows the cleared history', async () => {
    await open(makeState({ history: [entry()], cursor: '2' }));
    const cleared = makeState({ history: [], cursor: '0' });
    restart.mockResolvedValue(cleared);
    getState.mockImplementation(async () => cleared);

    fireEvent.click(screen.getByRole('button', { name: 'Restart' }));

    await waitFor(() => expect(restart).toHaveBeenCalledWith('st-1'));
    await screen.findByText(/No step ran yet/);
  });

  it('applies parameter values and step outcomes', async () => {
    await open(makeState({
      parameters: [{ name: 'n', value: '5', isSet: false }],
      outcomes: { a: 'success' }
    }));
    setValues.mockResolvedValue(makeState({ parameters: [{ name: 'n', value: '9', isSet: true }], outcomes: { a: 'failed' } }));

    fireEvent.click(screen.getByText('Values'));
    fireEvent.change(screen.getByLabelText('n'), { target: { value: '9' } });
    fireEvent.change(screen.getByLabelText('a'), { target: { value: 'failed' } });
    fireEvent.click(screen.getByRole('button', { name: 'Apply values' }));

    await waitFor(() => expect(setValues).toHaveBeenCalledWith('st-1', { parameterValues: { n: '9' }, outcomes: { a: 'failed' } }));
  });

  it('keeps the text that the author types when a poll brings the same values', async () => {
    await open(makeState({ parameters: [{ name: 'n', value: '5', isSet: false }] }), 20);

    fireEvent.click(screen.getByText('Values'));
    fireEvent.change(screen.getByLabelText('n'), { target: { value: '12' } });
    await new Promise((resolve) => setTimeout(resolve, 80));

    expect(screen.getByLabelText('n')).toHaveValue('12');
  });

  it('shows a queue banner with Pause queue and pauses the queue', async () => {
    await open(makeState({ queue: { queueId: 'q1', queueName: 'Daily 5558', running: true, firingActive: false, pausedByStepThrough: false, alreadyPaused: false } }));
    pauseQueue.mockResolvedValue(makeState({ queue: { queueId: 'q1', queueName: 'Daily 5558', running: false, firingActive: false, pausedByStepThrough: true, alreadyPaused: false } }));
    getState.mockImplementation(async () => makeState({ queue: { queueId: 'q1', queueName: 'Daily 5558', running: false, firingActive: false, pausedByStepThrough: true, alreadyPaused: false } }));

    expect(screen.getByText(/The queue "Daily 5558" runs on this device/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Pause queue' }));

    await waitFor(() => expect(pauseQueue).toHaveBeenCalledWith('st-1'));
    expect(await screen.findByText(/paused by this step-through/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Pause queue' })).toBeNull();
  });

  it('does not offer Pause queue while a firing runs', async () => {
    await open(makeState({ queue: { queueId: 'q1', queueName: 'Daily 5558', running: true, firingActive: true, pausedByStepThrough: false, alreadyPaused: false } }));

    expect(screen.getByText(/runs a firing on this device now/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Pause queue' })).toBeNull();
  });

  it('says that a queue that was paused before stays paused', async () => {
    await open(makeState({ queue: { queueId: 'q1', queueName: 'Daily 5558', running: false, firingActive: false, pausedByStepThrough: false, alreadyPaused: true } }));

    expect(screen.getByText(/was paused before/)).toBeInTheDocument();
  });

  it('offers Restart when the saved sequence changed', async () => {
    await open();
    runNext.mockRejectedValue(new api.StepThroughError(409, 'sequence_changed', 'The saved sequence changed. Restart the step-through.'));
    restart.mockResolvedValue(makeState());

    fireEvent.click(screen.getByRole('button', { name: 'Run next step' }));

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('The saved sequence changed');
    fireEvent.click(within(alert).getByRole('button', { name: 'Restart' }));
    await waitFor(() => expect(restart).toHaveBeenCalledWith('st-1'));
  });
});

describe('mergeState', () => {
  it('adds only the newer history entries of a partial read', () => {
    const previous = makeState({ history: [entry({ seq: 1 }), entry({ seq: 2 })] });
    const next = makeState({ cursor: '2', history: [entry({ seq: 3 })] });

    const merged = mergeState(previous, next, true);

    expect(merged.history.map((e) => e.seq)).toEqual([1, 2, 3]);
    expect(merged.cursor).toBe('2');
  });

  it('replaces the history for a full read', () => {
    const previous = makeState({ history: [entry({ seq: 1 })] });
    const next = makeState({ history: [] });

    expect(mergeState(previous, next, false).history).toEqual([]);
  });

  it('keeps at most the newest 1000 entries', () => {
    const previous = makeState({ history: Array.from({ length: HISTORY_CAP }, (_, i) => entry({ seq: i + 1 })) });
    const next = makeState({ history: [entry({ seq: HISTORY_CAP + 1 })] });

    const merged = mergeState(previous, next, true);

    expect(merged.history).toHaveLength(HISTORY_CAP);
    expect(merged.history[0].seq).toBe(2);
    expect(merged.history[HISTORY_CAP - 1].seq).toBe(HISTORY_CAP + 1);
  });

  it('takes the new state when the id is different', () => {
    const merged = mergeState(makeState({ id: 'old', history: [entry({ seq: 1 })] }), makeState({ id: 'new', history: [] }), true);

    expect(merged.id).toBe('new');
    expect(merged.history).toEqual([]);
  });
});
