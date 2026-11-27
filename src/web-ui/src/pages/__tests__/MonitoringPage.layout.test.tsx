import React from 'react';
import { act, fireEvent, render, screen } from '@testing-library/react';
import { MonitoringPage } from '../MonitoringPage';
import * as monitoringApi from '../../services/monitoringApi';
import { installObjectUrlMock, makeSession } from '../../testUtils/monitoring';

jest.mock('../../services/monitoringApi');
const api = monitoringApi as jest.Mocked<typeof monitoringApi>;

const advance = async (ms: number) => {
  await act(async () => {
    await jest.advanceTimersByTimeAsync(ms);
  });
};
const flush = () => advance(0);

describe('MonitoringPage layout (feature 141, US3)', () => {
  beforeEach(() => {
    jest.useFakeTimers();
    installObjectUrlMock();
    window.history.replaceState(null, '', '/?area=monitoring');
    api.listMonitoredSessions.mockReset();
    api.fetchSnapshot.mockReset();
    api.fetchSnapshot.mockResolvedValue(new Blob(['png']));
    api.listMonitoredSessions.mockResolvedValue([makeSession('a'), makeSession('b')]);
  });
  afterEach(() => {
    jest.useRealTimers();
    jest.restoreAllMocks();
    window.history.replaceState(null, '', '/');
  });

  it('keeps the selected session after a switch from desktop to phone and back, with no remount', async () => {
    const view = render(<MonitoringPage layout="desktop" />);
    await flush();
    fireEvent.click(screen.getByRole('button', { name: 'Open pns (emulator-b)' }));
    await flush();
    expect(screen.getByRole('heading', { name: 'pns (emulator-b)' })).toBeInTheDocument();
    expect(screen.getByTestId('monitoring-page')).toHaveClass('monitoring-page--desktop');
    const listCalls = api.listMonitoredSessions.mock.calls.length;

    view.rerender(<MonitoringPage layout="phone" />);
    await flush();
    expect(screen.getByTestId('monitoring-page')).toHaveClass('monitoring-page--phone');
    expect(screen.getByRole('heading', { name: 'pns (emulator-b)' })).toBeInTheDocument();
    expect(window.location.search).toContain('session=b');
    // No remount: the list is not loaded again by a mount.
    expect(api.listMonitoredSessions.mock.calls.length).toBe(listCalls);

    view.rerender(<MonitoringPage layout="desktop" />);
    await flush();
    expect(screen.getByRole('heading', { name: 'pns (emulator-b)' })).toBeInTheDocument();
    expect(api.listMonitoredSessions.mock.calls.length).toBe(listCalls);
  });

  it('on a phone shows the list first, opens the detail with a new history entry, and back calls history.back()', async () => {
    const push = jest.spyOn(window.history, 'pushState');
    const back = jest.spyOn(window.history, 'back').mockImplementation(() => undefined);
    render(<MonitoringPage layout="phone" />);
    await flush();
    expect(screen.getByRole('list', { name: 'Sessions' })).toBeInTheDocument();
    expect(screen.getByTestId('session-row-a')).toBeInTheDocument();
    expect(screen.queryByTestId('monitoring-detail')).toBeNull();

    fireEvent.click(screen.getByRole('button', { name: 'Open pns (emulator-a)' }));
    await flush();
    expect(push).toHaveBeenCalledTimes(1);
    expect(window.location.search).toContain('session=a');
    expect(screen.getByTestId('monitoring-detail')).toBeInTheDocument();
    expect(screen.queryByRole('list', { name: 'Sessions' })).toBeNull();

    fireEvent.click(screen.getByRole('button', { name: 'Back' }));
    expect(back).toHaveBeenCalledTimes(1);
  });

  it('follows the history: a back step to an address with no session shows the list again', async () => {
    render(<MonitoringPage layout="phone" />);
    await flush();
    fireEvent.click(screen.getByRole('button', { name: 'Open pns (emulator-a)' }));
    await flush();
    expect(screen.getByTestId('monitoring-detail')).toBeInTheDocument();

    window.history.replaceState(window.history.state, '', '/?area=monitoring');
    await act(async () => { window.dispatchEvent(new PopStateEvent('popstate')); });
    expect(screen.queryByTestId('monitoring-detail')).toBeNull();
    expect(screen.getByRole('list', { name: 'Sessions' })).toBeInTheDocument();
  });

  it('restores the selection from the address query', async () => {
    window.history.replaceState(null, '', '/?area=monitoring&session=a');
    render(<MonitoringPage layout="desktop" />);
    await flush();
    expect(screen.getByRole('heading', { name: 'pns (emulator-a)' })).toBeInTheDocument();
  });

  it('on the desktop opens the detail page in place of the list, and Back shows the list', async () => {
    render(<MonitoringPage layout="desktop" />);
    await flush();
    expect(screen.getByRole('table', { name: 'Sessions' })).toBeInTheDocument();
    fireEvent.click(screen.getByTestId('session-row-a'));
    await flush();
    expect(screen.getByTestId('monitoring-detail')).toBeInTheDocument();
    expect(screen.queryByRole('table', { name: 'Sessions' })).toBeNull();
    fireEvent.click(screen.getByRole('button', { name: 'Back' }));
    await flush();
    expect(screen.queryByTestId('monitoring-detail')).toBeNull();
    expect(screen.getByRole('table', { name: 'Sessions' })).toBeInTheDocument();
    expect(window.location.search).not.toContain('session=');
  });

  it('shows the list states: loading, empty, and an error with a retry', async () => {
    api.listMonitoredSessions.mockReset();
    api.listMonitoredSessions.mockRejectedValueOnce(new Error('Boom')).mockResolvedValue([]);
    render(<MonitoringPage layout="desktop" />);
    expect(screen.getByText('Loading…')).toBeInTheDocument();
    await flush();
    expect(screen.getAllByRole('alert')[0]).toHaveTextContent('Boom');
    fireEvent.click(screen.getByRole('button', { name: 'Try again' }));
    await flush();
    expect(screen.getByText('No running sessions.')).toBeInTheDocument();
  });

  it('keeps the detail when a list call fails', async () => {
    window.history.replaceState(null, '', '/?area=monitoring&session=a');
    render(<MonitoringPage layout="desktop" />);
    await flush();
    api.listMonitoredSessions.mockRejectedValue(new Error('Boom'));
    await advance(5000);
    expect(screen.getByTestId('monitoring-detail')).toBeInTheDocument();
  });
});
