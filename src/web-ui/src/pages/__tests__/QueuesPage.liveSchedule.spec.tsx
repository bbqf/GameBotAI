import React from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { QueuesPage } from '../QueuesPage';
import { listQueues, liveScheduleSequence } from '../../services/queues';
import { listSequences } from '../../services/sequences';

jest.mock('../../services/queues');
jest.mock('../../services/sequences');
jest.mock('../../services/useAdbDevices', () => ({
  useAdbDevices: () => ({ devices: [{ serial: 'emu-1' }], loading: false, error: undefined, refresh: () => {} }),
}));

const listQueuesMock = listQueues as jest.MockedFunction<typeof listQueues>;
const liveScheduleSequenceMock = liveScheduleSequence as jest.MockedFunction<typeof liveScheduleSequence>;
const listSequencesMock = listSequences as jest.MockedFunction<typeof listSequences>;

const runningQueue = {
  id: 'q1', name: 'Daily', emulatorSerial: 'emu-1', cycleExecution: true, status: 'Running', entryCount: 1,
};

beforeEach(() => {
  jest.resetAllMocks();
  listSequencesMock.mockResolvedValue([{ id: 'seq-a', name: 'Alpha', steps: [] }] as any);
  listQueuesMock.mockResolvedValue([runningQueue] as any);
});

const openControl = async () => {
  render(<QueuesPage />);
  await screen.findByText('Daily');
  fireEvent.click(screen.getByRole('button', { name: 'Schedule a sequence for Daily' }));
  await screen.findByLabelText('Sequence to schedule');
};

describe('QueuesPage live schedule', () => {
  it('submits a valid offset to the live-schedule API', async () => {
    liveScheduleSequenceMock.mockResolvedValue({ sequenceId: 'seq-a', offset: '00:05:00', expectedFireAt: '2026-06-17T14:05:00+00:00' } as any);
    await openControl();

    fireEvent.change(screen.getByLabelText('Sequence to schedule'), { target: { value: 'seq-a' } });
    fireEvent.change(screen.getByLabelText('Schedule offset minutes'), { target: { value: '5' } });
    fireEvent.click(screen.getByRole('button', { name: 'Schedule' }));

    await waitFor(() => expect(liveScheduleSequenceMock).toHaveBeenCalledWith('q1', 'seq-a', '00:05:00'));
    // Pending indicator shows the expected (earliest) fire time.
    const pending = await screen.findByTestId('pending-schedule');
    expect(pending).toHaveTextContent(/expected \(earliest\)/i);
    expect(pending).toHaveTextContent('Alpha');
  });

  it('blocks submission with no sequence selected and does not call the API', async () => {
    await openControl();

    fireEvent.change(screen.getByLabelText('Schedule offset minutes'), { target: { value: '5' } });
    fireEvent.click(screen.getByRole('button', { name: 'Schedule' }));

    expect(await screen.findByRole('alert')).toHaveTextContent(/select a sequence/i);
    expect(liveScheduleSequenceMock).not.toHaveBeenCalled();
  });

  it('blocks a negative offset and does not call the API', async () => {
    await openControl();

    fireEvent.change(screen.getByLabelText('Sequence to schedule'), { target: { value: 'seq-a' } });
    fireEvent.change(screen.getByLabelText('Schedule offset minutes'), { target: { value: '-5' } });
    fireEvent.click(screen.getByRole('button', { name: 'Schedule' }));

    expect(await screen.findByRole('alert')).toHaveTextContent(/non-negative/i);
    expect(liveScheduleSequenceMock).not.toHaveBeenCalled();
  });
});
