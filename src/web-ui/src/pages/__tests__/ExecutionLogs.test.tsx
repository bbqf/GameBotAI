import React from 'react';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { ExecutionLogsPage } from '../ExecutionLogs';
import { getExecutionSubtree, listExecutionLogs, ExecutionTreeNodeStatus } from '../../services/executionLogsApi';

jest.mock('../../services/executionLogsApi');

let nowSpy: jest.SpyInstance<number, []>;

const listExecutionLogsMock = listExecutionLogs as jest.MockedFunction<typeof listExecutionLogs>;
const getExecutionSubtreeMock = getExecutionSubtree as jest.MockedFunction<typeof getExecutionSubtree>;

const createListItem = (id: string, name: string, status: string, childCount = 0) => ({
  id,
  timestampUtc: new Date().toISOString(),
  executionType: 'command' as const,
  finalStatus: status as 'running' | 'success' | 'failure',
  childCount,
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
  });

  afterEach(() => {
    nowSpy.mockRestore();
  });

  it('loads default columns and default sort', async () => {
    render(<ExecutionLogsPage />);

    expect(await screen.findByText('Alpha Command')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Timestamp' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Name' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Status' })).toBeInTheDocument();
    expect(screen.getByText('Type')).toBeInTheDocument();
    expect(screen.getByText('Additional information')).toBeInTheDocument();

    await waitFor(() => expect(listExecutionLogsMock).toHaveBeenCalledWith(expect.objectContaining({
      sortBy: 'timestamp',
      sortDirection: 'desc',
      pageSize: 50
    })));
  }, 15000);

  it('updates sorting when the Name header is clicked', async () => {
    render(<ExecutionLogsPage />);

    await screen.findByText('Alpha Command');
    fireEvent.click(screen.getByRole('button', { name: 'Name' }));

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

  it('shows sub-element detail in plain language without raw JSON blocks', async () => {
    listExecutionLogsMock.mockResolvedValue({
      items: [createListItem('id-1', 'Alpha Command', 'success', 1)],
      nextPageToken: undefined
    });
    getExecutionSubtreeMock.mockResolvedValue({
      executionId: 'id-1',
      finalStatus: 'success',
      root: {
        nodeKind: 'command',
        executionId: 'id-1',
        order: 0,
        label: 'Alpha Command',
        status: 'success',
        children: [
          { nodeKind: 'tap', order: 1, label: 'Tap target', status: 'success', message: 'Tapped at the expected location.', children: [] }
        ]
      }
    });

    const { container } = render(<ExecutionLogsPage />);

    await screen.findByText('Alpha Command');
    fireEvent.click(screen.getByRole('button', { name: 'Expand sub-elements' }));

    expect(await screen.findByText('Tap target')).toBeInTheDocument();
    expect(screen.getByText(/Tapped at the expected location\./)).toBeInTheDocument();
    expect(container.querySelector('pre.json')).toBeNull();
    expect(screen.queryByText(/"executionId"/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/"stepOutcomes"/i)).not.toBeInTheDocument();
  });

  it('renders wait-for-image parameters and exit condition in the additional-information column', async () => {
    listExecutionLogsMock.mockResolvedValue({
      items: [createListItem('id-1', 'Alpha Command', 'success', 1)],
      nextPageToken: undefined
    });
    getExecutionSubtreeMock.mockResolvedValue({
      executionId: 'id-1',
      finalStatus: 'success',
      root: {
        nodeKind: 'command',
        executionId: 'id-1',
        order: 0,
        label: 'Alpha Command',
        status: 'success',
        children: [
          {
            nodeKind: 'wait',
            order: 1,
            label: 'waitForImage',
            status: 'completed_timeout' as unknown as ExecutionTreeNodeStatus,
            message: 'timeout_elapsed',
            detailAttributes: {
              timeoutMs: 1500,
              effectiveTimeoutMs: 1500,
              referenceImageId: 'mail_icon',
              confidence: 0.92,
              exitCondition: 'timeout_elapsed',
              imageLoadStatus: 'loaded'
            },
            children: []
          }
        ]
      }
    });

    render(<ExecutionLogsPage />);

    await screen.findByText('Alpha Command');
    fireEvent.click(screen.getByRole('button', { name: 'Expand sub-elements' }));

    const waitRow = (await screen.findByText('waitForImage')).closest('tr') as HTMLElement;
    expect(within(waitRow).getByText(/Wait settings: timeout 1500 ms, effective timeout 1500 ms\./)).toBeInTheDocument();
    expect(within(waitRow).getByText(/Image: mail_icon; confidence 0.92; load status loaded\./)).toBeInTheDocument();
    expect(within(waitRow).getByText(/Exit condition: Timeout elapsed\./)).toBeInTheDocument();
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

    expect(await screen.findByText('Alpha Command')).toBeInTheDocument();
    expect(screen.getByText(new Date('2026-03-02T11:57:00.000Z').toLocaleString())).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText('Timestamp display'), { target: { value: 'relative' } });
    expect(await screen.findByText('3m ago')).toBeInTheDocument();
  });
});
