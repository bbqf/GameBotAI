import React from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { QueuesPage } from '../QueuesPage';
import { listQueues, getQueue, updateQueue } from '../../services/queues';
import { listSequences } from '../../services/sequences';

jest.mock('../../services/queues');
jest.mock('../../services/sequences');
jest.mock('../../services/queueTemplates');
jest.mock('../../services/useAdbDevices', () => ({
  useAdbDevices: () => ({ devices: [{ serial: 'emu-1' }], loading: false, error: undefined, refresh: () => {} }),
}));

const listQueuesMock = listQueues as jest.MockedFunction<typeof listQueues>;
const getQueueMock = getQueue as jest.MockedFunction<typeof getQueue>;
const updateQueueMock = updateQueue as jest.MockedFunction<typeof updateQueue>;
const listSequencesMock = listSequences as jest.MockedFunction<typeof listSequences>;

const detail = () => ({
  id: 'q1', name: 'Daily', emulatorSerial: 'emu-9', cycleExecution: false, status: 'Stopped', entryCount: 1,
  entries: [{ entryId: 'e1', sequenceId: 'seq-a', sequenceName: 'A', stale: false }],
});

beforeEach(() => {
  jest.resetAllMocks();
  listQueuesMock.mockResolvedValue([{ id: 'q1', name: 'Daily', emulatorSerial: 'emu-9', cycleExecution: false, status: 'Stopped', entryCount: 1 }] as any);
  listSequencesMock.mockResolvedValue([] as any);
  getQueueMock.mockResolvedValue(detail() as any);
  updateQueueMock.mockResolvedValue({} as any);
});

const openEdit = async () => {
  render(<QueuesPage />);
  await screen.findByText('Daily');
  fireEvent.click(screen.getByText('Daily'));
  await screen.findByText('Edit Queue');
};

const follows = (a: Node, b: Node) =>
  Boolean(a.compareDocumentPosition(b) & Node.DOCUMENT_POSITION_FOLLOWING);

describe('QueuesPage edit layout', () => {
  it('renders the edit controls in row order: name+emulator -> templates -> cycle -> sequences -> Save/Cancel', async () => {
    await openEdit();
    const name = screen.getByLabelText('Name *');
    const emulator = screen.getByLabelText('Emulator *');
    const templates = screen.getByRole('region', { name: 'Queue templates' });
    const cycle = screen.getByLabelText('Cycle execution');
    const sequences = screen.getByRole('region', { name: 'Queue sequences' });
    const save = screen.getByText('Save');

    expect(follows(name, emulator)).toBe(true);
    expect(follows(emulator, templates)).toBe(true);
    expect(follows(templates, cycle)).toBe(true);
    expect(follows(cycle, sequences)).toBe(true);
    expect(follows(sequences, save)).toBe(true);
  });

  it('shows the emulator read-only with no "cannot be changed" hint', async () => {
    await openEdit();
    const emulator = screen.getByLabelText('Emulator *') as HTMLInputElement;
    expect(emulator).toBeDisabled();
    expect(emulator).toHaveValue('emu-9');
    expect(screen.queryByText(/bound emulator cannot be changed/i)).not.toBeInTheDocument();
  });

  it('Save commits only name + cycle execution', async () => {
    await openEdit();
    fireEvent.change(screen.getByLabelText('Name *'), { target: { value: 'Daily Renamed' } });
    fireEvent.click(screen.getByLabelText('Cycle execution'));
    fireEvent.click(screen.getByText('Save'));
    await waitFor(() =>
      expect(updateQueueMock).toHaveBeenCalledWith('q1', { name: 'Daily Renamed', cycleExecution: true })
    );
  });

  it('Cancel closes the edit page', async () => {
    await openEdit();
    fireEvent.click(screen.getByText('Cancel'));
    await waitFor(() => expect(screen.queryByText('Edit Queue')).not.toBeInTheDocument());
  });
});
