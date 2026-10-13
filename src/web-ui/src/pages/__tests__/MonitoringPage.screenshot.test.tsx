import React from 'react';
import { act, fireEvent, render, screen, within } from '@testing-library/react';
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

describe('MonitoringPage list and screenshot (feature 141, US1)', () => {
  beforeEach(() => {
    jest.useFakeTimers();
    installObjectUrlMock();
    window.history.replaceState(null, '', '/?area=monitoring');
    api.listMonitoredSessions.mockReset();
    api.fetchSnapshot.mockReset();
    api.fetchSnapshot.mockResolvedValue(new Blob(['png']));
  });
  afterEach(() => {
    jest.useRealTimers();
    window.history.replaceState(null, '', '/');
  });

  it('lists all sessions with a state chip', async () => {
    api.listMonitoredSessions.mockResolvedValue([
      makeSession('a'),
      makeSession('b', { state: 'no_answer', reason: 'capture_stalled' }),
      makeSession('c', { state: 'restarting' }),
      makeSession('d', { state: 'stopping' })
    ]);
    render(<MonitoringPage layout="desktop" />);
    await flush();
    const list = screen.getByRole('list', { name: 'Sessions' });
    const rows = within(list).getAllByRole('listitem');
    expect(rows).toHaveLength(4);
    expect(within(rows[0]).getByText('Live')).toBeInTheDocument();
    expect(within(rows[1]).getByText('No answer')).toBeInTheDocument();
    expect(within(rows[2]).getByText('Restarting')).toBeInTheDocument();
    expect(within(rows[3]).getByText('Stopping')).toBeInTheDocument();
  });

  it('shows "No running sessions." for an empty list', async () => {
    api.listMonitoredSessions.mockResolvedValue([]);
    render(<MonitoringPage layout="desktop" />);
    await flush();
    expect(screen.getByText('No running sessions.')).toBeInTheDocument();
  });

  it('shows Loading, then the picture and its age that counts up', async () => {
    api.listMonitoredSessions.mockResolvedValue([makeSession('a')]);
    let resolveSnapshot: (b: Blob) => void = () => undefined;
    api.fetchSnapshot.mockImplementationOnce(() => new Promise<Blob>((resolve) => { resolveSnapshot = resolve; }));
    render(<MonitoringPage layout="desktop" />);
    await flush();
    fireEvent.click(screen.getByRole('button', { name: 'Select pns (emulator-a)' }));
    await flush();
    expect(screen.getByText('Loading')).toBeInTheDocument();
    await act(async () => { resolveSnapshot(new Blob(['png'])); });
    await flush();
    expect(screen.getByRole('img', { name: 'Live screenshot of pns (emulator-a)' })).toBeInTheDocument();
    expect(screen.getByTestId('screenshot-age')).toHaveTextContent('Updated 0 s ago');
    expect(screen.queryByText('Loading')).toBeNull();
    api.fetchSnapshot.mockReturnValue(new Promise<Blob>(() => undefined));
    await advance(1500);
    expect(screen.getByTestId('screenshot-age')).toHaveTextContent('Updated 1 s ago');
  });

  it('keeps the last picture and shows "No live picture" after a failed request', async () => {
    api.listMonitoredSessions.mockResolvedValue([makeSession('a')]);
    render(<MonitoringPage layout="desktop" />);
    await flush();
    fireEvent.click(screen.getByRole('button', { name: 'Select pns (emulator-a)' }));
    await flush();
    expect(screen.getByRole('img')).toBeInTheDocument();
    expect(screen.queryByText('No live picture')).toBeNull();
    api.fetchSnapshot.mockRejectedValue(new Error('The screenshot request failed (HTTP 504).'));
    await advance(2000);
    expect(screen.getByText('No live picture')).toBeInTheDocument();
    expect(screen.getByRole('img')).toBeInTheDocument();
    expect(screen.getByTestId('screenshot-age')).toBeInTheDocument();
  });

  it('shows the error text when no good picture exists', async () => {
    api.listMonitoredSessions.mockResolvedValue([makeSession('a')]);
    api.fetchSnapshot.mockRejectedValue(new Error('The screenshot request failed (HTTP 504).'));
    render(<MonitoringPage layout="desktop" />);
    await flush();
    fireEvent.click(screen.getByRole('button', { name: 'Select pns (emulator-a)' }));
    await flush();
    expect(screen.getByText('No live picture')).toBeInTheDocument();
    expect(screen.getByText('The screenshot request failed (HTTP 504).')).toBeInTheDocument();
    expect(screen.queryByRole('img')).toBeNull();
  });

  it('shows "Session ended" when the selected session leaves the list', async () => {
    api.listMonitoredSessions.mockResolvedValueOnce([makeSession('a'), makeSession('b')]).mockResolvedValue([makeSession('b')]);
    render(<MonitoringPage layout="desktop" />);
    await flush();
    fireEvent.click(screen.getByRole('button', { name: 'Select pns (emulator-a)' }));
    await flush();
    expect(screen.getByTestId('monitoring-detail')).toBeInTheDocument();
    await advance(5000);
    expect(screen.getByText('Session ended')).toBeInTheDocument();
    expect(screen.queryByTestId('monitoring-detail')).toBeNull();
    expect(window.location.search).not.toContain('session=');
  });

  it('requests a screenshot only for the selected session', async () => {
    api.listMonitoredSessions.mockResolvedValue([makeSession('a'), makeSession('b'), makeSession('c')]);
    render(<MonitoringPage layout="desktop" />);
    await flush();
    expect(api.fetchSnapshot).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole('button', { name: 'Select pns (emulator-b)' }));
    await flush();
    await advance(6000);
    expect(api.fetchSnapshot.mock.calls.length).toBeGreaterThan(1);
    expect(api.fetchSnapshot.mock.calls.every(([id]) => id === 'b')).toBe(true);
    expect(window.location.search).toContain('session=b');
  });
});
