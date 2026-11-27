import React from 'react';
import { fireEvent, render, screen, within } from '@testing-library/react';
import { ListView } from '../ListView';
import type { ListColumn, RowActions } from '../types';
import { StatusChip } from '../StatusChip';

type Row = { id: string; name: string; note: string };

const rows: Row[] = [
  { id: '1', name: 'Alpha', note: 'first' },
  { id: '2', name: 'Beta', note: '' },
];

const columns: ListColumn<Row>[] = [
  { id: 'name', header: 'Name', width: 'flex', render: (r) => r.name, text: (r) => r.name },
  { id: 'note', header: 'Note', width: 120, render: (r) => r.note, text: (r) => r.note, cellTestId: 'note-cell' },
];

const actionsFor = (onSelect = jest.fn()): ((row: Row) => RowActions) => (row) => ({
  visible: [
    { id: 'play', label: 'Start', ariaLabel: `Start ${row.name}`, icon: 'play', onSelect },
    { id: 'stop', label: 'Stop', ariaLabel: `Stop ${row.name}`, icon: 'stop', disabled: true, onSelect },
  ],
  menu: [{ id: 'del', label: 'Delete', ariaLabel: `Delete ${row.name}`, icon: 'delete', tone: 'danger', onSelect }],
});

describe('ListView desktop', () => {
  const renderList = (extra: Partial<React.ComponentProps<typeof ListView<Row>>> = {}) =>
    render(
      <ListView<Row>
        mode="desktop"
        ariaLabel="Items table"
        columns={columns}
        rows={rows}
        getRowKey={(r) => r.id}
        {...extra}
      />
    );

  it('marks the selected row', () => {
    render(<ListView<Row> mode="desktop" selectedRowKey="2" ariaLabel="Items table" columns={columns} rows={rows} getRowKey={(r) => r.id} />);
    const [, first, second] = screen.getAllByRole('row');
    expect(first).not.toHaveClass('ui-list__row--selected');
    expect(first).toHaveAttribute('aria-selected', 'false');
    expect(second).toHaveClass('ui-list__row--selected');
    expect(second).toHaveAttribute('aria-selected', 'true');
  });

  it('does not show the secondary columns in compact mode', () => {
    const withSecondary: ListColumn<Row>[] = [columns[0], { ...columns[1], secondary: true }];
    const { container, rerender } = render(
      <ListView<Row> mode="desktop" ariaLabel="Items table" columns={withSecondary} rows={rows} getRowKey={(r) => r.id} />
    );
    expect(screen.getByRole('columnheader', { name: 'Note' })).toBeInTheDocument();

    rerender(
      <ListView<Row> mode="desktop" compact ariaLabel="Items table" columns={withSecondary} rows={rows} getRowKey={(r) => r.id} />
    );
    expect(screen.queryByRole('columnheader', { name: 'Note' })).toBeNull();
    expect(container.querySelectorAll('col')).toHaveLength(1);
    expect(screen.queryAllByTestId('note-cell')).toHaveLength(0);
  });

  it('sets a col width for each fixed column and none for the flexible column', () => {
    const { container } = renderList({ rowActions: actionsFor(), actionsColumn: { header: 'Actions', width: 184 } });
    const cols = Array.from(container.querySelectorAll('col'));
    expect(cols).toHaveLength(3);
    expect(cols[0].style.width).toBe('');
    expect(cols[1].style.width).toBe('120px');
    expect(cols[2].style.width).toBe('184px');
    expect(screen.getByRole('table', { name: 'Items table' }).style.minWidth).toBe(`${120 + 184 + 160}px`);
  });

  it('shows a dash for empty text and a title for each cell', () => {
    renderList();
    const cells = screen.getAllByTestId('note-cell');
    expect(cells[0]).toHaveAttribute('title', 'first');
    expect(cells[1]).toHaveTextContent('—');
    expect(cells[1]).not.toHaveAttribute('title');
  });

  it('calls onRowClick for a click on the row and for the open button', () => {
    const onRowClick = jest.fn();
    renderList({ onRowClick });
    fireEvent.click(screen.getByText('first'));
    expect(onRowClick).toHaveBeenCalledWith(rows[0]);
    fireEvent.click(screen.getByRole('button', { name: 'Beta' }));
    expect(onRowClick).toHaveBeenCalledTimes(2);
    expect(onRowClick).toHaveBeenLastCalledWith(rows[1]);
  });

  it('does not call onRowClick for a click in the actions cell', () => {
    const onRowClick = jest.fn();
    const onSelect = jest.fn();
    renderList({ onRowClick, rowActions: actionsFor(onSelect) });
    fireEvent.click(screen.getByRole('button', { name: 'Start Alpha' }));
    expect(onSelect).toHaveBeenCalledTimes(1);
    expect(onRowClick).not.toHaveBeenCalled();
    expect(screen.getByRole('button', { name: 'Stop Alpha' })).toBeDisabled();
  });

  it('uses the open label and the menu label when given', () => {
    renderList({
      onRowClick: jest.fn(),
      rowActions: actionsFor(),
      openLabel: (r) => `Open ${r.name} now`,
      menuLabel: (r) => `More actions for ${r.name}`,
    });
    expect(screen.getByRole('button', { name: 'Open Alpha now' })).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'More actions for Alpha' }));
    expect(screen.getByRole('menuitem', { name: 'Delete' })).toBeInTheDocument();
  });

  it('uses render props for the actions', () => {
    renderList({ renderActions: (r) => <span>custom {r.name}</span> });
    expect(screen.getByText('custom Alpha')).toBeInTheDocument();
    expect(screen.getByRole('columnheader', { name: 'Actions' })).toBeInTheDocument();
  });

  it('aligns an end column', () => {
    renderList({ columns: [{ ...columns[0] }, { ...columns[1], align: 'end' }] });
    expect(screen.getByRole('columnheader', { name: 'Note' })).toHaveClass('ui-list__cell--end');
  });

  it('shows the loading state', () => {
    renderList({ state: { kind: 'loading' } });
    expect(screen.getByText('Loading…')).toBeInTheDocument();
    expect(screen.queryByText('Alpha')).toBeNull();
  });

  it('shows the empty state with the default and the given text', () => {
    const { unmount } = renderList({ state: { kind: 'empty' } });
    expect(screen.getByText('No items.')).toBeInTheDocument();
    unmount();
    renderList({ state: { kind: 'empty', text: 'No queues found.' } });
    expect(screen.getByText('No queues found.')).toBeInTheDocument();
  });

  it('shows the error state and calls onRetry', () => {
    const onRetry = jest.fn();
    renderList({ state: { kind: 'error', text: 'It failed', onRetry } });
    expect(screen.getByText('It failed')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Try again' }));
    expect(onRetry).toHaveBeenCalledTimes(1);
  });

  it('shows the error state with no retry button when there is no onRetry', () => {
    renderList({ state: { kind: 'error', text: 'It failed' } });
    expect(screen.queryByRole('button', { name: 'Try again' })).toBeNull();
  });
});

describe('ListView phone', () => {
  const phone = {
    title: (r: Row) => r.name,
    chip: () => <StatusChip state="running" />,
    meta: (r: Row) => r.note,
  };
  const renderPhone = (extra: Partial<React.ComponentProps<typeof ListView<Row>>> = {}) =>
    render(
      <ListView<Row>
        mode="phone"
        ariaLabel="Items"
        columns={columns}
        rows={rows}
        getRowKey={(r) => r.id}
        phone={phone}
        rowTestId={(r) => `card-${r.id}`}
        {...extra}
      />
    );

  it('shows the title, the chip, and the meta of each row', () => {
    renderPhone();
    const row = screen.getByTestId('card-1');
    expect(within(row).getByText('Alpha')).toBeInTheDocument();
    expect(within(row).getByRole('status')).toBeInTheDocument();
    expect(within(row).getByText('first')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Open Alpha' })).toBeInTheDocument();
  });

  it('shows the first action as a touch button and a menu', () => {
    const onSelect = jest.fn();
    renderPhone({ rowActions: actionsFor(onSelect), menuLabel: (r) => `More actions for ${r.name}` });
    const row = screen.getByTestId('card-1');
    const main = within(row).getByRole('button', { name: 'Start Alpha' });
    expect(main).toHaveClass('ui-icon-button--touch');
    expect(within(row).queryByRole('button', { name: 'Stop Alpha' })).toBeNull();
    fireEvent.click(main);
    expect(onSelect).toHaveBeenCalledTimes(1);
    const more = within(row).getByRole('button', { name: 'More actions for Alpha' });
    expect(more).toHaveClass('ui-icon-button--touch');
    fireEvent.click(more);
    expect(screen.getByRole('menuitem', { name: 'Delete' })).toBeInTheDocument();
  });

  it('calls onRowClick for a tap on the open area', () => {
    const onRowClick = jest.fn();
    renderPhone({ onRowClick });
    fireEvent.click(screen.getByRole('button', { name: 'Open Beta' }));
    expect(onRowClick).toHaveBeenCalledWith(rows[1]);
  });

  it('uses the first column text as the title when there is no phone config', () => {
    renderPhone({ phone: undefined });
    expect(screen.getByRole('button', { name: 'Open Alpha' })).toBeInTheDocument();
  });

  it('uses a render prop for the actions', () => {
    renderPhone({ renderPhoneActions: (r) => <span>phone {r.name}</span> });
    expect(screen.getByText('phone Alpha')).toBeInTheDocument();
  });

  it('shows the state rows', () => {
    const onRetry = jest.fn();
    const { unmount } = renderPhone({ state: { kind: 'loading' } });
    expect(screen.getByText('Loading…')).toBeInTheDocument();
    unmount();
    const second = renderPhone({ state: { kind: 'empty', text: 'None yet.' } });
    expect(screen.getByText('None yet.')).toBeInTheDocument();
    second.unmount();
    renderPhone({ state: { kind: 'error', text: 'Broken', onRetry } });
    fireEvent.click(screen.getByRole('button', { name: 'Try again' }));
    expect(onRetry).toHaveBeenCalled();
  });

  it('uses the layout mode of the window when no mode is given', () => {
    render(<ListView<Row> ariaLabel="Auto" columns={columns} rows={rows} getRowKey={(r) => r.id} phone={phone} />);
    expect(screen.getByRole('table', { name: 'Auto' })).toBeInTheDocument();
  });
});