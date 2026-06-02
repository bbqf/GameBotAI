import React from 'react';
import { act, render, screen } from '@testing-library/react';
import { ExecutionLogsPage } from '../ExecutionLogs';
import { getExecutionLogDetail, getExecutionSubtree, listExecutionLogs } from '../../services/executionLogsApi';

jest.mock('../../services/executionLogsApi');
jest.mock('../../hooks/useNavigationCollapse', () => ({
  useNavigationCollapse: () => ({ isCollapsed: false })
}));

const listExecutionLogsMock = listExecutionLogs as jest.MockedFunction<typeof listExecutionLogs>;
const getExecutionLogDetailMock = getExecutionLogDetail as jest.MockedFunction<typeof getExecutionLogDetail>;
const getExecutionSubtreeMock = getExecutionSubtree as jest.MockedFunction<typeof getExecutionSubtree>;

const makeItem = (finalStatus: 'running' | 'success') => ({
  id: 'seq-live-1',
  timestampUtc: new Date('2026-06-02T10:00:00.000Z').toISOString(),
  executionType: 'sequence' as const,
  finalStatus,
  childCount: 1,
  objectRef: { objectType: 'sequence', objectId: 'seq-1', displayNameSnapshot: 'Live Run' },
  summary: 'Live Run'
});

const flush = async () => {
  await act(async () => {
    await Promise.resolve();
    await Promise.resolve();
  });
};

describe('ExecutionLogsPage polling', () => {
  beforeEach(() => {
    jest.useFakeTimers();
    jest.resetAllMocks();
    getExecutionLogDetailMock.mockResolvedValue({
      executionId: 'seq-live-1',
      summary: 'Detail.',
      relatedObjects: [],
      snapshot: { isAvailable: false },
      stepOutcomes: []
    });
    getExecutionSubtreeMock.mockResolvedValue({
      executionId: 'seq-live-1',
      finalStatus: 'running',
      root: { nodeKind: 'sequence', executionId: 'seq-live-1', order: 0, label: 'Live Run', status: 'running', children: [] }
    });
  });

  afterEach(() => {
    jest.runOnlyPendingTimers();
    jest.useRealTimers();
  });

  it('polls while running and stops once the execution finishes', async () => {
    let calls = 0;
    listExecutionLogsMock.mockImplementation((() => {
      calls += 1;
      return Promise.resolve({ items: [makeItem(calls < 2 ? 'running' : 'success')], nextPageToken: undefined });
    }) as any);

    render(<ExecutionLogsPage />);
    await flush();

    expect(screen.getByText('running')).toBeInTheDocument();
    expect(listExecutionLogsMock).toHaveBeenCalledTimes(1);

    // First poll tick: re-fetches and the execution is now success.
    await act(async () => { jest.advanceTimersByTime(2000); });
    await flush();

    expect(listExecutionLogsMock).toHaveBeenCalledTimes(2);
    expect(screen.getByText('success')).toBeInTheDocument();

    // No longer running → polling stops; further ticks do not re-fetch.
    const callsAfterFinish = listExecutionLogsMock.mock.calls.length;
    await act(async () => { jest.advanceTimersByTime(6000); });
    await flush();
    expect(listExecutionLogsMock).toHaveBeenCalledTimes(callsAfterFinish);
  });

  it('refreshes an expanded in-progress subtree on each poll tick', async () => {
    listExecutionLogsMock.mockResolvedValue({ items: [makeItem('running')], nextPageToken: undefined });

    render(<ExecutionLogsPage />);
    await flush();

    // Expand the running sequence to load its subtree once.
    await act(async () => {
      screen.getByRole('button', { name: 'Expand sub-elements' }).click();
    });
    await flush();
    expect(getExecutionSubtreeMock).toHaveBeenCalledTimes(1);

    // A poll tick re-fetches the expanded subtree for live progress.
    await act(async () => { jest.advanceTimersByTime(2000); });
    await flush();
    expect(getExecutionSubtreeMock.mock.calls.length).toBeGreaterThanOrEqual(2);
  });
});
