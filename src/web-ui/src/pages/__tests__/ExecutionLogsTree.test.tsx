import React from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { ExecutionLogsPage } from '../ExecutionLogs';
import { getExecutionLogDetail, getExecutionSubtree, listExecutionLogs } from '../../services/executionLogsApi';

jest.mock('../../services/executionLogsApi');
jest.mock('../../hooks/useNavigationCollapse', () => ({
  useNavigationCollapse: () => ({ isCollapsed: false })
}));

const listExecutionLogsMock = listExecutionLogs as jest.MockedFunction<typeof listExecutionLogs>;
const getExecutionLogDetailMock = getExecutionLogDetail as jest.MockedFunction<typeof getExecutionLogDetail>;
const getExecutionSubtreeMock = getExecutionSubtree as jest.MockedFunction<typeof getExecutionSubtree>;

const sequenceItem = {
  id: 'seq-exec-1',
  timestampUtc: new Date('2026-06-02T10:00:00.000Z').toISOString(),
  executionType: 'sequence' as const,
  finalStatus: 'success' as const,
  childCount: 2,
  objectRef: { objectType: 'sequence', objectId: 'seq-1', displayNameSnapshot: 'My Sequence' },
  summary: 'My Sequence success'
};

const subtree = {
  executionId: 'seq-exec-1',
  finalStatus: 'success' as const,
  root: {
    nodeKind: 'sequence' as const,
    executionId: 'seq-exec-1',
    order: 0,
    label: 'My Sequence',
    status: 'success' as const,
    children: [
      {
        nodeKind: 'command' as const,
        executionId: 'child-a',
        order: 1,
        label: 'Open Mail',
        status: 'success' as const,
        commandName: 'Open Mail',
        deepLink: {
          sequenceId: 'seq-1',
          stepId: 'step-1',
          sequenceLabel: 'My Sequence',
          stepLabel: 'Step 1',
          resolutionStatus: 'resolved' as const,
          directPath: '/authoring/sequences/seq-1?stepId=step-1'
        },
        children: [
          { nodeKind: 'tap' as const, order: 1, label: 'Tap target', status: 'success' as const, children: [] }
        ]
      }
    ]
  }
};

describe('ExecutionLogsPage tree', () => {
  beforeEach(() => {
    jest.resetAllMocks();
    listExecutionLogsMock.mockResolvedValue({ items: [sequenceItem], nextPageToken: undefined });
    getExecutionLogDetailMock.mockResolvedValue({
      executionId: 'seq-exec-1',
      summary: 'My Sequence success',
      relatedObjects: [],
      snapshot: { isAvailable: false },
      stepOutcomes: []
    });
    getExecutionSubtreeMock.mockResolvedValue(subtree);
  });

  it('expands a sequence row to reveal nested sub-elements', async () => {
    render(<ExecutionLogsPage />);

    await screen.findByText('My Sequence');
    fireEvent.click(screen.getByRole('button', { name: 'Expand sub-elements' }));

    await waitFor(() => expect(getExecutionSubtreeMock).toHaveBeenCalledWith('seq-exec-1'));
    expect(await screen.findByText('Open Mail')).toBeInTheDocument();
    // Nested command primitive is reachable through expansion.
    expect(screen.getByText('Tap target')).toBeInTheDocument();
  });

  it('exposes a deep link from a sub-element to its authored sequence/step', async () => {
    render(<ExecutionLogsPage />);

    await screen.findByText('My Sequence');
    fireEvent.click(screen.getByRole('button', { name: 'Expand sub-elements' }));
    await screen.findByText('Open Mail');

    // The nested command sub-element offers an in-sequence deep link (FR-011) and
    // activating it is handled without error.
    const deepLink = screen.getByRole('button', { name: 'Open in sequence' });
    expect(deepLink).toBeInTheDocument();
    expect(() => fireEvent.click(deepLink)).not.toThrow();
  });
});
