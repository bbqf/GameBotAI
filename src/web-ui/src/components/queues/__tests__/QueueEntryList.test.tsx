import React from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { QueueEntryList } from '../QueueEntryList';
import { QueueEntryDto } from '../../../services/queues';
import { SequenceDto } from '../../../services/sequences';

const sequences = [
  { id: 'seq-a', name: 'Alpha', steps: [] },
  { id: 'seq-b', name: 'Bravo', steps: [] },
] as unknown as SequenceDto[];

describe('QueueEntryList', () => {
  it('renders entries in order with a stale badge for unresolved references', () => {
    const entries: QueueEntryDto[] = [
      { entryId: 'e1', sequenceId: 'seq-a', sequenceName: 'Alpha', stale: false },
      { entryId: 'e2', sequenceId: 'seq-gone', sequenceName: null, stale: true },
    ];
    render(<QueueEntryList entries={entries} sequences={sequences} onAdd={jest.fn()} onRemove={jest.fn()} />);

    const rows = screen.getAllByTestId('queue-entry');
    expect(rows).toHaveLength(2);
    expect(rows[0]).toHaveTextContent('Alpha');
    expect(rows[1]).toHaveTextContent('(stale)');
  });

  it('adds the selected sequence and clears selection', () => {
    const onAdd = jest.fn();
    render(<QueueEntryList entries={[]} sequences={sequences} onAdd={onAdd} onRemove={jest.fn()} />);

    fireEvent.change(screen.getByLabelText('Add sequence'), { target: { value: 'seq-b' } });
    fireEvent.click(screen.getByText('Add'));
    expect(onAdd).toHaveBeenCalledWith('seq-b');
  });

  it('removes an entry', () => {
    const onRemove = jest.fn();
    const entries: QueueEntryDto[] = [{ entryId: 'e1', sequenceId: 'seq-a', sequenceName: 'Alpha', stale: false }];
    render(<QueueEntryList entries={entries} sequences={sequences} onAdd={jest.fn()} onRemove={onRemove} />);

    fireEvent.click(screen.getByLabelText('Remove Alpha'));
    expect(onRemove).toHaveBeenCalledWith('e1');
  });

  it('shows an empty hint when there are no entries', () => {
    render(<QueueEntryList entries={[]} sequences={sequences} onAdd={jest.fn()} onRemove={jest.fn()} />);
    expect(screen.getByText('No sequences in this queue yet.')).toBeInTheDocument();
  });
});
