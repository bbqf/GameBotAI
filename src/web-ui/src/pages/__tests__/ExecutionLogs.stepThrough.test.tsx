import React from 'react';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { ExecutionLogsPage } from '../ExecutionLogs';
import { listExecutionLogs } from '../../services/executionLogsApi';
import { projectEntryRow } from '../executionLogGrid';

jest.mock('../../services/executionLogsApi');

const listExecutionLogsMock = listExecutionLogs as jest.MockedFunction<typeof listExecutionLogs>;

const entryOf = (id: string, name: string, origin?: string) => ({
  id,
  timestampUtc: new Date('2026-10-02T10:00:00.000Z').toISOString(),
  executionType: 'sequence' as const,
  finalStatus: 'success' as const,
  childCount: 0,
  objectRef: { objectType: 'sequence', objectId: `seq-${id}`, displayNameSnapshot: name },
  summary: `Sequence '${name}' success.`,
  origin
});

describe('ExecutionLogs step-through origin', () => {
  beforeEach(() => {
    jest.resetAllMocks();
    listExecutionLogsMock.mockResolvedValue({
      items: [entryOf('1', 'Real run'), entryOf('2', 'Stepped run', 'step-through')],
      nextPageToken: undefined
    });
  });

  it('shows the step-through badge only on a step-through run', async () => {
    render(<ExecutionLogsPage />);
    await screen.findByText('Real run');

    const stepped = screen.getByText('Stepped run').closest('tr') as HTMLElement;
    const real = screen.getByText('Real run').closest('tr') as HTMLElement;
    expect(within(stepped).getByText('step-through')).toBeInTheDocument();
    expect(within(real).queryByText('step-through')).toBeNull();
  });

  it('filters by origin and sends the origin to the API', async () => {
    render(<ExecutionLogsPage />);
    await screen.findByText('Real run');

    fireEvent.change(screen.getByLabelText('Origin'), { target: { value: 'step-through' } });

    await waitFor(() => expect(listExecutionLogsMock).toHaveBeenLastCalledWith(expect.objectContaining({ origin: 'step-through' })));
  });

  it('sends no origin for all runs', async () => {
    render(<ExecutionLogsPage />);
    await screen.findByText('Real run');

    expect(listExecutionLogsMock.mock.calls[0][0]).toEqual(expect.objectContaining({ origin: undefined }));
  });

  it('projects the origin onto the grid row', () => {
    expect(projectEntryRow(entryOf('3', 'x', 'step-through'), 't').origin).toBe('step-through');
    expect(projectEntryRow(entryOf('4', 'y'), 't').origin).toBeUndefined();
  });
});
