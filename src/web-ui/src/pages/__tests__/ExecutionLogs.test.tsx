import React from 'react';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { ExecutionLogsPage } from '../ExecutionLogs';
import { getExecutionLogDetail, listExecutionLogs } from '../../services/executionLogsApi';

jest.mock('../../services/executionLogsApi');
jest.mock('../../hooks/useNavigationCollapse', () => ({
  useNavigationCollapse: () => ({ isCollapsed: false })
}));

let nowSpy: jest.SpyInstance<number, []>;

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
    nowSpy = jest.spyOn(Date, 'now').mockReturnValue(new Date('2026-03-02T12:00:00.000Z').getTime());
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

  afterEach(() => {
    nowSpy.mockRestore();
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

  it('renders details in plain language without raw JSON blocks', async () => {
    getExecutionLogDetailMock.mockResolvedValueOnce({
      executionId: 'id-1',
      summary: 'The command finished successfully.',
      relatedObjects: [
        { label: 'Farm Command', targetType: 'command', targetId: 'cmd-1', isAvailable: true }
      ],
      snapshot: { isAvailable: true, caption: 'Snapshot captured during execution.' },
      stepOutcomes: [
        { stepName: 'Tap target', status: 'success', message: 'Tapped at the expected location.' }
      ]
    });

    const { container } = render(<ExecutionLogsPage />);

    expect(await screen.findByText('The command finished successfully.')).toBeInTheDocument();
    expect(screen.getByText(/Tap target/)).toBeInTheDocument();
    expect(screen.getByText(/Tapped at the expected location./)).toBeInTheDocument();
    expect(container.querySelector('pre.json')).toBeNull();
    expect(screen.queryByText(/"executionId"/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/"stepOutcomes"/i)).not.toBeInTheDocument();
  });

  it('defaults to exact local timestamp and switches to relative mode', async () => {
    listExecutionLogsMock.mockResolvedValueOnce({
      items: [
        {
          ...createListItem('id-1', 'Alpha Command', 'success'),
          timestampUtc: '2026-03-02T11:57:00.000Z'
        }
      ]
    });

    render(<ExecutionLogsPage />);

    expect(await screen.findByText(/Alpha Command/)).toBeInTheDocument();
    expect(screen.getByText(new Date('2026-03-02T11:57:00.000Z').toLocaleString())).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText('Timestamp display'), { target: { value: 'relative' } });
    expect(await screen.findByText('3m ago')).toBeInTheDocument();
  });
});
