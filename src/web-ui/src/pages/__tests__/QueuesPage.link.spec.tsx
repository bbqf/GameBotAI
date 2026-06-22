import React from 'react';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { QueuesPage } from '../QueuesPage';
import { listQueues, getQueue, replaceQueueEntries, setQueueTemplateLink } from '../../services/queues';
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
const setLinkMock = setQueueTemplateLink as jest.MockedFunction<typeof setQueueTemplateLink>;
const listSequencesMock = listSequences as jest.MockedFunction<typeof listSequences>;
const saveTemplateMock = saveQueueTemplate as jest.MockedFunction<typeof saveQueueTemplate>;
const getTemplateMock = getQueueTemplate as jest.MockedFunction<typeof getQueueTemplate>;
const listTemplatesMock = listQueueTemplates as jest.MockedFunction<typeof listQueueTemplates>;

const queue = (over: Partial<any> = {}) => ({
  id: 'q1', name: 'Daily', emulatorSerial: 'emu-1', cycleExecution: false,
  status: 'Stopped', entryCount: 0, linkedTemplateId: null, ...over,
});

const emptyDetail = (over: Partial<any> = {}) => ({ ...queue(over), linkedTemplateName: null, entries: [] });

beforeEach(() => {
  jest.resetAllMocks();
  listQueuesMock.mockResolvedValue([queue()] as any);
  listSequencesMock.mockResolvedValue([] as any);
  getQueueMock.mockResolvedValue(emptyDetail() as any);
  listTemplatesMock.mockResolvedValue([{ id: 't1', name: 'Daily Farm', entryCount: 1, createdAt: null, updatedAt: null }] as any);
  getTemplateMock.mockResolvedValue({
    id: 't1', name: 'Daily Farm', entryCount: 1, createdAt: null, updatedAt: null,
    entries: [{ sequenceId: 'seq-x', sequenceName: 'X', stale: false }],
  } as any);
  replaceEntriesMock.mockResolvedValue({} as any);
  setLinkMock.mockResolvedValue({} as any);
});

const openEdit = async () => {
  render(<QueuesPage />);
  fireEvent.click(await screen.findByText('Daily'));
  await screen.findByText('Edit Queue');
};

describe('QueuesPage link wiring (US2)', () => {
  it('links the queue to the loaded template id after loading', async () => {
    await openEdit();
    fireEvent.click(screen.getByText('(no template)'));
    fireEvent.click(within(await screen.findByRole('region', { name: 'Load template' })).getByText('Load'));

    await waitFor(() => expect(replaceEntriesMock).toHaveBeenCalledWith('q1', ['seq-x']));
    expect(setLinkMock).toHaveBeenCalledWith('q1', 't1');
  });

  it('links the queue to the saved template id after saving', async () => {
    saveTemplateMock.mockResolvedValue({ id: 't9', name: 'Patrol', entryCount: 0, createdAt: null, updatedAt: null, entries: [] } as any);
    await openEdit();
    fireEvent.click(screen.getByText('(no template)'));
    const area = await screen.findByRole('region', { name: 'Load template' });
    fireEvent.change(within(area).getByLabelText('Template name'), { target: { value: 'Patrol' } });
    fireEvent.click(within(area).getByText('Rename'));

    await waitFor(() => expect(saveTemplateMock).toHaveBeenCalled());
    expect(setLinkMock).toHaveBeenCalledWith('q1', 't9');
  });
});
