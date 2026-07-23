import React from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { QueuesPage } from '../QueuesPage';
import { listQueues, getQueue } from '../../services/queues';
import { listSequences } from '../../services/sequences';

jest.mock('../../services/queues');
jest.mock('../../services/sequences');
jest.mock('../../services/useAdbDevices', () => ({
  useAdbDevices: () => ({ devices: [{ serial: 'emu-1' }], loading: false, error: undefined, refresh: () => {} }),
}));

// Stub the monitor so US2 routing is asserted independently of US1's rendering. The stub exposes the
// running→stopped transition via a button that invokes onReturnToEditor.
jest.mock('../../components/queues/QueueMonitor', () => ({
  QueueMonitor: ({ queueId, onReturnToEditor }: { queueId: string; onReturnToEditor?: () => void }) => (
    <div data-testid="queue-monitor-stub">
      monitor:{queueId}
      <button type="button" onClick={() => onReturnToEditor?.()}>Back to editor</button>
    </div>
  ),
}));

const listQueuesMock = listQueues as jest.MockedFunction<typeof listQueues>;
const getQueueMock = getQueue as jest.MockedFunction<typeof getQueue>;
const listSequencesMock = listSequences as jest.MockedFunction<typeof listSequences>;

const row = (over: Partial<any> = {}) => ({
  id: 'q1', name: 'Daily', emulatorSerial: 'emu-1', cycleExecution: false, status: 'Stopped', entryCount: 0, ...over,
});
const detail = (over: Partial<any> = {}) => ({
  ...row(over), entries: [], linkedTemplateId: null, linkedTemplateName: null, linkedGameId: null, linkedGameName: null, ...over,
});

beforeEach(() => {
  jest.resetAllMocks();
  listSequencesMock.mockResolvedValue([] as any);
});

describe('QueuesPage monitor-vs-editor routing', () => {
  it('renders the monitor (not the editor) when the opened queue is Running', async () => {
    listQueuesMock.mockResolvedValue([row({ status: 'Running' })] as any);
    getQueueMock.mockResolvedValue(detail({ status: 'Running' }) as any);

    render(<QueuesPage />);
    await screen.findByText('Daily');

    fireEvent.click(screen.getByRole('button', { name: 'Daily' }));

    await screen.findByTestId('queue-monitor-stub');
    expect(screen.queryByText('Edit Queue')).not.toBeInTheDocument();
  });

  it('renders the editor when the opened queue is Stopped', async () => {
    listQueuesMock.mockResolvedValue([row({ status: 'Stopped' })] as any);
    getQueueMock.mockResolvedValue(detail({ status: 'Stopped' }) as any);

    render(<QueuesPage />);
    await screen.findByText('Daily');

    fireEvent.click(screen.getByRole('button', { name: 'Daily' }));

    await screen.findByText('Edit Queue');
    expect(screen.queryByTestId('queue-monitor-stub')).not.toBeInTheDocument();
  });

  it('returns to the editor when the run ends (running → stopped transition)', async () => {
    listQueuesMock.mockResolvedValue([row({ status: 'Running' })] as any);
    getQueueMock
      .mockResolvedValueOnce(detail({ status: 'Running' }) as any)
      .mockResolvedValueOnce(detail({ status: 'Stopped' }) as any);

    render(<QueuesPage />);
    await screen.findByText('Daily');

    fireEvent.click(screen.getByRole('button', { name: 'Daily' }));
    await screen.findByTestId('queue-monitor-stub');

    fireEvent.click(screen.getByRole('button', { name: 'Back to editor' }));

    await screen.findByText('Edit Queue');
    await waitFor(() => expect(screen.queryByTestId('queue-monitor-stub')).not.toBeInTheDocument());
  });
});
