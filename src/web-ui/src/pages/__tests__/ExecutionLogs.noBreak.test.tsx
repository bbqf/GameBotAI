import React from 'react';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { ExecutionLogsPage } from '../ExecutionLogs';
import {
  ExecutionSubtreeResponseDto,
  getExecutionSubtree,
  listExecutionLogs
} from '../../services/executionLogsApi';

jest.mock('../../services/executionLogsApi');

const listExecutionLogsMock = listExecutionLogs as jest.MockedFunction<typeof listExecutionLogs>;
const getExecutionSubtreeMock = getExecutionSubtree as jest.MockedFunction<typeof getExecutionSubtree>;

// A run whose loop contains a break that never fired: its only non-success break outcome is
// no_break. The run/loop themselves stay success (FR-004/FR-005).
const sequenceItem = {
  id: 'seq-exec-1',
  timestampUtc: new Date('2026-07-01T10:00:00.000Z').toISOString(),
  executionType: 'sequence' as const,
  finalStatus: 'success' as const,
  childCount: 1,
  objectRef: { objectType: 'sequence', objectId: 'seq-1', displayNameSnapshot: 'Loop Sequence' },
  summary: "Sequence 'Loop Sequence' success."
};

const sequenceSubtree: ExecutionSubtreeResponseDto = {
  executionId: 'seq-exec-1',
  finalStatus: 'success',
  root: {
    nodeKind: 'sequence',
    executionId: 'seq-exec-1',
    order: 0,
    label: 'Loop Sequence',
    status: 'success',
    children: [
      {
        nodeKind: 'loop',
        order: 1,
        label: 'Farm loop',
        status: 'success',
        children: [
          { nodeKind: 'step', order: 1, label: 'Break check', status: 'no_break', children: [] }
        ]
      }
    ]
  }
};

const rowOf = (name: string) => screen.getByText(name).closest('tr') as HTMLElement;
const expand = (name: string) =>
  fireEvent.click(within(rowOf(name)).getByRole('button', { name: 'Expand sub-elements' }));

describe('ExecutionLogs no_break rendering', () => {
  beforeEach(() => {
    jest.resetAllMocks();
    listExecutionLogsMock.mockResolvedValue({ items: [sequenceItem], nextPageToken: undefined });
    getExecutionSubtreeMock.mockResolvedValue(sequenceSubtree);
  });

  // T007 (US1): the grid renders a distinct neutral "No break" badge for a no_break node.
  it('renders a distinct neutral "No break" badge, not failure or skipped', async () => {
    render(<ExecutionLogsPage />);
    await screen.findByText('Loop Sequence');

    expand('Loop Sequence');
    await waitFor(() => expect(getExecutionSubtreeMock).toHaveBeenCalledWith('seq-exec-1'));
    await screen.findByText('Farm loop');
    expand('Farm loop');

    const breakRow = await screen.findByText('No break');
    const row = breakRow.closest('tr') as HTMLElement;

    // Distinct neutral status, carried on the row for styling — not red failure, not skipped.
    expect(row.getAttribute('data-status')).toBe('no_break');
    expect(row.getAttribute('data-status')).not.toBe('failure');
    expect(row.getAttribute('data-status')).not.toBe('skipped');

    // Readable label rather than the raw token.
    expect(within(row).getByText('No break')).toBeInTheDocument();
  });

  // T018 (US2): a run whose only non-success break outcome is no_break renders as Succeeded.
  it('keeps the run/loop rows non-failed when the only break outcome is no_break', async () => {
    render(<ExecutionLogsPage />);
    await screen.findByText('Loop Sequence');

    // Run row stays success.
    expect(rowOf('Loop Sequence').getAttribute('data-status')).toBe('success');

    expand('Loop Sequence');
    await waitFor(() => expect(getExecutionSubtreeMock).toHaveBeenCalledWith('seq-exec-1'));

    // Loop row stays success (not marked failure by the non-firing break).
    const loopRow = (await screen.findByText('Farm loop')).closest('tr') as HTMLElement;
    expect(loopRow.getAttribute('data-status')).toBe('success');
    expect(loopRow.getAttribute('data-status')).not.toBe('failure');
  });
});
