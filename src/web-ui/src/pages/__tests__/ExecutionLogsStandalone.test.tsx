import React from 'react';
import { fireEvent, render, screen, within } from '@testing-library/react';
import { ExecutionLogsPage } from '../ExecutionLogs';
import { getExecutionSubtree, listExecutionLogs } from '../../services/executionLogsApi';

jest.mock('../../services/executionLogsApi');

const listExecutionLogsMock = listExecutionLogs as jest.MockedFunction<typeof listExecutionLogs>;
const getExecutionSubtreeMock = getExecutionSubtree as jest.MockedFunction<typeof getExecutionSubtree>;

const leafCommand = {
  id: 'cmd-leaf-1',
  timestampUtc: new Date('2026-06-02T10:00:00.000Z').toISOString(),
  executionType: 'command' as const,
  finalStatus: 'success' as const,
  childCount: 0,
  objectRef: { objectType: 'command', objectId: 'cmd-leaf', displayNameSnapshot: 'Solo Command' },
  summary: 'Solo Command success'
};

const detailedCommand = {
  id: 'cmd-exec-1',
  timestampUtc: new Date('2026-06-02T10:01:00.000Z').toISOString(),
  executionType: 'command' as const,
  finalStatus: 'success' as const,
  childCount: 1,
  objectRef: { objectType: 'command', objectId: 'cmd-1', displayNameSnapshot: 'Detailed Command' },
  summary: 'Detailed Command success'
};

describe('ExecutionLogsPage stand-alone command', () => {
  beforeEach(() => {
    jest.resetAllMocks();
    listExecutionLogsMock.mockResolvedValue({ items: [leafCommand, detailedCommand], nextPageToken: undefined });
    getExecutionSubtreeMock.mockResolvedValue({
      executionId: 'cmd-exec-1',
      finalStatus: 'success',
      root: {
        nodeKind: 'command',
        executionId: 'cmd-exec-1',
        order: 0,
        label: 'Detailed Command',
        status: 'success',
        children: [
          { nodeKind: 'tap', order: 1, label: 'Tap target', status: 'success', message: 'Tapped at the expected location.', children: [] }
        ]
      }
    });
  });

  it('shows a childless stand-alone command as a non-expandable leaf row', async () => {
    render(<ExecutionLogsPage />);

    const row = (await screen.findByText('Solo Command')).closest('tr') as HTMLElement;
    expect(within(row).queryByRole('button')).not.toBeInTheDocument();
  });

  it('surfaces full command detail via row expansion in the grid', async () => {
    render(<ExecutionLogsPage />);

    const row = (await screen.findByText('Detailed Command')).closest('tr') as HTMLElement;
    fireEvent.click(within(row).getByRole('button', { name: 'Expand sub-elements' }));

    expect(await screen.findByText('Tap target')).toBeInTheDocument();
    expect(screen.getByText(/Tapped at the expected location\./)).toBeInTheDocument();
  });
});
