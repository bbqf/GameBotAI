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
  id: 'q1', name: 'Daily', emulatorSerial: 'emu-1', cycleExecution: false, pauseWhenIdle: false, idleThresholdSeconds: 30, status: 'Stopped', entryCount: 0, ...over,
});

beforeEach(() => {
  jest.resetAllMocks();
  listQueuesMock.mockResolvedValue([] as any);
  listSequencesMock.mockResolvedValue([] as any);
});

describe('QueuesPage CRUD', () => {
  it('lists queues with their status and activity, and no emulator', async () => {
    listQueuesMock.mockResolvedValue([queue()] as any);
    render(<QueuesPage />);

    await screen.findByText('Daily');
    expect(screen.queryByText('emu-1')).not.toBeInTheDocument();
    expect(screen.getByTestId('queue-status')).toHaveTextContent('Stopped');
    expect(screen.getByTestId('queue-activity')).toHaveTextContent('Stopped');
  });

  it('has no Schedule button on a running row and opens no live-schedule form (feature 173)', async () => {
    listQueuesMock.mockResolvedValue([queue({ status: 'Running' })] as any);
    render(<QueuesPage />);

    await screen.findByText('Daily');
    expect(screen.queryByRole('button', { name: /Schedule/i })).not.toBeInTheDocument();
    expect(screen.queryByText('Schedule')).not.toBeInTheDocument();
    expect(document.querySelector('.queues-live-schedule-row')).toBeNull();
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
    await waitFor(() => expect(createQueueMock).toHaveBeenCalledWith({ name: 'Daily', emulatorSerial: 'emu-1', cycleExecution: false, pauseWhenIdle: false, idleThresholdSeconds: 30, emulatorInstanceName: null, emulatorInstanceIndex: null, resumeOnServiceStart: false }));
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

    await waitFor(() => expect(updateQueueMock).toHaveBeenCalledWith('q1', { name: 'Daily 2', cycleExecution: false, pauseWhenIdle: false, idleThresholdSeconds: 30, emulatorInstanceName: null, emulatorInstanceIndex: null, resumeOnServiceStart: false }));
  });

  it('deletes a queue after confirmation', async () => {
    listQueuesMock.mockResolvedValue([queue()] as any);
    getQueueMock.mockResolvedValue({ ...queue(), entries: [] } as any);
    deleteQueueMock.mockResolvedValue(undefined as any);

    render(<QueuesPage />);
    await screen.findByText('Daily');
    fireEvent.click(screen.getByRole('button', { name: 'Delete Daily' }));

    const dialog = await screen.findByRole('dialog');
    fireEvent.click(within(dialog).getByRole('button', { name: 'Delete' }));

    await waitFor(() => expect(deleteQueueMock).toHaveBeenCalledWith('q1'));
  });
});
