import React from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { ExecutionLogsPage } from '../ExecutionLogs';
import { listExecutionLogs } from '../../services/executionLogsApi';

jest.mock('../../services/executionLogsApi');

const listExecutionLogsMock = listExecutionLogs as jest.MockedFunction<typeof listExecutionLogs>;

const createListItem = (id: string, name: string, status: string) => ({
  id,
  timestampUtc: '2026-03-02T11:57:00.000Z',
  executionType: 'command' as const,
  finalStatus: status as 'running' | 'success' | 'failure',
  childCount: 0,
  objectRef: {
    objectType: 'command',
    objectId: id,
    displayNameSnapshot: name
  },
  summary: `${name} ${status}`
});

describe('ExecutionLogsPage layout', () => {
  beforeEach(() => {
    jest.resetAllMocks();
    listExecutionLogsMock.mockResolvedValue({
      items: [createListItem('id-1', 'Alpha Command', 'success')],
      nextPageToken: undefined
    });
    jest.spyOn(console, 'error').mockImplementation(() => undefined);
  });

  afterEach(() => {
    jest.restoreAllMocks();
  });

  it('renders a single full-width grid with no separate detail panel at any width', async () => {
    const { container } = render(<ExecutionLogsPage />);

    expect(await screen.findByText('Alpha Command')).toBeInTheDocument();
    expect(screen.getByLabelText('Execution logs list')).toBeInTheDocument();
    // No detail panel and no phone drill-down "back to list" affordance.
    expect(screen.queryByLabelText('Execution log detail')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /back to list/i })).not.toBeInTheDocument();
    // The grid lives inside a horizontally scrollable container.
    expect(container.querySelector('.execution-logs-scroll')).not.toBeNull();
  });

  it('preserves filter and timestamp-mode state without a separate detail screen', async () => {
    render(<ExecutionLogsPage />);

    await waitFor(() => expect(listExecutionLogsMock).toHaveBeenCalled());
    fireEvent.change(screen.getByLabelText('Object name'), { target: { value: 'alpha' } });
    fireEvent.change(screen.getByLabelText('Timestamp display'), { target: { value: 'relative' } });

    await waitFor(() => expect(screen.getByDisplayValue('alpha')).toBeInTheDocument());
    await waitFor(() => expect(screen.getByText('Alpha Command')).toBeInTheDocument());

    // Clicking a row does not navigate away or open a panel; filter/sort state persists.
    fireEvent.click(screen.getByText('Alpha Command'));
    expect(screen.queryByRole('button', { name: /back to list/i })).not.toBeInTheDocument();
    expect(screen.getByDisplayValue('alpha')).toBeInTheDocument();
    expect(screen.getByText(/ago/)).toBeInTheDocument();
    await waitFor(() => expect(listExecutionLogsMock).toHaveBeenLastCalledWith(expect.objectContaining({
      filterObjectName: 'alpha'
    })));
  });
});
