import React from 'react';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { ExecutionLogsPage } from '../ExecutionLogs';
import { getExecutionSubtree, listExecutionLogs } from '../../services/executionLogsApi';

jest.mock('../../services/executionLogsApi');

const listExecutionLogsMock = listExecutionLogs as jest.MockedFunction<typeof listExecutionLogs>;
const getExecutionSubtreeMock = getExecutionSubtree as jest.MockedFunction<typeof getExecutionSubtree>;

const sequenceItem = {
  id: 'seq-exec-1',
  timestampUtc: new Date('2026-06-02T10:00:00.000Z').toISOString(),
  executionType: 'sequence' as const,
  finalStatus: 'success' as const,
  childCount: 1,
  objectRef: { objectType: 'sequence', objectId: 'seq-1', displayNameSnapshot: 'My Sequence' },
  summary: "Sequence 'My Sequence' success with 1 step executed."
};

const commandItem = {
  id: 'cmd-exec-1',
  timestampUtc: new Date('2026-06-02T10:01:00.000Z').toISOString(),
  executionType: 'command' as const,
  finalStatus: 'success' as const,
  childCount: 1,
  objectRef: { objectType: 'command', objectId: 'cmd-1', displayNameSnapshot: 'Standalone Cmd' },
  summary: "Command 'Standalone Cmd' success."
};

const sequenceSubtree = {
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
        timestampUtc: new Date('2026-06-02T10:00:30.000Z').toISOString(),
        children: [
          { nodeKind: 'tap' as const, order: 1, label: 'Tap target', status: 'success' as const, children: [] }
        ]
      }
    ]
  }
};

const commandSubtree = {
  executionId: 'cmd-exec-1',
  finalStatus: 'success' as const,
  root: {
    nodeKind: 'command' as const,
    executionId: 'cmd-exec-1',
    order: 0,
    label: 'Standalone Cmd',
    status: 'success' as const,
    children: [
      { nodeKind: 'tap' as const, order: 1, label: 'Cmd Tap', status: 'success' as const, children: [] }
    ]
  }
};

const rowOf = (name: string) => screen.getByText(name).closest('tr') as HTMLElement;
const expand = (name: string) =>
  fireEvent.click(within(rowOf(name)).getByRole('button', { name: 'Expand sub-elements' }));
const collapse = (name: string) =>
  fireEvent.click(within(rowOf(name)).getByRole('button', { name: 'Collapse sub-elements' }));

describe('ExecutionLogsPage grid', () => {
  beforeEach(() => {
    jest.resetAllMocks();
    listExecutionLogsMock.mockResolvedValue({ items: [sequenceItem, commandItem], nextPageToken: undefined });
    getExecutionSubtreeMock.mockImplementation((id: string) =>
      Promise.resolve(id === 'seq-exec-1' ? sequenceSubtree : commandSubtree)
    );
  });

  // --- US1: single full-width grid replaces the split list/detail layout ---

  it('renders a single grid with the six columns and no separate detail panel', async () => {
    render(<ExecutionLogsPage />);
    await screen.findByText('My Sequence');

    const headers = screen.getAllByRole('columnheader');
    expect(headers).toHaveLength(6);
    expect(screen.getByRole('button', { name: 'Timestamp' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Name' })).toBeInTheDocument();
    expect(screen.getByText('Type')).toBeInTheDocument();
    expect(screen.getByText('Additional information')).toBeInTheDocument();

    // The former Execution Detail panel is gone.
    expect(screen.queryByText('Execution details')).not.toBeInTheDocument();
    expect(screen.queryByText('Related objects')).not.toBeInTheDocument();
  });

  it('shows top-level type, status and summary in the additional-information column', async () => {
    render(<ExecutionLogsPage />);
    await screen.findByText('My Sequence');

    const row = rowOf('My Sequence');
    expect(within(row).getByText('Sequence')).toBeInTheDocument();
    expect(within(row).getByText('success')).toBeInTheDocument();
    expect(within(row).getByText("Sequence 'My Sequence' success with 1 step executed.")).toBeInTheDocument();
  });

  // --- US2: expandable commands and multi-level independent expansion ---

  it('expands a command step to a second level only after the step is expanded', async () => {
    render(<ExecutionLogsPage />);
    await screen.findByText('My Sequence');

    expand('My Sequence');
    await waitFor(() => expect(getExecutionSubtreeMock).toHaveBeenCalledWith('seq-exec-1'));
    expect(await screen.findByText('Open Mail')).toBeInTheDocument();
    // The command's own child is not visible until the command step is expanded.
    expect(screen.queryByText('Tap target')).not.toBeInTheDocument();

    expand('Open Mail');
    expect(await screen.findByText('Tap target')).toBeInTheDocument();
  });

  it('makes a stand-alone command expandable and a leaf tap non-expandable', async () => {
    render(<ExecutionLogsPage />);
    await screen.findByText('Standalone Cmd');

    expand('Standalone Cmd');
    expect(await screen.findByText('Cmd Tap')).toBeInTheDocument();
    // A leaf node (tap) exposes no expand control.
    expect(within(rowOf('Cmd Tap')).queryByRole('button')).not.toBeInTheDocument();
  });

  it('expands multiple top-level rows independently', async () => {
    render(<ExecutionLogsPage />);
    await screen.findByText('My Sequence');

    expand('My Sequence');
    await screen.findByText('Open Mail');
    expand('Standalone Cmd');
    await screen.findByText('Cmd Tap');

    // Both branches are open at once.
    expect(screen.getByText('Open Mail')).toBeInTheDocument();
    expect(screen.getByText('Cmd Tap')).toBeInTheDocument();

    // Collapsing one leaves the other expanded.
    collapse('My Sequence');
    expect(screen.queryByText('Open Mail')).not.toBeInTheDocument();
    expect(screen.getByText('Cmd Tap')).toBeInTheDocument();
  });

  it('shows the execution timestamp on a sequence/command sub-element row', async () => {
    render(<ExecutionLogsPage />);
    await screen.findByText('My Sequence');

    expand('My Sequence');
    await screen.findByText('Open Mail');

    // The command sub-element carries its own recorded execution time, formatted like top-level rows.
    const expected = new Date('2026-06-02T10:00:30.000Z').toLocaleString();
    expect(within(rowOf('Open Mail')).getByText(expected)).toBeInTheDocument();

    // A primitive leaf step has no recorded time, so its timestamp cell stays blank.
    expand('Open Mail');
    await screen.findByText('Tap target');
    const tapCells = rowOf('Tap target').querySelectorAll('td');
    expect(tapCells[1].textContent).toBe('');
  });

  // --- US3: "Open in sequence" buttons removed ---

  it('renders no "Open in sequence" buttons anywhere', async () => {
    render(<ExecutionLogsPage />);
    await screen.findByText('My Sequence');

    expand('My Sequence');
    await screen.findByText('Open Mail');
    expand('Open Mail');
    await screen.findByText('Tap target');

    expect(screen.queryByText('Open in sequence')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Open in sequence' })).not.toBeInTheDocument();
  });
});
