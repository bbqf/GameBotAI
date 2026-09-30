import * as api from '../../lib/api';
import {
  createNotificationTarget,
  deleteNotificationTarget,
  listNotificationTargets,
  listNotificationTypes,
  testNotificationTarget,
  updateNotificationTarget
} from '../notifications';
import { setQueueNotificationLevel } from '../queues';

jest.mock('../../lib/api');

const mockGet = api.getJson as jest.MockedFunction<typeof api.getJson>;
const mockPost = api.postJson as jest.MockedFunction<typeof api.postJson>;
const mockPut = api.putJson as jest.MockedFunction<typeof api.putJson>;
const mockDelete = api.deleteJson as jest.MockedFunction<typeof api.deleteJson>;

const input = {
  type: 'telegram',
  name: 'Phone',
  enabled: true,
  settings: { chatId: '1' },
  secrets: { botToken: '123456:ABCDEFGHIJKLMNOPQRSTUVWXYZ' }
};

describe('notifications service', () => {
  beforeEach(() => {
    jest.resetAllMocks();
  });

  it('lists the targets with GET', async () => {
    mockGet.mockResolvedValue([]);

    await expect(listNotificationTargets()).resolves.toEqual([]);

    expect(mockGet).toHaveBeenCalledWith('/api/notifications/targets');
  });

  it('creates a target with POST and the body', async () => {
    mockPost.mockResolvedValue({ id: 't1' });

    await createNotificationTarget(input);

    expect(mockPost).toHaveBeenCalledWith('/api/notifications/targets', input);
  });

  it('updates a target with PUT on the ID', async () => {
    mockPut.mockResolvedValue({ id: 't1' });

    await updateNotificationTarget('t1', input);

    expect(mockPut).toHaveBeenCalledWith('/api/notifications/targets/t1', input);
  });

  it('deletes a target with DELETE on the ID', async () => {
    mockDelete.mockResolvedValue(undefined);

    await deleteNotificationTarget('t1');

    expect(mockDelete).toHaveBeenCalledWith('/api/notifications/targets/t1');
  });

  it('tests a target with POST on the test route and no body value', async () => {
    mockPost.mockResolvedValue({ ok: true, reason: null });

    await expect(testNotificationTarget('t1')).resolves.toEqual({ ok: true, reason: null });

    expect(mockPost).toHaveBeenCalledWith('/api/notifications/targets/t1/test', {});
  });

  it('lists the types with GET', async () => {
    mockGet.mockResolvedValue([]);

    await listNotificationTypes();

    expect(mockGet).toHaveBeenCalledWith('/api/notifications/types');
  });

  it('sets the queue level with PUT on the level route', async () => {
    mockPut.mockResolvedValue({ id: 'q1', notificationLevel: 'failure' });

    await setQueueNotificationLevel('q1', 'failure');

    expect(mockPut).toHaveBeenCalledWith('/api/queues/q1/notification-level', { level: 'failure' });
  });

  it('never returns the token from a list call', async () => {
    mockGet.mockResolvedValue([{ id: 't1', hasSecret: true, secretHint: '••••Xy9z', settings: { chatId: '1' } }]);

    const list = await listNotificationTargets();

    expect(JSON.stringify(list)).not.toContain('botToken');
  });
});
