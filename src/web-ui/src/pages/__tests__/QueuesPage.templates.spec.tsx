import React from 'react';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { QueuesPage } from '../QueuesPage';
import { listQueues, getQueue, replaceQueueEntries } from '../../services/queues';
import { listSequences } from '../../services/sequences';
import { saveQueueTemplate, getQueueTemplate, listQueueTemplates, deleteQueueTemplate } from '../../services/queueTemplates';
import { ApiError } from '../../lib/api';

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
const deleteTemplateMock = deleteQueueTemplate as jest.MockedFunction<typeof deleteQueueTemplate>;

const queue = (over: Partial<any> = {}) => ({
  id: 'q1', name: 'Daily', emulatorSerial: 'emu-1', cycleExecution: false, status: 'Stopped', entryCount: 2, ...over,
});

const detailWithEntries = () => ({
  ...queue(),
  entries: [
    { entryId: 'e1', sequenceId: 'seq-a', sequenceName: 'A', stale: false },
    { entryId: 'e2', sequenceId: 'seq-b', sequenceName: 'B', stale: false },
  ],
});

const templateDetail = () => ({
  id: 't1', name: 'Daily Farm', entryCount: 1, createdAt: null, updatedAt: null,
  entries: [{ sequenceId: 'seq-x', sequenceName: 'X', stale: false }],
});

beforeEach(() => {
  jest.resetAllMocks();
  listQueuesMock.mockResolvedValue([queue()] as any);
  listSequencesMock.mockResolvedValue([] as any);
  getQueueMock.mockResolvedValue(detailWithEntries() as any);
  listTemplatesMock.mockResolvedValue([{ id: 't1', name: 'Daily Farm', entryCount: 1, createdAt: null, updatedAt: null }] as any);
  getTemplateMock.mockResolvedValue(templateDetail() as any);
  replaceEntriesMock.mockResolvedValue({} as any);
});

const openSaveSection = async () => {
  render(<QueuesPage />);
  await screen.findByText('Daily');
  fireEvent.click(screen.getByText('Daily'));
  await screen.findByText('Edit Queue');
  fireEvent.click(screen.getByText('Save Template'));
  return screen.findByRole('region', { name: 'Save template' });
};

describe('QueuesPage template save wiring', () => {
  it('builds sequenceIds from the current entries and saves', async () => {
    saveTemplateMock.mockResolvedValue({} as any);
    const section = await openSaveSection();

    fireEvent.change(within(section).getByLabelText('Template name'), { target: { value: 'Daily Farm' } });
    fireEvent.click(within(section).getByText('Save'));

    await waitFor(() =>
      expect(saveTemplateMock).toHaveBeenCalledWith({ name: 'Daily Farm', sequenceIds: ['seq-a', 'seq-b'], overwrite: false })
    );
    expect(await screen.findByText('Template "Daily Farm" saved successfully.')).toBeInTheDocument();
  });

  it('on 409 conflict prompts to overwrite and re-saves with overwrite=true', async () => {
    saveTemplateMock
      .mockRejectedValueOnce(new ApiError(409, 'template_exists'))
      .mockResolvedValueOnce({} as any);
    const section = await openSaveSection();

    fireEvent.change(within(section).getByLabelText('Template name'), { target: { value: 'Daily Farm' } });
    fireEvent.click(within(section).getByText('Save'));

    fireEvent.click(await within(section).findByText('Overwrite'));

    await waitFor(() =>
      expect(saveTemplateMock).toHaveBeenLastCalledWith({ name: 'Daily Farm', sequenceIds: ['seq-a', 'seq-b'], overwrite: true })
    );
  });
});

const openLoadSection = async () => {
  render(<QueuesPage />);
  await screen.findByText('Daily');
  fireEvent.click(screen.getByText('Daily'));
  await screen.findByText('Edit Queue');
  fireEvent.click(screen.getByText('(no template)'));
  return screen.findByRole('region', { name: 'Load template' });
};

describe('QueuesPage template load wiring', () => {
  it('replaces entries after confirming the replacement for a non-empty queue', async () => {
    const picker = await openLoadSection();
    fireEvent.click(within(picker).getByText('Load'));

    const confirm = await screen.findByRole('dialog', { name: 'Replace queue entries' });
    fireEvent.click(within(confirm).getByRole('button', { name: 'Replace' }));

    await waitFor(() => expect(replaceEntriesMock).toHaveBeenCalledWith('q1', ['seq-x']));
  });

  it('does not replace entries when the replacement is canceled', async () => {
    const picker = await openLoadSection();
    fireEvent.click(within(picker).getByText('Load'));

    const confirm = await screen.findByRole('dialog', { name: 'Replace queue entries' });
    fireEvent.click(within(confirm).getByRole('button', { name: 'Cancel' }));

    expect(replaceEntriesMock).not.toHaveBeenCalled();
  });

  it('pre-fills the save section with the loaded template name', async () => {
    getQueueMock.mockResolvedValue({ ...queue({ entryCount: 0 }), entries: [] } as any);
    const picker = await openLoadSection();
    fireEvent.click(within(picker).getByText('Load'));
    await waitFor(() => expect(replaceEntriesMock).toHaveBeenCalledWith('q1', ['seq-x']));

    fireEvent.click(screen.getByText('Save Template'));
    const saveSection = await screen.findByRole('region', { name: 'Save template' });
    expect(within(saveSection).getByLabelText('Template name')).toHaveValue('Daily Farm');
  });

  it('disables the Load action while the queue is running', async () => {
    getQueueMock.mockResolvedValue({ ...detailWithEntries(), status: 'Running' } as any);
    render(<QueuesPage />);
    await screen.findByText('Daily');
    fireEvent.click(screen.getByText('Daily'));
    await screen.findByText('Edit Queue');
    fireEvent.click(screen.getByText('(no template)'));
    const picker = await screen.findByRole('region', { name: 'Load template' });
    expect(within(picker).getByText('Load')).toBeDisabled();
  });

  it('loads the same template independently into two different queues (FR-016)', async () => {
    listQueuesMock.mockResolvedValue([queue({ id: 'q1', name: 'Daily' }), queue({ id: 'q2', name: 'Arena' })] as any);
    getQueueMock.mockImplementation((id: string) => Promise.resolve({ ...queue({ id, entryCount: 0 }), entries: [] } as any));

    render(<QueuesPage />);
    await screen.findByText('Daily');

    fireEvent.click(screen.getByText('Daily'));
    await screen.findByText('Edit Queue');
    fireEvent.click(screen.getByText('(no template)'));
    fireEvent.click(within(await screen.findByRole('region', { name: 'Load template' })).getByText('Load'));
    await waitFor(() => expect(replaceEntriesMock).toHaveBeenCalledWith('q1', ['seq-x']));

    fireEvent.click(screen.getByText('Arena'));
    await waitFor(() => expect(getQueueMock).toHaveBeenLastCalledWith('q2'));
    fireEvent.click(screen.getByText('(no template)'));
    fireEvent.click(within(await screen.findByRole('region', { name: 'Load template' })).getByText('Load'));
    await waitFor(() => expect(replaceEntriesMock).toHaveBeenCalledWith('q2', ['seq-x']));
  });
});

describe('QueuesPage template delete wiring', () => {
  it('deletes a template from the picker without touching the queue entries', async () => {
    deleteTemplateMock.mockResolvedValue(undefined as any);
    const picker = await openLoadSection();
    expect(screen.getAllByTestId('queue-entry')).toHaveLength(2);

    fireEvent.click(within(picker).getByText('Delete'));
    const confirm = await screen.findByRole('dialog', { name: 'Confirm Delete' });
    listTemplatesMock.mockResolvedValue([] as any);
    fireEvent.click(within(confirm).getByRole('button', { name: 'Delete' }));

    await waitFor(() => expect(deleteTemplateMock).toHaveBeenCalledWith('t1'));
    expect(replaceEntriesMock).not.toHaveBeenCalled();
    expect(screen.getAllByTestId('queue-entry')).toHaveLength(2);
  });
});
