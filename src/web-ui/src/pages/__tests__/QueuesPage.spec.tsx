import React from 'react';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { QueuesPage } from '../QueuesPage';
import {
  listQueues, getQueue, createQueue, updateQueue, deleteQueue, duplicateQueue,
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
const duplicateQueueMock = duplicateQueue as jest.MockedFunction<typeof duplicateQueue>;
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
    await waitFor(() => expect(createQueueMock).toHaveBeenCalledWith({ name: 'Daily', emulatorSerial: 'emu-1', cycleExecution: false, pauseWhenIdle: false, idleThresholdSeconds: 30, emulatorInstanceName: null, emulatorInstanceIndex: null, resumeOnServiceStart: false, smartScheduling: false, smartSchedulingWindowMinutes: 15 }));
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

    await waitFor(() => expect(updateQueueMock).toHaveBeenCalledWith('q1', { name: 'Daily 2', cycleExecution: false, pauseWhenIdle: false, idleThresholdSeconds: 30, emulatorInstanceName: null, emulatorInstanceIndex: null, resumeOnServiceStart: false, smartScheduling: false, smartSchedulingWindowMinutes: 15 }));
  });

  it('sends the smart scheduling settings when it saves an edited queue (feature 189)', async () => {
    listQueuesMock.mockResolvedValue([queue({ smartScheduling: true, smartSchedulingWindowMinutes: 20 })] as any);
    getQueueMock.mockResolvedValue({ ...queue({ smartScheduling: true, smartSchedulingWindowMinutes: 20 }), entries: [] } as any);
    updateQueueMock.mockResolvedValue({} as any);

    render(<QueuesPage />);
    await screen.findByText('Daily');
    fireEvent.click(screen.getByText('Daily'));

    await screen.findByText('Edit Queue');
    fireEvent.change(screen.getByLabelText('Window (minutes)'), { target: { value: '30' } });
    fireEvent.click(screen.getByText('Save'));

    await waitFor(() => expect(updateQueueMock).toHaveBeenCalledWith('q1', expect.objectContaining({ smartScheduling: true, smartSchedulingWindowMinutes: 30 })));
  });

  it('does not save a queue with a window out of range (feature 189)', async () => {
    listQueuesMock.mockResolvedValue([queue({ smartScheduling: true })] as any);
    getQueueMock.mockResolvedValue({ ...queue({ smartScheduling: true }), entries: [] } as any);

    render(<QueuesPage />);
    await screen.findByText('Daily');
    fireEvent.click(screen.getByText('Daily'));

    await screen.findByText('Edit Queue');
    fireEvent.change(screen.getByLabelText('Window (minutes)'), { target: { value: '0' } });
    fireEvent.click(screen.getByText('Save'));

    expect(await screen.findByText('Enter a value from 1 to 120.')).toBeInTheDocument();
    expect(updateQueueMock).not.toHaveBeenCalled();
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

// Feature 199: the device recovery option in the queue form.
describe('QueuesPage device recovery (feature 199)', () => {
  const reboot = { action: 'reboot-instance', afterMs: 120000, maxAttempts: 3, cooldownMs: 30000 };

  const openEdit = async (over: Partial<any> = {}) => {
    listQueuesMock.mockResolvedValue([queue(over)] as any);
    getQueueMock.mockResolvedValue({ ...queue(over), entries: [] } as any);
    updateQueueMock.mockResolvedValue({} as any);
    render(<QueuesPage />);
    await screen.findByText('Daily');
    fireEvent.click(screen.getByText('Daily'));
    await screen.findByText('Edit Queue');
  };

  it('shows the stored values of a queue with reboot-instance', async () => {
    await openEdit({ emulatorInstanceName: 'PNS', deviceRecovery: reboot });

    expect(screen.getByLabelText('Device recovery')).toBeChecked();
    expect(screen.getByLabelText('Recovery wait minutes')).toHaveValue(2);
    expect(screen.getByLabelText('Recovery maximum attempts')).toHaveValue(3);
    expect(screen.getByLabelText('Recovery time between attempts minutes')).toHaveValue(0.5);
  });

  it('turns the option on and saves reboot-instance with the default wait of 5 minutes', async () => {
    await openEdit({ emulatorInstanceName: 'PNS' });

    fireEvent.click(screen.getByLabelText('Device recovery'));
    fireEvent.click(screen.getByText('Save'));

    await waitFor(() => expect(updateQueueMock).toHaveBeenCalledWith('q1', expect.objectContaining({
      deviceRecovery: { action: 'reboot-instance', afterMs: 300000, maxAttempts: 2, cooldownMs: 180000 }
    })));
  });

  it('sends the wait that the operator typed in minutes', async () => {
    await openEdit({ emulatorInstanceName: 'PNS' });

    fireEvent.click(screen.getByLabelText('Device recovery'));
    fireEvent.change(screen.getByLabelText('Recovery wait minutes'), { target: { value: '3' } });
    fireEvent.click(screen.getByText('Save'));

    await waitFor(() => expect(updateQueueMock).toHaveBeenCalledWith('q1', expect.objectContaining({
      deviceRecovery: expect.objectContaining({ action: 'reboot-instance', afterMs: 180000 })
    })));
  });

  it('turns the option off and saves the action none', async () => {
    await openEdit({ emulatorInstanceName: 'PNS', deviceRecovery: reboot });

    fireEvent.click(screen.getByLabelText('Device recovery'));
    fireEvent.click(screen.getByText('Save'));

    await waitFor(() => expect(updateQueueMock).toHaveBeenCalledWith('q1', expect.objectContaining({
      deviceRecovery: { action: 'none', afterMs: 120000, maxAttempts: 3, cooldownMs: 30000 }
    })));
  });

  it('sends no deviceRecovery key for a queue with no recovery and no change', async () => {
    await openEdit();

    fireEvent.click(screen.getByText('Save'));

    await waitFor(() => expect(updateQueueMock).toHaveBeenCalled());
    expect(updateQueueMock.mock.calls[0][1]).not.toHaveProperty('deviceRecovery');
  });

  it('sends the saved settings in the next save of the same form', async () => {
    await openEdit({ emulatorInstanceName: 'PNS' });
    updateQueueMock.mockResolvedValueOnce({ deviceRecovery: { action: 'reboot-instance', afterMs: 300000, maxAttempts: 2, cooldownMs: 180000 } } as any);

    fireEvent.click(screen.getByLabelText('Device recovery'));
    fireEvent.click(screen.getByText('Save'));
    await waitFor(() => expect(updateQueueMock).toHaveBeenCalledTimes(1));
    await screen.findByText('Queue updated successfully.');
    fireEvent.click(screen.getByLabelText('Device recovery'));
    fireEvent.click(screen.getByText('Save'));

    await waitFor(() => expect(updateQueueMock).toHaveBeenCalledTimes(2));
    expect(updateQueueMock.mock.calls[1][1]).toEqual(expect.objectContaining({ deviceRecovery: expect.objectContaining({ action: 'none' }) }));
  });

  it('shows the message of a 400 response', async () => {
    await openEdit({ emulatorInstanceName: 'PNS' });
    updateQueueMock.mockRejectedValueOnce(new Error('deviceRecovery.afterMs must be at least 60000 (was: 10)'));

    fireEvent.click(screen.getByLabelText('Device recovery'));
    fireEvent.click(screen.getByText('Save'));

    expect(await screen.findByText('deviceRecovery.afterMs must be at least 60000 (was: 10)')).toBeInTheDocument();
  });

  it('stops the save and shows the message when the instance name is empty', async () => {
    await openEdit();

    fireEvent.click(screen.getByLabelText('Device recovery'));
    fireEvent.click(screen.getByText('Save'));

    expect(await screen.findByText('Device recovery needs an emulator instance name.')).toBeInTheDocument();
    expect(updateQueueMock).not.toHaveBeenCalled();
  });

  it('stops the save for a wait below 1 minute', async () => {
    await openEdit({ emulatorInstanceName: 'PNS', deviceRecovery: reboot });

    fireEvent.change(screen.getByLabelText('Recovery wait minutes'), { target: { value: '0.5' } });
    fireEvent.click(screen.getByText('Save'));

    expect(await screen.findByText('Enter 1 minute or more.')).toBeInTheDocument();
    expect(screen.getByLabelText('Recovery wait minutes')).toHaveAttribute('aria-invalid', 'true');
    expect(updateQueueMock).not.toHaveBeenCalled();
  });

  it.each(['6', '1.5'])('stops the save for %s attempts', async (attempts) => {
    await openEdit({ emulatorInstanceName: 'PNS', deviceRecovery: reboot });

    fireEvent.change(screen.getByLabelText('Recovery maximum attempts'), { target: { value: attempts } });
    fireEvent.click(screen.getByText('Save'));

    expect(await screen.findByText('Enter a whole number from 1 to 5.')).toBeInTheDocument();
    expect(updateQueueMock).not.toHaveBeenCalled();
  });

  it('stops the save for a negative time between attempts', async () => {
    await openEdit({ emulatorInstanceName: 'PNS', deviceRecovery: reboot });

    fireEvent.change(screen.getByLabelText('Recovery time between attempts minutes'), { target: { value: '-1' } });
    fireEvent.click(screen.getByText('Save'));

    expect(await screen.findByText('Enter 0 minutes or more.')).toBeInTheDocument();
    expect(updateQueueMock).not.toHaveBeenCalled();
  });

  it('shows no device state line in the edit view of a stopped queue', async () => {
    await openEdit({ emulatorInstanceName: 'PNS', deviceRecovery: reboot });

    expect(screen.queryByText(/^Device:/)).toBeNull();
  });

  it('shows the recovery settings of a duplicate in its edit form (FR-010)', async () => {
    const source = queue({ emulatorInstanceName: 'PNS', deviceRecovery: reboot });
    const copy = queue({ id: 'q2', name: 'Daily (copy)', emulatorInstanceName: 'PNS-2', deviceRecovery: reboot });
    listQueuesMock.mockResolvedValueOnce([source] as any).mockResolvedValue([source, copy] as any);
    getQueueMock.mockResolvedValue({ ...copy, entries: [] } as any);
    duplicateQueueMock.mockResolvedValue({} as any);

    render(<QueuesPage />);
    await screen.findByText('Daily');
    fireEvent.click(screen.getByRole('button', { name: 'Duplicate Daily' }));
    const dialog = await screen.findByRole('dialog');
    fireEvent.click(within(dialog).getByRole('button', { name: /Duplicate|Create|Save/ }));
    await waitFor(() => expect(duplicateQueueMock).toHaveBeenCalled());
    fireEvent.click(await screen.findByText('Daily (copy)'));

    await screen.findByText('Edit Queue');
    expect(screen.getByLabelText('Device recovery')).toBeChecked();
    expect(screen.getByLabelText('Recovery maximum attempts')).toHaveValue(3);
  });
});
