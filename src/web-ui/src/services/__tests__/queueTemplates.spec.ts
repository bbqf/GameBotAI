import {
  listQueueTemplates,
  getQueueTemplate,
  saveQueueTemplate,
  deleteQueueTemplate,
} from '../queueTemplates';
import { getJson, postJson, deleteJson } from '../../lib/api';

jest.mock('../../lib/api', () => {
  const actual = jest.requireActual('../../lib/api');
  return {
    ...actual,
    getJson: jest.fn(),
    postJson: jest.fn(),
    putJson: jest.fn(),
    patchJson: jest.fn(),
    deleteJson: jest.fn(),
  };
});

describe('queueTemplates service', () => {
  const mockGetJson = getJson as jest.MockedFunction<typeof getJson>;
  const mockPostJson = postJson as jest.MockedFunction<typeof postJson>;
  const mockDeleteJson = deleteJson as jest.MockedFunction<typeof deleteJson>;

  beforeEach(() => jest.clearAllMocks());

  it('lists templates from the collection endpoint', async () => {
    mockGetJson.mockResolvedValue([] as any);
    await listQueueTemplates();
    expect(mockGetJson).toHaveBeenCalledWith('/api/queue-templates');
  });

  it('gets a template detail by id', async () => {
    mockGetJson.mockResolvedValue({ id: 't1', entries: [] } as any);
    await getQueueTemplate('t1');
    expect(mockGetJson).toHaveBeenCalledWith('/api/queue-templates/t1');
  });

  it('saves a template via POST with name, entries and overwrite', async () => {
    mockPostJson.mockResolvedValue({ id: 't1', entries: [] } as any);
    const payload = { name: 'Daily Farm', entries: [{ sequenceId: 'seq-a' }, { sequenceId: 'seq-b' }], overwrite: false };
    await saveQueueTemplate(payload);
    expect(mockPostJson).toHaveBeenCalledWith('/api/queue-templates', payload);
  });

  it('deletes a template by id', async () => {
    mockDeleteJson.mockResolvedValue(undefined as any);
    await deleteQueueTemplate('t1');
    expect(mockDeleteJson).toHaveBeenCalledWith('/api/queue-templates/t1');
  });
});
