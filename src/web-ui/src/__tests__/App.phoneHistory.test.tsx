import React from 'react';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { App } from '../App';
import { mockViewport, ViewportMock } from '../testUtils/viewport';
import * as queuesApi from '../services/queues';

jest.mock('../services/queues');
jest.mock('../pages/Configuration', () => ({
  CONFIGURATION_AREA_PATH: '/configuration',
  ConfigurationPage: () => <input aria-label="mock token field" />
}));
jest.mock('../pages/CommandsPage', () => ({ CommandsPage: () => <div>Commands page</div> }));
jest.mock('../pages/QueuesPage', () => ({ QueuesPage: () => <div data-testid="desktop-queues-page">Desktop queues</div> }));
jest.mock('../pages/MonitoringPage', () => ({
  MONITORING_AREA_PATH: '/monitoring',
  MonitoringPage: () => <div data-testid="monitoring-page">Monitoring page</div>
}));
jest.mock('../pages/NotificationsPage', () => ({
  NOTIFICATIONS_AREA_PATH: '/notifications',
  NotificationsPage: () => <div data-testid="notifications-page">Notifications page</div>
}));
jest.mock('../pages/UpdatePage', () => ({
  UPDATE_AREA_PATH: '/update',
  UpdatePage: () => <div data-testid="update-page">Update page</div>
}));
jest.mock('../pages/Execution', () => ({
  EXECUTION_AREA_PATH: '/execution',
  EXECUTION_LOGS_AREA_PATH: '/execution-logs',
  ExecutionPage: () => <div data-testid="execution-page">Execution page</div>
}));
jest.mock('../pages/ExecutionLogs', () => ({ ExecutionLogsPage: () => <div>Logs page</div> }));

const queuesMock = queuesApi as jest.Mocked<typeof queuesApi>;

const queueDto = (): queuesApi.QueueDto => ({
  id: 'q1', name: 'Daily', emulatorSerial: 'emulator-5558', cycleExecution: false, pauseWhenIdle: false,
  idleThresholdSeconds: 30, status: 'Stopped', entryCount: 0, linkedTemplateId: null, linkedGameId: null, linkedGameName: null
});

const goBack = async () => {
  await act(async () => {
    window.history.back();
    await new Promise((resolve) => setTimeout(resolve, 30));
  });
};

const goForward = async () => {
  await act(async () => {
    window.history.forward();
    await new Promise((resolve) => setTimeout(resolve, 30));
  });
};

const originalFetch = global.fetch;
let mock: ViewportMock;

const setup = () => {
  window.history.replaceState(null, '', '/');
  queuesMock.listQueues.mockResolvedValue([queueDto()]);
  queuesMock.getQueue.mockResolvedValue({ ...queueDto(), entries: [], linkedTemplateName: null });
  queuesMock.getQueueMonitor.mockResolvedValue({
    queueId: 'q1', name: 'Daily', running: false, cycleExecution: false, runStartedAt: null,
    current: null, upcoming: [], nothingScheduled: false, lastOutcome: null
  });
  global.fetch = jest.fn().mockResolvedValue({ ok: true, status: 200 }) as unknown as typeof fetch;
};

const teardown = () => {
  mock?.restore();
  global.fetch = originalFetch;
  jest.clearAllMocks();
};

const openDaily = async () => {
  fireEvent.click(await screen.findByRole('button', { name: 'Open Daily' }));
  await screen.findByTestId('phone-queue-detail');
};

describe('App phone history', () => {
  beforeEach(setup);
  afterEach(teardown);

  it('walks back through queue detail, list, and first screen (SC-003)', async () => {
    mock = mockViewport(360, 640);
    render(<App />);
    await screen.findByTestId('phone-queues');
    expect(window.history.state.idx).toBe(0);
    await openDaily();
    expect(window.location.search).toContain('queue=q1');
    fireEvent.click(screen.getByRole('button', { name: 'Monitoring' }));
    expect(await screen.findByTestId('monitoring-page')).toBeInTheDocument();
    expect(window.history.state.idx).toBe(2);

    await goBack();
    expect(await screen.findByTestId('phone-queue-detail')).toBeInTheDocument();
    await goBack();
    expect(await screen.findByTestId('phone-queues')).toBeInTheDocument();
    expect(screen.queryByTestId('phone-queue-detail')).toBeNull();
    await goBack();
    // The test browser cannot leave the page. The first-screen marker (idx 0) is the accepted proxy:
    // the real browser leaves the application from this entry. The real-phone check confirms it.
    expect(window.history.state.idx).toBe(0);
    expect(screen.getByTestId('phone-queues')).toBeInTheDocument();
  });

  it('walks back on a second path: Configuration, then Update', async () => {
    mock = mockViewport(360, 640);
    render(<App />);
    await screen.findByTestId('phone-queues');
    fireEvent.click(screen.getByRole('button', { name: /More/ }));
    fireEvent.click(screen.getByRole('button', { name: 'Configuration' }));
    await screen.findByLabelText('mock token field');
    fireEvent.click(screen.getByRole('button', { name: /More/ }));
    fireEvent.click(screen.getByRole('button', { name: 'Update' }));
    await screen.findByTestId('update-page');
    await goBack();
    expect(await screen.findByLabelText('mock token field')).toBeInTheDocument();
    await goBack();
    expect(await screen.findByTestId('phone-queues')).toBeInTheDocument();
    expect(window.history.state.idx).toBe(0);
  });

  it('goes forward again', async () => {
    mock = mockViewport(360, 640);
    render(<App />);
    await screen.findByTestId('phone-queues');
    await openDaily();
    await goBack();
    expect(await screen.findByTestId('phone-queues')).toBeInTheDocument();
    await goForward();
    expect(await screen.findByTestId('phone-queue-detail')).toBeInTheDocument();
    expect(window.location.search).toContain('queue=q1');
  });

  it('adds one entry for a tab bar move and none for the same screen', async () => {
    mock = mockViewport(360, 640);
    render(<App />);
    await screen.findByTestId('phone-queues');
    const length = window.history.length;
    fireEvent.click(screen.getByRole('button', { name: 'Monitoring' }));
    await screen.findByTestId('monitoring-page');
    expect(window.history.length).toBe(length + 1);
    fireEvent.click(screen.getByRole('button', { name: 'Monitoring' }));
    expect(window.history.length).toBe(length + 1);
  });

  it('opens a direct link to a queue detail (SC-004)', async () => {
    window.history.replaceState(null, '', '/?area=queues&queue=q1');
    mock = mockViewport(360, 640);
    render(<App />);
    expect(await screen.findByTestId('phone-queue-detail')).toBeInTheDocument();
    expect(await screen.findByRole('heading', { name: 'Daily' })).toBeInTheDocument();
  });

  it.each([
    ['queues', 'phone-queues'],
    ['monitoring', 'monitoring-page'],
    ['update', 'update-page']
  ])('opens a direct link to %s', async (area, testId) => {
    window.history.replaceState(null, '', `/?area=${area}`);
    mock = mockViewport(360, 640);
    render(<App />);
    expect(await screen.findByTestId(testId)).toBeInTheDocument();
  });

  it('uses history.back for the on-screen back control when the entry is not the first', async () => {
    mock = mockViewport(360, 640);
    render(<App />);
    await screen.findByTestId('phone-queues');
    await openDaily();
    const back = jest.spyOn(window.history, 'back');
    fireEvent.click(screen.getByRole('button', { name: /Back/ }));
    expect(back).toHaveBeenCalled();
    back.mockRestore();
    await goBack();
  });

  it('replaces the entry with the list when the detail is the first entry', async () => {
    window.history.replaceState(null, '', '/?area=queues&queue=q1');
    mock = mockViewport(360, 640);
    render(<App />);
    await screen.findByTestId('phone-queue-detail');
    const length = window.history.length;
    const back = jest.spyOn(window.history, 'back');
    fireEvent.click(screen.getByRole('button', { name: /Back/ }));
    expect(back).not.toHaveBeenCalled();
    expect(await screen.findByTestId('phone-queues')).toBeInTheDocument();
    expect(window.history.length).toBe(length);
    expect(window.location.search).not.toContain('queue=');
    back.mockRestore();
  });

  it('keeps idx after a reload', async () => {
    mock = mockViewport(360, 640);
    const first = render(<App />);
    await screen.findByTestId('phone-queues');
    await openDaily();
    expect(window.history.state.idx).toBe(1);
    first.unmount();
    render(<App />);
    expect(await screen.findByTestId('phone-queue-detail')).toBeInTheDocument();
    expect(window.history.state.idx).toBe(1);
  });

  it('keeps the screen and the history when the layout changes (FR-013)', async () => {
    mock = mockViewport(360, 640);
    render(<App />);
    await screen.findByTestId('phone-queues');
    await openDaily();
    const length = window.history.length;
    act(() => mock.setViewport(1280, 800));
    expect(window.history.length).toBe(length);
    expect(window.location.search).toContain('area=queues');
    expect(window.location.search).toContain('queue=q1');
    act(() => mock.setViewport(360, 640));
    expect(await screen.findByTestId('phone-queue-detail')).toBeInTheDocument();
    expect(window.history.length).toBe(length);
    expect(window.location.search).toContain('queue=q1');
    await goBack();
    expect(await screen.findByTestId('phone-queues')).toBeInTheDocument();
  });
});

describe('App phone Execution redirect', () => {
  beforeEach(setup);
  afterEach(teardown);

  it('shows Queues for ?area=execution and replaces the entry', async () => {
    window.history.replaceState(null, '', '/?area=execution');
    const length = window.history.length;
    mock = mockViewport(360, 640);
    render(<App />);
    expect(await screen.findByTestId('phone-queues')).toBeInTheDocument();
    expect(screen.queryByTestId('execution-page')).toBeNull();
    expect(window.location.search).toContain('area=queues');
    expect(window.history.length).toBe(length);
  });

  it('shows Queues for the /execution path', async () => {
    window.history.replaceState(null, '', '/execution');
    mock = mockViewport(360, 640);
    render(<App />);
    expect(await screen.findByTestId('phone-queues')).toBeInTheDocument();
    expect(window.location.search).toContain('area=queues');
  });

  it('does not return to Execution on back', async () => {
    window.history.replaceState(null, '', '/?area=execution');
    mock = mockViewport(360, 640);
    render(<App />);
    await screen.findByTestId('phone-queues');
    fireEvent.click(screen.getByRole('button', { name: 'Monitoring' }));
    await screen.findByTestId('monitoring-page');
    await goBack();
    expect(await screen.findByTestId('phone-queues')).toBeInTheDocument();
    expect(screen.queryByTestId('execution-page')).toBeNull();
    expect(window.location.search).toContain('area=queues');
  });

  it('does not redirect /execution-logs', async () => {
    window.history.replaceState(null, '', '/execution-logs');
    mock = mockViewport(360, 640);
    render(<App />);
    expect(await screen.findByRole('heading', { name: 'Execution Logs', level: 2 })).toBeInTheDocument();
    expect(window.location.search).toContain('area=execution-logs');
  });

  it('keeps Queues after a change to the desktop layout, and the desktop still has Execution', async () => {
    window.history.replaceState(null, '', '/?area=execution');
    mock = mockViewport(360, 640);
    render(<App />);
    await screen.findByTestId('phone-queues');
    act(() => mock.setViewport(1280, 800));
    expect(screen.getByTestId('desktop-queues-page')).toBeInTheDocument();
    expect(window.location.search).toContain('area=queues');
    fireEvent.click(screen.getByRole('tab', { name: 'Execution' }));
    await waitFor(() => expect(screen.getByTestId('execution-page')).toBeInTheDocument());
  });
});
