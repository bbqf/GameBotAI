import React from 'react';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { QueuesPage } from '../QueuesPage';
import { listQueues, getQueue, replaceQueueEntries } from '../../services/queues';
import { listSequences } from '../../services/sequences';
import { saveQueueTemplate, getQueueTemplate, listQueueTemplates } from '../../services/queueTemplates';

jest.mock('../../services/queues');
jest.mock('../../services/sequences');
jest.mock('../../services/queueTemplates');
jest.mock('../../services/useAdbDevices', () => ({
  useAdbDevices: () => ({ devices: [{ serial: 'emu-1' }], loading: false, error: undefined, refresh: () => {} }),
}));

const listQueuesMock = listQueues as jest.MockedFunction<typeof listQueues>;
const getQueueMock = getQueue as jest.MockedFunction<typeof getQueue>;
const replaceEntriesMock = replaceQueueEntries as jest.MockedFunction<typeof replaceQueueEntries>;
const listSequencesMock = listSequences as jest.MockedFunction<typeof listSequences>;
const saveTemplateMock = saveQueueTemplate as jest.MockedFunction<typeof saveQueueTemplate>;
const getTemplateMock = getQueueTemplate as jest.MockedFunction<typeof getQueueTemplate>;
const listTemplatesMock = listQueueTemplates as jest.MockedFunction<typeof listQueueTemplates>;

const queue = (over: Partial<any> = {}) => ({
  id: 'q1', name: 'Daily', emulatorSerial: 'emu-1', cycleExecution: false, status: 'Stopped', entryCount: 2, ...over,
});

const detailWithEntries = (over: Partial<any> = {}) => ({
  ...queue(over),
  entries: [
    { entryId: 'e1', sequenceId: 'seq-a', sequenceName: 'A', stale: false },
    { entryId: 'e2', sequenceId: 'seq-b', sequenceName: 'B', stale: false },
  ],
  ...over,
});

beforeEach(() => {
  jest.resetAllMocks();
  listQueuesMock.mockResolvedValue([queue()] as any);
  listSequencesMock.mockResolvedValue([] as any);
  getQueueMock.mockResolvedValue(detailWithEntries() as any);
  listTemplatesMock.mockResolvedValue([{ id: 't1', name: 'Daily Farm', entryCount: 1, createdAt: null, updatedAt: null }] as any);
  saveTemplateMock.mockResolvedValue({} as any);
  replaceEntriesMock.mockResolvedValue({} as any);
});

/** Saves a template so the queue becomes "associated" with the name "Daily Farm". */
const openEditAndAssociate = async () => {
  render(<QueuesPage />);
  await screen.findByText('Daily');
  fireEvent.click(screen.getByText('Daily'));
  await screen.findByText('Edit Queue');
  fireEvent.click(screen.getByText('Save Template'));
  const section = await screen.findByRole('region', { name: 'Save template' });
  fireEvent.change(within(section).getByLabelText('Template name'), { target: { value: 'Daily Farm' } });
  fireEvent.click(within(section).getByText('Save'));
  // After save, the name button reflects the associated template.
  await screen.findByText('Daily Farm');
};

describe('QueuesPage reload template', () => {
  it('disables Reload Template when no template is associated', async () => {
    render(<QueuesPage />);
    await screen.findByText('Daily');
    fireEvent.click(screen.getByText('Daily'));
    await screen.findByText('Edit Queue');
    expect(screen.getByText('Reload Template')).toBeDisabled();
  });

  it('prompts before replacing divergent non-empty entries, then applies on confirm', async () => {
    getTemplateMock.mockResolvedValue({
      id: 't1', name: 'Daily Farm', entryCount: 1, createdAt: null, updatedAt: null,
      entries: [{ sequenceId: 'seq-x', sequenceName: 'X', stale: false }],
    } as any);
    await openEditAndAssociate();

    fireEvent.click(screen.getByText('Reload Template'));
    const confirm = await screen.findByRole('dialog', { name: 'Reload template' });
    fireEvent.click(within(confirm).getByRole('button', { name: 'Reload' }));

    await waitFor(() => expect(replaceEntriesMock).toHaveBeenCalledWith('q1', ['seq-x']));
  });

  it('leaves entries unchanged when the reload confirmation is canceled', async () => {
    getTemplateMock.mockResolvedValue({
      id: 't1', name: 'Daily Farm', entryCount: 1, createdAt: null, updatedAt: null,
      entries: [{ sequenceId: 'seq-x', sequenceName: 'X', stale: false }],
    } as any);
    await openEditAndAssociate();

    fireEvent.click(screen.getByText('Reload Template'));
    const confirm = await screen.findByRole('dialog', { name: 'Reload template' });
    fireEvent.click(within(confirm).getByRole('button', { name: 'Cancel' }));

    expect(replaceEntriesMock).not.toHaveBeenCalled();
  });

  it('applies without a prompt when current entries already match the template', async () => {
    getTemplateMock.mockResolvedValue({
      id: 't1', name: 'Daily Farm', entryCount: 2, createdAt: null, updatedAt: null,
      entries: [
        { sequenceId: 'seq-a', sequenceName: 'A', stale: false },
        { sequenceId: 'seq-b', sequenceName: 'B', stale: false },
      ],
    } as any);
    await openEditAndAssociate();

    fireEvent.click(screen.getByText('Reload Template'));
    await waitFor(() => expect(getTemplateMock).toHaveBeenCalled());
    expect(screen.queryByRole('dialog', { name: 'Reload template' })).not.toBeInTheDocument();
    expect(replaceEntriesMock).not.toHaveBeenCalled();
  });

  it('applies without a prompt when the queue is empty', async () => {
    getQueueMock.mockResolvedValue(detailWithEntries({ entryCount: 0, entries: [] }) as any);
    getTemplateMock.mockResolvedValue({
      id: 't1', name: 'Daily Farm', entryCount: 1, createdAt: null, updatedAt: null,
      entries: [{ sequenceId: 'seq-x', sequenceName: 'X', stale: false }],
    } as any);
    await openEditAndAssociate();

    fireEvent.click(screen.getByText('Reload Template'));
    await waitFor(() => expect(replaceEntriesMock).toHaveBeenCalledWith('q1', ['seq-x']));
    expect(screen.queryByRole('dialog', { name: 'Reload template' })).not.toBeInTheDocument();
  });

  it('reports when the associated template is no longer available', async () => {
    await openEditAndAssociate();
    listTemplatesMock.mockResolvedValue([] as any);

    fireEvent.click(screen.getByText('Reload Template'));
    expect(await screen.findByText('Template "Daily Farm" is no longer available.')).toBeInTheDocument();
    expect(replaceEntriesMock).not.toHaveBeenCalled();
  });
});
