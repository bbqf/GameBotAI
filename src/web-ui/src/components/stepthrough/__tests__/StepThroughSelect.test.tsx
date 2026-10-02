import React from 'react';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
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
  endStepThrough: jest.fn(),
  endStepThroughOnClose: jest.fn()
}));
jest.mock('../../../services/sessionsApi');

const start = api.startStepThrough as jest.MockedFunction<typeof api.startStepThrough>;
const getState = api.getStepThrough as jest.MockedFunction<typeof api.getStepThrough>;
const select = api.selectStep as jest.MockedFunction<typeof api.selectStep>;
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

const rowOf = (path: string) => document.querySelector(`li[data-path="${path}"]`) as HTMLElement;

describe('StepThroughPanel manual selection', () => {
  beforeEach(() => {
    jest.resetAllMocks();
    sessions.mockResolvedValue([runningSession()]);
    end.mockResolvedValue(undefined);
  });

  it('selects a step when the author clicks its row and moves the marker', async () => {
    await open();
    const moved = makeState({ cursor: '2' });
    select.mockResolvedValue(moved);
    getState.mockImplementation(async () => moved);

    fireEvent.click(within(rowOf('2')).getByRole('button', { name: 'tap 30,40' }));

    await waitFor(() => expect(select).toHaveBeenCalledWith('st-1', '2'));
    await waitFor(() => expect(rowOf('2')).toHaveAttribute('aria-current', 'step'));
    expect(rowOf('0')).not.toHaveAttribute('aria-current');
  });

  it('can select a step inside a loop body', async () => {
    await open();
    select.mockResolvedValue(makeState({ cursor: '1/body/0' }));

    fireEvent.click(within(rowOf('1/body/0')).getByRole('button', { name: 'command open-menu' }));

    await waitFor(() => expect(select).toHaveBeenCalledWith('st-1', '1/body/0'));
  });

  it('does not make a loop or an if header row clickable', async () => {
    await open();

    expect(within(rowOf('1')).queryByRole('button')).toBeNull();
    expect(within(rowOf('1')).getByText('loop 2 times')).toBeInTheDocument();
  });

  it('keeps Run next step enabled after the sequence is complete when the author selected a step', async () => {
    await open(makeState({ state: 'complete', cursor: null, history: [entry()] }));
    expect(screen.getByRole('button', { name: 'Run next step' })).toBeDisabled();
    const selected = makeState({ state: 'idle', cursor: '0', history: [entry()] });
    select.mockResolvedValue(selected);
    getState.mockImplementation(async () => selected);

    fireEvent.click(within(rowOf('0')).getByRole('button', { name: 'tap 10,20' }));

    await waitFor(() => expect(screen.getByRole('button', { name: 'Run next step' })).toBeEnabled());
  });

  it('blocks selection while a step runs', async () => {
    await open(makeState({ state: 'running', running: { path: '0', startedAt: '2026-10-02T10:00:01Z' } }));

    expect(within(rowOf('2')).getByRole('button', { name: 'tap 30,40' })).toBeDisabled();
  });

  it('shows the last status of a step in its row', async () => {
    await open(makeState({ history: [entry({ path: '0', status: 'Failed' })] }));

    expect(within(rowOf('0')).getByText('Failed')).toBeInTheDocument();
  });
});
