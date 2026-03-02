import React from 'react';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { ExecutionLogsPage } from '../ExecutionLogs';
import { getExecutionLogDetail, listExecutionLogs } from '../../services/executionLogsApi';

jest.mock('../../services/executionLogsApi');
jest.mock('../../hooks/useNavigationCollapse', () => ({
  useNavigationCollapse: () => ({ isCollapsed: false })
}));

const listExecutionLogsMock = listExecutionLogs as jest.MockedFunction<typeof listExecutionLogs>;
const getExecutionLogDetailMock = getExecutionLogDetail as jest.MockedFunction<typeof getExecutionLogDetail>;

const createListItem = (id: string, name: string, status: string) => ({
  id,
  timestampUtc: new Date().toISOString(),
  executionType: 'command',
  finalStatus: status,
  objectRef: {
    objectType: 'command',
    objectId: id,
    displayNameSnapshot: name
  },
  summary: `${name} ${status}`
});

describe('ExecutionLogsPage', () => {
  beforeEach(() => {
    jest.resetAllMocks();
    listExecutionLogsMock.mockResolvedValue({
      items: [createListItem('id-1', 'Alpha Command', 'success')],
      nextPageToken: undefined
    });
    getExecutionLogDetailMock.mockResolvedValue({
      executionId: 'id-1',
      summary: 'Command completed successfully',
      relatedObjects: [],
      snapshot: { isAvailable: false },
      stepOutcomes: []
    });
  });

  it('loads default columns and default sort', async () => {
    render(<ExecutionLogsPage />);

    expect(await screen.findByText('Alpha Command')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Timestamp' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Object Name' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Status' })).toBeInTheDocument();

    await waitFor(() => expect(listExecutionLogsMock).toHaveBeenCalledWith(expect.objectContaining({
      sortBy: 'timestamp',
      sortDirection: 'desc',
      pageSize: 50
    })));
  });

  it('updates sorting when header is clicked', async () => {
    render(<ExecutionLogsPage />);

    await screen.findByText('Alpha Command');
    fireEvent.click(screen.getByRole('button', { name: 'Object Name' }));

    await waitFor(() => expect(listExecutionLogsMock).toHaveBeenCalledWith(expect.objectContaining({
      sortBy: 'objectName',
      sortDirection: 'asc'
    })));
  });

  it('uses latest-request-wins behavior for rapid filter updates', async () => {
    let firstResolve: ((value: any) => void) | undefined;
    const first = new Promise((resolve) => {
      firstResolve = resolve;
    });

    listExecutionLogsMock.mockImplementation(((query?: any) => {
      if (!query?.filterObjectName) {
        return first as any;
      }
      if (query.filterObjectName === 'bravo') {
        return Promise.resolve({ items: [createListItem('id-2', 'Bravo Command', 'failure')] }) as any;
      }
      return Promise.resolve({ items: [createListItem('id-1', 'Alpha Command', 'success')] }) as any;
    }) as any);

    render(<ExecutionLogsPage />);

    fireEvent.change(screen.getByLabelText('Object name'), { target: { value: 'a' } });
    fireEvent.change(screen.getByLabelText('Object name'), { target: { value: 'bravo' } });

    await screen.findByText('Bravo Command');

    await act(async () => {
      firstResolve?.({ items: [createListItem('id-stale', 'Stale Command', 'success')] });
    });

    expect(screen.queryByText('Stale Command')).not.toBeInTheDocument();
  });
});
