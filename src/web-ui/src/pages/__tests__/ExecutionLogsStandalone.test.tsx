import React from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { ExecutionLogsPage } from '../ExecutionLogs';
import { getExecutionLogDetail, getExecutionSubtree, listExecutionLogs } from '../../services/executionLogsApi';

jest.mock('../../services/executionLogsApi');
jest.mock('../../hooks/useNavigationCollapse', () => ({
  useNavigationCollapse: () => ({ isCollapsed: false })
}));

const listExecutionLogsMock = listExecutionLogs as jest.MockedFunction<typeof listExecutionLogs>;
const getExecutionLogDetailMock = getExecutionLogDetail as jest.MockedFunction<typeof getExecutionLogDetail>;
const getExecutionSubtreeMock = getExecutionSubtree as jest.MockedFunction<typeof getExecutionSubtree>;

const commandItem = {
  id: 'cmd-exec-1',
  timestampUtc: new Date('2026-06-02T10:00:00.000Z').toISOString(),
  executionType: 'command' as const,
  finalStatus: 'success' as const,
  childCount: 0,
  objectRef: { objectType: 'command', objectId: 'cmd-1', displayNameSnapshot: 'Solo Command' },
  summary: 'Solo Command success'
};

describe('ExecutionLogsPage stand-alone command', () => {
  beforeEach(() => {
    jest.resetAllMocks();
    listExecutionLogsMock.mockResolvedValue({ items: [commandItem], nextPageToken: undefined });
    getExecutionLogDetailMock.mockResolvedValue({
      executionId: 'cmd-exec-1',
      summary: 'Solo Command finished successfully.',
      relatedObjects: [],
      snapshot: { isAvailable: false },
      stepOutcomes: [
        { stepName: 'Tap target', status: 'success', message: 'Tapped at the expected location.' }
      ]
    });
  });

  it('shows a stand-alone command as a non-expandable leaf row', async () => {
    render(<ExecutionLogsPage />);

    await screen.findByText('Solo Command');
    expect(screen.queryByRole('button', { name: 'Expand sub-elements' })).not.toBeInTheDocument();
    expect(getExecutionSubtreeMock).not.toHaveBeenCalled();
  });

  it('still surfaces full command detail in the detail panel', async () => {
    render(<ExecutionLogsPage />);

    fireEvent.click(await screen.findByText('Solo Command'));

    expect(await screen.findByText('Solo Command finished successfully.')).toBeInTheDocument();
    expect(screen.getByText(/Tap target/)).toBeInTheDocument();
    expect(screen.getByText(/Tapped at the expected location./)).toBeInTheDocument();
  });
});
