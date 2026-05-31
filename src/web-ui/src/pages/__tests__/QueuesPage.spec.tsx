import React from 'react';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { QueuesPage } from '../QueuesPage';
import {
  listQueues, getQueue, createQueue, updateQueue, deleteQueue,
} from '../../services/queues';
import { listSequences } from '../../services/sequences';

jest.mock('../../services/queues');
jest.mock('../../services/sequences');
jest.mock('../../services/useAdbDevices', () => ({
  useAdbDevices: () => ({ devices: [{ serial: 'emu-1' }], loading: false, error: undefined, refresh: () => {} }),
}));

const listQueuesMock = listQueues as jest.MockedFunction<typeof listQueues>;
const getQueueMock = getQueue as jest.MockedFunction<typeof getQueue>;
const createQueueMock = createQueue as jest.MockedFunction<typeof createQueue>;
const updateQueueMock = updateQueue as jest.MockedFunction<typeof updateQueue>;
const deleteQueueMock = deleteQueue as jest.MockedFunction<typeof deleteQueue>;
const listSequencesMock = listSequences as jest.MockedFunction<typeof listSequences>;

const queue = (over: Partial<any> = {}) => ({
  id: 'q1', name: 'Daily', emulatorSerial: 'emu-1', cycleExecution: false, status: 'Stopped', entryCount: 0, ...over,
});

beforeEach(() => {
  jest.resetAllMocks();
  listQueuesMock.mockResolvedValue([] as any);
  listSequencesMock.mockResolvedValue([] as any);
});

describe('QueuesPage CRUD', () => {
  it('lists queues with their emulator and status', async () => {
    listQueuesMock.mockResolvedValue([queue()] as any);
    render(<QueuesPage />);

    await screen.findByText('Daily');
    expect(screen.getByText('emu-1')).toBeInTheDocument();
    expect(screen.getByTestId('queue-status')).toHaveTextContent('Stopped');
  });

  it('validates required fields and creates a queue', async () => {
    render(<QueuesPage />);
    await waitFor(() => expect(listQueuesMock).toHaveBeenCalled());

    fireEvent.click(screen.getByRole('button', { name: 'Create Queue' }));
    fireEvent.click(screen.getByText('Save'));
    expect(await screen.findByText('Name is required')).toBeInTheDocument();
    expect(createQueueMock).not.toHaveBeenCalled();

    fireEvent.change(screen.getByLabelText('Name *'), { target: { value: 'Daily' } });
    fireEvent.change(screen.getByLabelText('Emulator *'), { target: { value: 'emu-1' } });
    createQueueMock.mockResolvedValue({} as any);

    fireEvent.click(screen.getByText('Save'));
    await waitFor(() => expect(createQueueMock).toHaveBeenCalledWith({ name: 'Daily', emulatorSerial: 'emu-1', cycleExecution: false }));
  });

  it('opens edit and updates a queue', async () => {
    listQueuesMock.mockResolvedValue([queue()] as any);
    getQueueMock.mockResolvedValue({ ...queue(), entries: [] } as any);
    updateQueueMock.mockResolvedValue({} as any);

    render(<QueuesPage />);
    await screen.findByText('Daily');
    fireEvent.click(screen.getByText('Daily'));

    await screen.findByText('Edit Queue');
    fireEvent.change(screen.getByLabelText('Name *'), { target: { value: 'Daily 2' } });
    fireEvent.click(screen.getByText('Save'));

    await waitFor(() => expect(updateQueueMock).toHaveBeenCalledWith('q1', { name: 'Daily 2', cycleExecution: false }));
  });

  it('deletes a queue after confirmation', async () => {
    listQueuesMock.mockResolvedValue([queue()] as any);
    getQueueMock.mockResolvedValue({ ...queue(), entries: [] } as any);
    deleteQueueMock.mockResolvedValue(undefined as any);

    render(<QueuesPage />);
    await screen.findByText('Daily');
    fireEvent.click(screen.getByRole('button', { name: 'Delete' }));

    const dialog = await screen.findByRole('dialog');
    fireEvent.click(within(dialog).getByRole('button', { name: 'Delete' }));

    await waitFor(() => expect(deleteQueueMock).toHaveBeenCalledWith('q1'));
  });
});
