import React from 'react';
import { act, fireEvent, render, screen } from '@testing-library/react';
import { App } from '../App';
import { mockViewport, ViewportMock } from '../testUtils/viewport';
import * as queuesApi from '../services/queues';
import * as templatesApi from '../services/queueTemplates';
import * as sequencesApi from '../services/sequences';

jest.mock('../services/queues');
jest.mock('../services/queueTemplates');
jest.mock('../services/sequences');
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
  NotificationsPage: () => <div>Notifications page</div>
}));
jest.mock('../pages/UpdatePage', () => ({ UPDATE_AREA_PATH: '/update', UpdatePage: () => <div>Update page</div> }));
jest.mock('../pages/Execution', () => ({
  EXECUTION_AREA_PATH: '/execution',
  EXECUTION_LOGS_AREA_PATH: '/execution-logs',
  ExecutionPage: () => <div>Execution page</div>
}));
jest.mock('../pages/ExecutionLogs', () => ({ ExecutionLogsPage: () => <div>Logs page</div> }));

const queuesMock = queuesApi as jest.Mocked<typeof queuesApi>;
const templatesMock = templatesApi as jest.Mocked<typeof templatesApi>;
const sequencesMock = sequencesApi as jest.Mocked<typeof sequencesApi>;

const queueDto = (): queuesApi.QueueDto => ({
  id: 'q1', name: 'Daily', emulatorSerial: 'emulator-5558', cycleExecution: false, pauseWhenIdle: false,
  idleThresholdSeconds: 30, status: 'Stopped', entryCount: 1, linkedTemplateId: 't1', linkedGameId: null, linkedGameName: null
});

const goBack = async () => {
  await act(async () => {
    window.history.back();
    await new Promise((resolve) => setTimeout(resolve, 30));
  });
};

const originalFetch = global.fetch;
let mock: ViewportMock;

beforeEach(() => {
  jest.resetAllMocks();
  window.history.replaceState(null, '', '/');
  queuesMock.listQueues.mockResolvedValue([queueDto()]);
  queuesMock.getQueue.mockResolvedValue({
    ...queueDto(), linkedTemplateName: 'Daily tpl',
    entries: [{ entryId: 'e1', sequenceId: 's1', sequenceName: 'Help All', stale: false }]
  });
  queuesMock.getQueueMonitor.mockResolvedValue({
    queueId: 'q1', name: 'Daily', running: false, cycleExecution: false, runStartedAt: null,
    current: null, upcoming: [], nothingScheduled: false, lastOutcome: null
  });
  templatesMock.getQueueTemplate.mockResolvedValue({
    id: 't1', name: 'Daily tpl', entryCount: 1, createdAt: null, updatedAt: null,
    entries: [{
      entryId: 'e1', sequenceId: 's1', sequenceName: 'Help All', stale: false, scheduleType: 'OncePerRun',
      timerTimeOfDay: null, timerRelativeOffset: null, enabled: true, parameterValues: []
    }]
  });
  sequencesMock.getSequence.mockResolvedValue({ id: 's1', name: 'Help All', steps: [], parameters: [] } as unknown as sequencesApi.SequenceDto);
  global.fetch = jest.fn().mockResolvedValue({ ok: true, status: 200 }) as unknown as typeof fetch;
});

afterEach(() => {
  mock?.restore();
  global.fetch = originalFetch;
});

const openDetail = async () => {
  fireEvent.click(await screen.findByRole('button', { name: 'Open Daily' }));
  await screen.findByTestId('phone-queue-detail');
};

describe('Phone entry page navigation (feature 185, rules P-01, E-11, M-01, M-02)', () => {
  it('adds one history entry for a tap on a row, and back shows the queue detail again', async () => {
    mock = mockViewport(360, 640);
    render(<App />);
    await screen.findByTestId('phone-queues');
    await openDetail();
    const row = await screen.findByRole('button', { name: 'Entry settings for Help All' });
    const length = window.history.length;

    fireEvent.click(row);

    expect(await screen.findByTestId('phone-entry-page')).toBeInTheDocument();
    expect(window.history.length).toBe(length + 1);
    expect(window.location.search).toContain('queue=q1');
    expect(window.location.search).toContain('entry=e1');

    await goBack();
    expect(await screen.findByTestId('phone-queue-detail')).toBeInTheDocument();
    expect(screen.queryByTestId('phone-entry-page')).toBeNull();
    expect(window.location.search).not.toContain('entry=');
  });

  it('goes forward to the entry page again', async () => {
    mock = mockViewport(360, 640);
    render(<App />);
    await screen.findByTestId('phone-queues');
    await openDetail();
    fireEvent.click(await screen.findByRole('button', { name: 'Entry settings for Help All' }));
    await screen.findByTestId('phone-entry-page');
    await goBack();
    await screen.findByTestId('phone-queue-detail');

    await act(async () => {
      window.history.forward();
      await new Promise((resolve) => setTimeout(resolve, 30));
    });

    expect(await screen.findByTestId('phone-entry-page')).toBeInTheDocument();
  });

  it('uses history.back for the on-screen Back when the page is not the first entry', async () => {
    mock = mockViewport(360, 640);
    render(<App />);
    await screen.findByTestId('phone-queues');
    await openDetail();
    fireEvent.click(await screen.findByRole('button', { name: 'Entry settings for Help All' }));
    await screen.findByTestId('phone-entry-page');
    const back = jest.spyOn(window.history, 'back');

    fireEvent.click(screen.getByRole('button', { name: /Back/ }));

    expect(back).toHaveBeenCalled();
    back.mockRestore();
    await goBack();
  });

  it('shows the page after a reload of the address', async () => {
    window.history.replaceState(null, '', '/?area=queues&queue=q1&entry=e1');
    mock = mockViewport(360, 640);
    render(<App />);

    expect(await screen.findByTestId('phone-entry-page')).toBeInTheDocument();
    expect(await screen.findByRole('heading', { name: 'Help All' })).toBeInTheDocument();
  });

  it('shows the queue detail when the on-screen Back runs on the first entry', async () => {
    window.history.replaceState(null, '', '/?area=queues&queue=q1&entry=e1');
    mock = mockViewport(360, 640);
    render(<App />);
    await screen.findByRole('heading', { name: 'Help All' });
    const length = window.history.length;

    fireEvent.click(screen.getByRole('button', { name: /Back/ }));

    expect(await screen.findByTestId('phone-queue-detail')).toBeInTheDocument();
    expect(window.history.length).toBe(length);
    expect(window.location.search).not.toContain('entry=');
  });

  it('shows Entry not found for an address of an entry that is not in the template', async () => {
    window.history.replaceState(null, '', '/?area=queues&queue=q1&entry=zzz');
    mock = mockViewport(360, 640);
    render(<App />);

    expect(await screen.findByText('Entry not found')).toBeInTheDocument();
  });

  it('does not mount the entry page in desktop mode, and the desktop page shows instead (M-01, M-02)', async () => {
    window.history.replaceState(null, '', '/?area=queues&queue=q1&entry=e1');
    mock = mockViewport(1280, 800);
    render(<App />);

    expect(await screen.findByTestId('desktop-queues-page')).toBeInTheDocument();
    expect(screen.queryByTestId('phone-entry-page')).toBeNull();
    expect(window.location.search).not.toContain('entry=');
  });

  describe('with unsaved edits (rule E-08, the leave guard)', () => {
    const openAndType = async () => {
      mock = mockViewport(360, 640);
      render(<App />);
      await screen.findByTestId('phone-queues');
      await openDetail();
      templatesMock.getQueueTemplate.mockResolvedValue({
        id: 't1', name: 'Daily tpl', entryCount: 1, createdAt: null, updatedAt: null,
        entries: [{
          entryId: 'e1', sequenceId: 's1', sequenceName: 'Help All', stale: false, scheduleType: 'OncePerRun',
          timerTimeOfDay: null, timerRelativeOffset: null, enabled: true, parameterValues: []
        }]
      });
      sequencesMock.getSequence.mockResolvedValue({
        id: 's1', name: 'Help All', steps: [],
        parameters: [{ name: 'count', type: 'number', default: '3', required: false }]
      } as unknown as sequencesApi.SequenceDto);
      fireEvent.click(await screen.findByRole('button', { name: 'Entry settings for Help All' }));
      await screen.findByLabelText('Parameter values');
      fireEvent.click(screen.getByLabelText('Use default count'));
      fireEvent.change(screen.getByLabelText('Value for count'), { target: { value: '8' } });
    };

    it('keeps the page and asks the operator when the phone back step runs', async () => {
      await openAndType();
      const length = window.history.length;

      await goBack();

      expect(screen.getByTestId('phone-entry-page')).toBeInTheDocument();
      expect(await screen.findByRole('alertdialog', { name: 'Unsaved changes' })).toBeInTheDocument();
      expect(window.location.search).toContain('entry=e1');
      expect(window.history.length).toBe(length);
      expect(screen.getByLabelText('Value for count')).toHaveValue('8');
    });

    it('does not loop: one back step gives one dialog and the page stays', async () => {
      await openAndType();

      await goBack();
      await act(async () => { await new Promise((resolve) => setTimeout(resolve, 60)); });

      expect(screen.getAllByRole('alertdialog')).toHaveLength(1);
      expect(screen.getByTestId('phone-entry-page')).toBeInTheDocument();
    });

    it('leaves to the queue detail after Discard changes', async () => {
      await openAndType();
      await goBack();
      fireEvent.click(await screen.findByRole('button', { name: 'Discard changes' }));
      await act(async () => { await new Promise((resolve) => setTimeout(resolve, 60)); });

      expect(await screen.findByTestId('phone-queue-detail')).toBeInTheDocument();
      expect(screen.queryByTestId('phone-entry-page')).toBeNull();
    });

    it('stays on the page after Keep editing', async () => {
      await openAndType();
      await goBack();

      fireEvent.click(await screen.findByRole('button', { name: 'Keep editing' }));

      expect(screen.queryByRole('alertdialog')).toBeNull();
      expect(screen.getByLabelText('Value for count')).toHaveValue('8');
    });

    it('asks the operator for a tab bar move too, and stays on the page', async () => {
      await openAndType();

      fireEvent.click(screen.getByRole('button', { name: 'Monitoring' }));

      expect(await screen.findByRole('alertdialog', { name: 'Unsaved changes' })).toBeInTheDocument();
      expect(screen.queryByTestId('monitoring-page')).toBeNull();
    });
  });

  it('removes the entry page when the window changes to the desktop layout', async () => {
    mock = mockViewport(360, 640);
    render(<App />);
    await screen.findByTestId('phone-queues');
    await openDetail();
    fireEvent.click(await screen.findByRole('button', { name: 'Entry settings for Help All' }));
    await screen.findByTestId('phone-entry-page');

    act(() => mock.setViewport(1280, 800));

    expect(screen.getByTestId('desktop-queues-page')).toBeInTheDocument();
    expect(screen.queryByTestId('phone-entry-page')).toBeNull();
  });
});
