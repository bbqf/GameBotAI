import React from 'react';
import { act, fireEvent, render, screen } from '@testing-library/react';
import { MonitoringPage } from '../MonitoringPage';
import * as monitoringApi from '../../services/monitoringApi';
import { ApiError } from '../../lib/api';
import { installObjectUrlMock, makeRecord, makeSession } from '../../testUtils/monitoring';

jest.mock('../../services/monitoringApi');
const api = monitoringApi as jest.Mocked<typeof monitoringApi>;

const advance = async (ms: number) => {
  await act(async () => {
    await jest.advanceTimersByTimeAsync(ms);
  });
};
const flush = () => advance(0);

const openSession = async (ui: React.ReactElement) => {
  const view = render(ui);
  await flush();
  fireEvent.click(screen.getByRole('button', { name: /^Select / }));
  await flush();
  return view;
};

const confirmRestart = async () => {
  fireEvent.click(screen.getByRole('button', { name: 'Restart emulator' }));
  fireEvent.click(screen.getByRole('button', { name: 'Restart' }));
  await flush();
};

describe('MonitoringPage restart (feature 141, US2)', () => {
  beforeEach(() => {
    jest.useFakeTimers();
    installObjectUrlMock();
    window.history.replaceState(null, '', '/?area=monitoring');
    api.listMonitoredSessions.mockReset();
    api.fetchSnapshot.mockReset();
    api.startRestart.mockReset();
    api.getRestart.mockReset();
    api.fetchSnapshot.mockResolvedValue(new Blob(['png']));
    api.listMonitoredSessions.mockResolvedValue([makeSession('a')]);
  });
  afterEach(() => {
    jest.useRealTimers();
    window.history.replaceState(null, '', '/');
  });

  it('sends one POST on Restart and none on Cancel', async () => {
    api.startRestart.mockResolvedValue(makeRecord('a'));
    api.getRestart.mockReturnValue(new Promise(() => undefined));
    await openSession(<MonitoringPage layout="desktop" />);

    fireEvent.click(screen.getByRole('button', { name: 'Restart emulator' }));
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(api.startRestart).not.toHaveBeenCalled();

    await confirmRestart();
    expect(api.startRestart).toHaveBeenCalledTimes(1);
    expect(api.startRestart).toHaveBeenCalledWith('a');
  });

  it('shows the queue warning in the dialog when a queue owns the session', async () => {
    api.listMonitoredSessions.mockResolvedValue([makeSession('a', { ownerQueueId: 'q1', ownerQueueName: 'PNS Daily 5558' })]);
    await openSession(<MonitoringPage layout="desktop" />);
    fireEvent.click(screen.getByRole('button', { name: 'Restart emulator' }));
    expect(screen.getByTestId('restart-queue-warning')).toHaveTextContent('Queue PNS Daily 5558 uses this emulator.');
  });

  it('shows the step text while the restart runs and the control is off', async () => {
    api.startRestart.mockResolvedValue(makeRecord('a', { step: 'rebooting' }));
    api.getRestart.mockResolvedValue(makeRecord('a', { step: 'waiting_for_device' }));
    await openSession(<MonitoringPage layout="desktop" />);
    await confirmRestart();
    expect(screen.getByTestId('restart-status')).toHaveTextContent('Restarting: waiting for the emulator');
    expect(screen.getByRole('button', { name: 'Restart emulator' })).toBeDisabled();
  });

  it('shows "Emulator restarted" on success', async () => {
    api.startRestart.mockResolvedValue(makeRecord('a'));
    api.getRestart.mockResolvedValue(makeRecord('a', { state: 'success', step: 'done', endedAtUtc: '2026-10-13T08:11:00Z' }));
    await openSession(<MonitoringPage layout="desktop" />);
    await confirmRestart();
    expect(screen.getByTestId('restart-status')).toHaveTextContent('Emulator restarted');
    expect(screen.getByRole('button', { name: 'Restart emulator' })).not.toBeDisabled();
  });

  it('shows "Restart failed" with the reason text', async () => {
    api.startRestart.mockResolvedValue(makeRecord('a'));
    api.getRestart.mockResolvedValue(makeRecord('a', { state: 'failure', step: 'waiting_for_device', reason: 'timeout' }));
    await openSession(<MonitoringPage layout="desktop" />);
    await confirmRestart();
    expect(screen.getByTestId('restart-status')).toHaveTextContent('Restart failed. The emulator did not answer in time.');
  });

  it('shows the 409 message and does not send a second POST', async () => {
    api.startRestart.mockRejectedValue(new ApiError(409, 'A restart is already running for this emulator.'));
    api.getRestart.mockReturnValue(new Promise(() => undefined));
    await openSession(<MonitoringPage layout="desktop" />);
    await confirmRestart();
    expect(screen.getByTestId('restart-message')).toHaveTextContent('A restart is already running for this emulator.');
    await advance(10000);
    expect(api.startRestart).toHaveBeenCalledTimes(1);
  });

  it('never sends the POST again after a connection loss, and turns the control off', async () => {
    api.startRestart.mockResolvedValue(makeRecord('a'));
    api.getRestart.mockReturnValue(new Promise(() => undefined));
    const view = await openSession(<MonitoringPage layout="desktop" connectionState="online" />);
    await confirmRestart();
    expect(api.startRestart).toHaveBeenCalledTimes(1);

    view.rerender(<MonitoringPage layout="desktop" connectionState="lost" />);
    await advance(20000);
    expect(screen.getByRole('button', { name: 'Restart emulator' })).toBeDisabled();
    view.rerender(<MonitoringPage layout="desktop" connectionState="online" />);
    await advance(20000);
    expect(api.startRestart).toHaveBeenCalledTimes(1);
  });

  it('turns the control off and shows the reason when restartAvailable is false', async () => {
    api.listMonitoredSessions.mockResolvedValue([
      makeSession('a', { restartAvailable: false, restartUnavailableReason: 'unsupported_serial' })
    ]);
    await openSession(<MonitoringPage layout="desktop" />);
    expect(screen.getByRole('button', { name: 'Restart emulator' })).toBeDisabled();
    expect(screen.getByTestId('restart-unavailable')).toHaveTextContent('Restart is not available for this session.');
  });
});
