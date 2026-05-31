import React from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { QueuesPage } from '../QueuesPage';
import { listQueues, startQueue, stopQueue } from '../../services/queues';
import { listSequences } from '../../services/sequences';

jest.mock('../../services/queues');
jest.mock('../../services/sequences');
jest.mock('../../services/useAdbDevices', () => ({
  useAdbDevices: () => ({ devices: [{ serial: 'emu-1' }], loading: false, error: undefined, refresh: () => {} }),
}));

const listQueuesMock = listQueues as jest.MockedFunction<typeof listQueues>;
const startQueueMock = startQueue as jest.MockedFunction<typeof startQueue>;
const stopQueueMock = stopQueue as jest.MockedFunction<typeof stopQueue>;
const listSequencesMock = listSequences as jest.MockedFunction<typeof listSequences>;

const queue = (over: Partial<any> = {}) => ({
  id: 'q1', name: 'Daily', emulatorSerial: 'emu-1', cycleExecution: false, status: 'Stopped', entryCount: 0, ...over,
});

beforeEach(() => {
  jest.resetAllMocks();
  listSequencesMock.mockResolvedValue([] as any);
});

describe('QueuesPage start/stop', () => {
  it('starts a stopped queue and reflects Running, disabling Edit/Delete', async () => {
    listQueuesMock.mockResolvedValueOnce([queue({ status: 'Stopped' })] as any);
    render(<QueuesPage />);
    await screen.findByText('Daily');

    startQueueMock.mockResolvedValue({} as any);
    listQueuesMock.mockResolvedValueOnce([queue({ status: 'Running' })] as any);

    fireEvent.click(screen.getByRole('button', { name: 'Start' }));

    await waitFor(() => expect(startQueueMock).toHaveBeenCalledWith('q1'));
    await waitFor(() => expect(screen.getByTestId('queue-status')).toHaveTextContent('Running'));
    expect(screen.getByRole('button', { name: 'Edit' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Delete' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Stop' })).toBeInTheDocument();
  });

  it('stops a running queue', async () => {
    listQueuesMock.mockResolvedValueOnce([queue({ status: 'Running' })] as any);
    render(<QueuesPage />);
    await screen.findByText('Daily');

    stopQueueMock.mockResolvedValue({} as any);
    listQueuesMock.mockResolvedValueOnce([queue({ status: 'Stopped' })] as any);

    fireEvent.click(screen.getByRole('button', { name: 'Stop' }));

    await waitFor(() => expect(stopQueueMock).toHaveBeenCalledWith('q1'));
    await waitFor(() => expect(screen.getByTestId('queue-status')).toHaveTextContent('Stopped'));
  });
});
