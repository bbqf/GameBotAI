import React from 'react';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { StepThroughPanel } from '../StepThroughPanel';
import * as api from '../../../services/stepThrough';
import { getRunningSessions } from '../../../services/sessionsApi';
import { entry, makeState, runningSession } from '../testFixtures';

jest.mock('../../../services/stepThrough', () => ({
  ...jest.requireActual('../../../services/stepThrough'),
  startStepThrough: jest.fn(),
  getStepThrough: jest.fn(),
  runNextStep: jest.fn(),
  selectStep: jest.fn(),
  cancelStep: jest.fn(),
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
const cancel = api.cancelStep as jest.MockedFunction<typeof api.cancelStep>;
const end = api.endStepThrough as jest.MockedFunction<typeof api.endStepThrough>;
const sessions = getRunningSessions as jest.MockedFunction<typeof getRunningSessions>;

// The idle poll is slow, so a poll does not change the state during a test that does not need it.
const renderPanel = (onClose = jest.fn(), pollIdleMs = 5000) =>
  render(<StepThroughPanel sequenceId="seq-1" sequenceName="Daily sequence" onClose={onClose} pollIdleMs={pollIdleMs} pollRunningMs={20} />);

const startPanel = async (pollIdleMs = 5000) => {
  const view = renderPanel(jest.fn(), pollIdleMs);
  await screen.findByRole('option', { name: /pns on emulator-5558/ });
  fireEvent.click(screen.getByRole('button', { name: 'Start' }));
  await screen.findByLabelText('Steps');
  return view;
};

describe('StepThroughPanel', () => {
  beforeEach(() => {
    jest.resetAllMocks();
    sessions.mockResolvedValue([runningSession()]);
    start.mockResolvedValue(makeState());
    getState.mockImplementation(async () => makeState());
    end.mockResolvedValue(undefined);
  });

  it('lets the author select a game session and start', async () => {
    renderPanel();

    await screen.findByRole('option', { name: /pns on emulator-5558/ });
    fireEvent.click(screen.getByRole('button', { name: 'Start' }));

    await waitFor(() => expect(start).toHaveBeenCalledWith({ sequenceId: 'seq-1', gameSessionId: 'gs-1' }));
  });

  it('disables Start and explains what to do when no game session is connected', async () => {
    sessions.mockResolvedValue([]);
    renderPanel();

    expect(await screen.findByText(/Start a game session on the Execution page/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Start' })).toBeDisabled();
  });

  it('shows the steps with indent and puts the marker on the next step', async () => {
    await startPanel();

    const list = screen.getByLabelText('Steps');
    const rows = within(list).getAllByRole('listitem');
    expect(rows).toHaveLength(4);
    expect(rows[0]).toHaveAttribute('aria-current', 'step');
    expect(rows[1]).not.toHaveAttribute('aria-current');
    expect(rows[2]).toHaveStyle({ paddingLeft: '20px' });
    expect(rows[0]).toHaveStyle({ paddingLeft: '0px' });
    expect(within(rows[0]).getByText('next')).toBeInTheDocument();
  });

  it('runs the next step and shows the status after the step ends', async () => {
    await startPanel();
    runNext.mockResolvedValue(makeState({ state: 'running', running: { path: '0', startedAt: '2026-10-02T10:00:01Z' } }));
    getState.mockResolvedValue(makeState({ cursor: '1', history: [entry({ seq: 1, status: 'Succeeded', message: 'tap(10,20) sent to emulator' })] }));

    fireEvent.click(screen.getByRole('button', { name: 'Run next step' }));

    expect(await screen.findByText('tap(10,20) sent to emulator')).toBeInTheDocument();
    expect(screen.getByLabelText('History')).toBeInTheDocument();
    expect(screen.getByTestId('step-through-state')).toHaveTextContent('idle');
    expect(screen.getByRole('button', { name: 'Run next step' })).toBeEnabled();
  });

  it('disables Run next step while a step runs and offers Cancel step', async () => {
    start.mockResolvedValue(makeState({ state: 'running', running: { path: '0', startedAt: '2026-10-02T10:00:01Z' } }));
    getState.mockResolvedValue(makeState({ state: 'running', running: { path: '0', startedAt: '2026-10-02T10:00:01Z' } }));
    await startPanel();

    expect(screen.getByRole('button', { name: 'Run next step' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Cancel step' })).toBeEnabled();
    expect(screen.getByTestId('step-through-state')).toHaveTextContent('running');
    expect(screen.getByText(/A step runs now/)).toBeInTheDocument();

    cancel.mockResolvedValue(makeState({ state: 'running', running: { path: '0', startedAt: '2026-10-02T10:00:01Z' } }));
    fireEvent.click(screen.getByRole('button', { name: 'Cancel step' }));
    await waitFor(() => expect(cancel).toHaveBeenCalledWith('st-1'));
  });

  it('shows the complete state and disables Run next step', async () => {
    start.mockResolvedValue(makeState({ state: 'complete', cursor: null, history: [entry()] }));
    getState.mockResolvedValue(makeState({ state: 'complete', cursor: null, history: [entry()] }));
    await startPanel();

    expect(screen.getByTestId('step-through-state')).toHaveTextContent('complete');
    expect(screen.getByRole('button', { name: 'Run next step' })).toBeDisabled();
    expect(screen.getByText(/The sequence is complete/)).toBeInTheDocument();
  });

  it('shows the message of an error from the API', async () => {
    await startPanel();
    runNext.mockRejectedValue(new api.StepThroughError(409, 'queue_running', 'The queue "Daily" runs on this device.'));

    fireEvent.click(screen.getByRole('button', { name: 'Run next step' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('The queue "Daily" runs on this device.');
  });

  it('ends the step-through and calls onClose when the author clicks Close', async () => {
    const onClose = jest.fn();
    render(<StepThroughPanel sequenceId="seq-1" sequenceName="Daily sequence" onClose={onClose} pollIdleMs={5000} pollRunningMs={20} />);
    await screen.findByRole('option', { name: /pns on emulator-5558/ });
    fireEvent.click(screen.getByRole('button', { name: 'Start' }));
    await screen.findByLabelText('Steps');

    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: 'Close' }));
    });

    expect(end).toHaveBeenCalledWith('st-1');
    expect(onClose).toHaveBeenCalled();
  });

  it('ends the step-through when the view unmounts', async () => {
    const view = await startPanel();

    view.unmount();

    expect(end).toHaveBeenCalledWith('st-1');
  });

  it('tells the author when the lease ended', async () => {
    await startPanel(20);
    getState.mockRejectedValue(new api.StepThroughError(404, 'step_through_not_found', 'gone'));

    expect(await screen.findByText(/sent no read for 90 seconds/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Start' })).toBeInTheDocument();
  });
});
