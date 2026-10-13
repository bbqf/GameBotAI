import React from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { App } from '../App';
import { mockViewport, ViewportMock } from '../testUtils/viewport';
import * as queuesApi from '../services/queues';

jest.mock('../services/queues');
jest.mock('../pages/QueuesPage', () => ({ QueuesPage: () => <div>Desktop queues</div> }));

const queuesMock = queuesApi as jest.Mocked<typeof queuesApi>;

describe('SC-001: queue activity within 2 taps from the first screen', () => {
  let mock: ViewportMock;
  const originalFetch = global.fetch;

  beforeEach(() => {
    window.history.replaceState(null, '', '/');
    const queue: queuesApi.QueueDto = {
      id: 'q1', name: 'Daily', emulatorSerial: 'emulator-5558', cycleExecution: false, pauseWhenIdle: false,
      idleThresholdSeconds: 30, status: 'Running', entryCount: 1, linkedTemplateId: null, linkedGameId: null, linkedGameName: null
    };
    queuesMock.listQueues.mockResolvedValue([queue]);
    queuesMock.getQueue.mockResolvedValue({
      ...queue,
      linkedTemplateName: null,
      entries: [{ entryId: 'e1', sequenceId: 's1', sequenceName: 'Help All', stale: false }]
    });
    queuesMock.getQueueMonitor.mockResolvedValue({
      queueId: 'q1', name: 'Daily', running: true, cycleExecution: false, runStartedAt: null,
      current: {
        sequenceId: 's1', sequenceName: 'Help All', stale: false, scheduleKind: 'OncePerRun', reason: '',
        expectedAt: null, relativeLabel: 'now', repeats: false, order: 0
      },
      upcoming: [], nothingScheduled: false, lastOutcome: null
    });
    global.fetch = jest.fn().mockResolvedValue({ ok: true, status: 200 }) as unknown as typeof fetch;
  });

  afterEach(() => {
    mock?.restore();
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  it('shows the Now text after at most 2 taps', async () => {
    mock = mockViewport(360, 640);
    render(<App />);
    let taps = 0;
    const tap = (el: Element) => { taps += 1; fireEvent.click(el); };

    // The first screen is the Queues list. It already shows a summary line on the card.
    const card = await screen.findByTestId('queue-card-q1');
    await screen.findByText('Running: Help All');
    expect(card).toBeInTheDocument();

    tap(await screen.findByRole('button', { name: 'Open Daily' }));
    const now = await screen.findByTestId('queue-now');
    expect(now).toHaveTextContent('Running: Help All');
    expect(taps).toBeLessThanOrEqual(2);
  });
});
