import React from 'react';
import { act, fireEvent, render, screen, within } from '@testing-library/react';
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
  fireEvent.click(screen.getByRole('button', { name: /^Open / }));
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

  it('shows the sequence name, never the queue id, in the detail', async () => {
    api.listMonitoredSessions.mockResolvedValue([
      makeSession('a', { ownerQueueId: 'q-id-1', ownerQueueName: null, currentSequenceName: 'Help All' })
    ]);
    await openSession(<MonitoringPage layout="desktop" />);
    expect(screen.getByTestId('monitoring-sequence')).toHaveTextContent('Help All');
    expect(screen.queryByText(/q-id-1/)).toBeNull();
  });

  it('shows the "Not live" mark in the Device row of the selected session when the owner queue device is not live (feature 199)', async () => {
    api.listMonitoredSessions.mockResolvedValue([
      makeSession('a', { ownerQueueId: 'q1', ownerQueueName: 'PNS', ownerQueueDeviceLiveness: 'not_live' })
    ]);
    await openSession(<MonitoringPage layout="desktop" />);
    const detail = within(screen.getByTestId('monitoring-detail'));
    expect(detail.getByText('Not live')).toBeInTheDocument();
    expect(detail.getByText('Device').closest('.ui-kv__row')).toContainElement(detail.getByText('Not live'));
    expect(detail.getByRole('heading', { name: 'pns (emulator-a)' })).not.toContainElement(detail.getByText('Not live'));
  });

  it('shows no "Not live" mark in the header when the owner queue device is live (feature 199)', async () => {
    api.listMonitoredSessions.mockResolvedValue([
      makeSession('a', { ownerQueueId: 'q1', ownerQueueName: 'PNS', ownerQueueDeviceLiveness: 'live' })
    ]);
    await openSession(<MonitoringPage layout="desktop" />);
    expect(within(screen.getByTestId('monitoring-detail')).queryByText('Not live')).toBeNull();
  });

  it('shows a dash and no test id when the queue runs no sequence', async () => {
    api.listMonitoredSessions.mockResolvedValue([makeSession('a', { ownerQueueId: 'q1', ownerQueueName: 'PNS' })]);
    await openSession(<MonitoringPage layout="desktop" />);
    expect(screen.queryByTestId('monitoring-sequence')).toBeNull();
    expect(screen.getByText('Sequence').closest('.ui-kv__row')).toHaveTextContent('—');
  });

  it('shows the Reason row only for no answer, and a Started date', async () => {
    api.listMonitoredSessions.mockResolvedValue([makeSession('a', { state: 'no_answer', reason: 'No change after input' })]);
    const view = await openSession(<MonitoringPage layout="desktop" />);
    expect(screen.getByText('Reason').closest('.ui-kv__row')).toHaveTextContent('No change after input');
    expect(screen.getByText('Started').closest('.ui-kv__row')).not.toHaveTextContent('—');
    view.unmount();
    window.history.replaceState(null, '', '/?area=monitoring');
    api.listMonitoredSessions.mockResolvedValue([makeSession('a', { state: 'live', reason: 'old' })]);
    await openSession(<MonitoringPage layout="desktop" />);
    expect(screen.queryByText('Reason')).toBeNull();
  });

  it('shows the reason after the text when the restart is not available', async () => {
    api.listMonitoredSessions.mockResolvedValue([
      makeSession('a', { restartAvailable: false, restartUnavailableReason: 'The emulator is not an LDPlayer instance.' })
    ]);
    await openSession(<MonitoringPage layout="desktop" />);
    expect(screen.getByTestId('restart-unavailable')).toHaveTextContent(
      'Restart is not available for this session. The emulator is not an LDPlayer instance.'
    );
  });

  it('names the dialog after the session and shows the message', async () => {
    await openSession(<MonitoringPage layout="desktop" />);
    fireEvent.click(screen.getByRole('button', { name: 'Restart emulator' }));
    expect(screen.getByRole('dialog', { name: 'Restart emulator pns (emulator-a)?' })).toBeInTheDocument();
    expect(screen.getByText('The whole emulator stops and starts again.')).toBeInTheDocument();
  });

  it('closes the dialog with Escape and sends no call', async () => {
    await openSession(<MonitoringPage layout="desktop" />);
    fireEvent.click(screen.getByRole('button', { name: 'Restart emulator' }));
    fireEvent.keyDown(document, { key: 'Escape' });
    expect(screen.queryByRole('dialog')).toBeNull();
    expect(api.startRestart).not.toHaveBeenCalled();
  });

  it('puts the focus on Cancel when the dialog opens', async () => {
    await openSession(<MonitoringPage layout="desktop" />);
    fireEvent.click(screen.getByRole('button', { name: 'Restart emulator' }));
    expect(screen.getByRole('button', { name: 'Cancel' })).toHaveFocus();
  });

  it('shows no queue warning without a queue', async () => {
    await openSession(<MonitoringPage layout="desktop" />);
    fireEvent.click(screen.getByRole('button', { name: 'Restart emulator' }));
    expect(screen.queryByTestId('restart-queue-warning')).toBeNull();
  });

  it('shows "(no name)" in the warning when the queue has no name', async () => {
    api.listMonitoredSessions.mockResolvedValue([makeSession('a', { ownerQueueId: 'q1', ownerQueueName: null })]);
    await openSession(<MonitoringPage layout="desktop" />);
    fireEvent.click(screen.getByRole('button', { name: 'Restart emulator' }));
    expect(screen.getByTestId('restart-queue-warning')).toHaveTextContent('Queue (no name) uses this emulator.');
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
