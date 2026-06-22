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
  it('renders the edit controls in new order: name+emulator → cycle → Save/Cancel → template section → sequences', async () => {
    await openEdit();
    const name = screen.getByLabelText('Name *');
    const emulator = screen.getByLabelText('Emulator *');
    const cycle = screen.getByLabelText('Cycle execution');
    const save = screen.getByText('Save');
    const templates = screen.getByRole('region', { name: 'Queue templates' });
    const sequences = screen.getByRole('region', { name: 'Queue sequences' });

    expect(follows(name, emulator)).toBe(true);
    expect(follows(emulator, cycle)).toBe(true);
    expect(follows(cycle, save)).toBe(true);
    expect(follows(save, templates)).toBe(true);
    // sequences are inside the template section
    expect(follows(templates, sequences)).toBe(true);
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

describe('QueuesPage overview table columns (US3)', () => {
  it('does not render a Sequences column, but keeps Name/Emulator/Cycle/Status/Actions', async () => {
    render(<QueuesPage />);
    await screen.findByText('Daily');

    expect(screen.queryByRole('columnheader', { name: 'Sequences' })).not.toBeInTheDocument();
    ['Name', 'Emulator', 'Cycle', 'Status', 'Actions'].forEach((header) => {
      expect(screen.getByRole('columnheader', { name: header })).toBeInTheDocument();
    });
  });

  it('keeps the row actions working with the Sequences column removed', async () => {
    render(<QueuesPage />);
    await screen.findByText('Daily');

    // A stopped queue exposes Start / Edit / Delete (Schedule only appears while running).
    expect(screen.getByRole('button', { name: 'Start' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Edit' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Delete' })).toBeInTheDocument();
  });

  it('spans the expanded live-schedule row across the 5 remaining columns', async () => {
    listQueuesMock.mockResolvedValue([
      { id: 'q1', name: 'Daily', emulatorSerial: 'emu-9', cycleExecution: false, status: 'Running', entryCount: 1 },
    ] as any);
    render(<QueuesPage />);
    await screen.findByText('Daily');

    fireEvent.click(screen.getByRole('button', { name: 'Schedule a sequence for Daily' }));
    const cell = document.querySelector('.queues-live-schedule-row td') as HTMLTableCellElement;
    expect(cell).not.toBeNull();
    expect(cell.colSpan).toBe(5);
  });
});
