import React from 'react';
import { fireEvent, render, screen, within } from '@testing-library/react';
import { QueuesPage } from '../QueuesPage';
import { listQueues, getQueue } from '../../services/queues';
import { listSequences } from '../../services/sequences';
import { listQueueTemplates } from '../../services/queueTemplates';

jest.mock('../../services/queues');
jest.mock('../../services/sequences');
jest.mock('../../services/queueTemplates');
jest.mock('../../services/useAdbDevices', () => ({
  useAdbDevices: () => ({ devices: [{ serial: 'emu-1' }], loading: false, error: undefined, refresh: () => {} }),
}));

const listQueuesMock = listQueues as jest.MockedFunction<typeof listQueues>;
const getQueueMock = getQueue as jest.MockedFunction<typeof getQueue>;
const listSequencesMock = listSequences as jest.MockedFunction<typeof listSequences>;
const listTemplatesMock = listQueueTemplates as jest.MockedFunction<typeof listQueueTemplates>;

const queue = (over: Partial<any> = {}) => ({
  id: 'q1', name: 'Daily', emulatorSerial: 'emu-1', cycleExecution: false,
  status: 'Stopped', entryCount: 0, linkedTemplateId: null, ...over,
});

beforeEach(() => {
  jest.resetAllMocks();
  listQueuesMock.mockResolvedValue([queue()] as any);
  listSequencesMock.mockResolvedValue([] as any);
  listTemplatesMock.mockResolvedValue([] as any);
});

describe('QueuesPage auto-load on open (US1)', () => {
  it('shows the linked template name and the auto-loaded entries when opening a linked queue', async () => {
    getQueueMock.mockResolvedValue({
      ...queue({ entryCount: 2, linkedTemplateId: 't1' }),
      linkedTemplateName: 'Daily Farm',
      entries: [
        { entryId: 'e1', sequenceId: 'seq-a', sequenceName: 'A', stale: false },
        { entryId: 'e2', sequenceId: 'seq-b', sequenceName: 'B', stale: false },
      ],
    } as any);

    render(<QueuesPage />);
    fireEvent.click(await screen.findByText('Daily'));
    await screen.findByText('Edit Queue');

    const controls = screen.getByRole('region', { name: 'Queue templates' });
    expect(within(controls).getByText('Daily Farm')).toBeInTheDocument();
    expect(screen.getAllByTestId('queue-entry')).toHaveLength(2);
  });

  it('shows "(no template)" when opening an unlinked queue', async () => {
    getQueueMock.mockResolvedValue({
      ...queue({ linkedTemplateId: null }),
      linkedTemplateName: null,
      entries: [],
    } as any);

    render(<QueuesPage />);
    fireEvent.click(await screen.findByText('Daily'));
    await screen.findByText('Edit Queue');

    const controls = screen.getByRole('region', { name: 'Queue templates' });
    expect(within(controls).getByText('(no template)')).toBeInTheDocument();
  });
});
