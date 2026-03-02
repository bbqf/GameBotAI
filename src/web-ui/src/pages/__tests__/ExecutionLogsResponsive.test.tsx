import React from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { ExecutionLogsPage } from '../ExecutionLogs';
import { getExecutionLogDetail, listExecutionLogs } from '../../services/executionLogsApi';
import { useNavigationCollapse } from '../../hooks/useNavigationCollapse';

jest.mock('../../services/executionLogsApi');
jest.mock('../../hooks/useNavigationCollapse', () => ({
  useNavigationCollapse: jest.fn()
}));

const listExecutionLogsMock = listExecutionLogs as jest.MockedFunction<typeof listExecutionLogs>;
const getExecutionLogDetailMock = getExecutionLogDetail as jest.MockedFunction<typeof getExecutionLogDetail>;

const createListItem = (id: string, name: string, status: string) => ({
  id,
  timestampUtc: '2026-03-02T11:57:00.000Z',
  executionType: 'command',
  finalStatus: status,
  objectRef: {
    objectType: 'command',
    objectId: id,
    displayNameSnapshot: name
  },
  summary: `${name} ${status}`
});

describe('ExecutionLogsPage responsive behavior', () => {
  const useNavigationCollapseMock = useNavigationCollapse as jest.MockedFunction<typeof useNavigationCollapse>;

  beforeEach(() => {
    jest.resetAllMocks();
    useNavigationCollapseMock.mockReturnValue({ isCollapsed: false });
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

    jest.spyOn(console, 'error').mockImplementation(() => undefined);
  });

  afterEach(() => {
    jest.restoreAllMocks();
  });

  it('shows split list/detail layout on desktop widths', async () => {
    render(<ExecutionLogsPage />);

    expect(await screen.findByText('Alpha Command')).toBeInTheDocument();
    expect(screen.getByLabelText('Execution logs list')).toBeInTheDocument();
    expect(screen.getByLabelText('Execution log detail')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /back to list/i })).not.toBeInTheDocument();
  });

  it('shows drill-down detail flow on phone widths and preserves filter/sort state when returning', async () => {
    useNavigationCollapseMock.mockReturnValue({ isCollapsed: true });

    render(<ExecutionLogsPage />);

    await waitFor(() => expect(listExecutionLogsMock).toHaveBeenCalled());
    fireEvent.change(screen.getByLabelText('Object name'), { target: { value: 'alpha' } });
    fireEvent.change(screen.getByLabelText('Timestamp display'), { target: { value: 'relative' } });

    listExecutionLogsMock.mockResolvedValueOnce({
      items: [createListItem('id-1', 'Alpha Command', 'success')],
      nextPageToken: undefined
    });
    await waitFor(() => expect(screen.getByDisplayValue('alpha')).toBeInTheDocument());
    await waitFor(() => expect(screen.getByText('Alpha Command')).toBeInTheDocument());

    fireEvent.click(screen.getByText('Alpha Command'));
    expect(await screen.findByRole('button', { name: /back to list/i })).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: /back to list/i }));

    expect(screen.getByDisplayValue('alpha')).toBeInTheDocument();
    expect(screen.getByText(/ago/)).toBeInTheDocument();
    expect(listExecutionLogsMock).toHaveBeenLastCalledWith(expect.objectContaining({
      filterObjectName: 'alpha'
    }));
  });
});
