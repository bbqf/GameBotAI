import { setQueueTemplateLink } from '../queues';
import { putJson } from '../../lib/api';

jest.mock('../../lib/api', () => {
  const actual = jest.requireActual('../../lib/api');
  return {
    ...actual,
    getJson: jest.fn(),
    postJson: jest.fn(),
    putJson: jest.fn(),
    deleteJson: jest.fn(),
  };
});

describe('queues service - setQueueTemplateLink', () => {
  const mockPutJson = putJson as jest.MockedFunction<typeof putJson>;

  beforeEach(() => jest.clearAllMocks());

  it('sets the link via PUT with the template id', async () => {
    mockPutJson.mockResolvedValue({} as any);
    await setQueueTemplateLink('q1', 't1');
    expect(mockPutJson).toHaveBeenCalledWith('/api/queues/q1/template', { templateId: 't1' });
  });

  it('clears the link via PUT with null', async () => {
    mockPutJson.mockResolvedValue({} as any);
    await setQueueTemplateLink('q1', null);
    expect(mockPutJson).toHaveBeenCalledWith('/api/queues/q1/template', { templateId: null });
  });
});
