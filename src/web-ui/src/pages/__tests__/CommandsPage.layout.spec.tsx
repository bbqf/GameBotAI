import React from 'react';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { CommandsPage } from '../CommandsPage';
import { listCommands, getCommand, deleteCommand } from '../../services/commands';
import { ApiError } from '../../lib/api';

jest.mock('../../services/commands');

jest.mock('@dnd-kit/core', () => {
  const React = jest.requireActual('react');
  return {
    DndContext: ({ children }: any) => React.createElement(React.Fragment, null, children),
    PointerSensor: class {},
    useSensor: jest.fn(),
    useSensors: jest.fn(() => []),
  };
});

jest.mock('@dnd-kit/sortable', () => ({
  SortableContext: ({ children }: { children: React.ReactNode }) => <>{children}</>,
  verticalListSortingStrategy: {},
  useSortable: () => ({ attributes: {}, listeners: {}, setNodeRef: jest.fn(), transform: null, transition: undefined, isDragging: false }),
  arrayMove: jest.fn(),
}));

jest.mock('@dnd-kit/utilities', () => ({
  CSS: { Translate: { toString: () => '' }, Transform: { toString: () => '' } },
}));

jest.mock('../../components/images/ImageSelectorDropdown', () => ({
  ImageSelectorDropdown: ({ id, label, value, onChange }: { id?: string; label?: string; value: string; onChange: (v: string) => void }) => (
    <>
      {label && <label htmlFor={id}>{label}</label>}
      <input id={id} value={value} onChange={(e) => onChange(e.target.value)} />
    </>
  ),
}));

const listCommandsMock = listCommands as jest.MockedFunction<typeof listCommands>;
const getCommandMock = getCommand as jest.MockedFunction<typeof getCommand>;
const deleteCommandMock = deleteCommand as jest.MockedFunction<typeof deleteCommand>;

const command = { id: 'c1', name: 'Open shop', steps: [{ type: 'GoToHomeScreen', order: 0 }, { type: 'GoToHomeScreen', order: 1 }] };

beforeEach(() => {
  jest.resetAllMocks();
  listCommandsMock.mockResolvedValue([command] as any);
  getCommandMock.mockResolvedValue(command as any);
});

describe('CommandsPage list', () => {
  it('shows the header, the filter, and the columns Name and Steps with no Game column', async () => {
    render(<CommandsPage />);
    await screen.findByText('Open shop');
    expect(screen.getByRole('heading', { name: 'Commands' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Create command' })).toBeInTheDocument();
    expect(screen.getByLabelText('Filter by name').closest('th')).toBeNull();
    const headers = screen.getAllByRole('columnheader').map((h) => h.textContent);
    expect(headers).toContain('Name');
    expect(headers).toContain('Steps');
    expect(headers).not.toContain('Game');
    expect(screen.queryByText('All games')).toBeNull();
    expect(within(screen.getByTestId('command-row-c1')).getByText('2')).toBeInTheDocument();
  });

  it('shows the empty text', async () => {
    listCommandsMock.mockResolvedValue([] as any);
    render(<CommandsPage />);
    expect(await screen.findByText('No commands.')).toBeInTheDocument();
  });

  it('shows the error state with Try again', async () => {
    listCommandsMock.mockRejectedValueOnce(new Error('Server down'));
    render(<CommandsPage />);
    expect(await screen.findByText('Server down')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Try again' }));
    expect(await screen.findByText('Open shop')).toBeInTheDocument();
  });

  it('filters the rows by name', async () => {
    listCommandsMock.mockResolvedValue([command, { id: 'c2', name: 'Close shop', steps: [] }] as any);
    render(<CommandsPage />);
    await screen.findByText('Open shop');
    fireEvent.change(screen.getByLabelText('Filter by name'), { target: { value: 'close' } });
    expect(screen.queryByText('Open shop')).toBeNull();
    expect(screen.getByText('Close shop')).toBeInTheDocument();
  });

  it('opens the edit page with the Edit button', async () => {
    render(<CommandsPage />);
    await screen.findByText('Open shop');
    fireEvent.click(screen.getByRole('button', { name: 'Edit Open shop' }));
    expect(await screen.findByRole('heading', { name: 'Open shop' })).toBeInTheDocument();
  });
});

describe('CommandsPage form page', () => {
  it('shows the create title and no Delete button', async () => {
    render(<CommandsPage />);
    await screen.findByText('Open shop');
    fireEvent.click(screen.getByRole('button', { name: 'Create command' }));
    expect(screen.getByRole('heading', { name: 'New command' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Delete' })).toBeNull();
  });

  it('keeps the loaded name as the title while the name field changes', async () => {
    render(<CommandsPage />);
    await screen.findByText('Open shop');
    fireEvent.click(screen.getByText('Open shop'));
    const title = await screen.findByRole('heading', { name: 'Open shop' });
    fireEvent.change(screen.getByLabelText('Name *'), { target: { value: 'Changed' } });
    expect(screen.getByRole('heading', { name: 'Open shop' })).toBe(title);
  });

  it('puts Delete first and Save last in the footer bar of the edit page', async () => {
    const { container } = render(<CommandsPage />);
    await screen.findByText('Open shop');
    fireEvent.click(screen.getByText('Open shop'));
    await screen.findByRole('heading', { name: 'Open shop' });
    await waitFor(() => expect(screen.getByLabelText('Name *')).toHaveValue('Open shop'));
    const buttons = Array.from(container.querySelectorAll('.ui-footer-bar button')).map((b) => b.textContent);
    expect(buttons).toEqual(['Delete', 'Cancel', 'Save']);
  });

  it('goes back to the list with the back button', async () => {
    render(<CommandsPage />);
    await screen.findByText('Open shop');
    fireEvent.click(screen.getByText('Open shop'));
    await screen.findByRole('heading', { name: 'Open shop' });
    fireEvent.click(screen.getByRole('button', { name: 'Back' }));
    expect(await screen.findByRole('heading', { name: 'Commands' })).toBeInTheDocument();
  });

  it('asks before the back button leaves unsaved changes', async () => {
    const confirmSpy = jest.spyOn(window, 'confirm').mockReturnValue(false);
    render(<CommandsPage />);
    await screen.findByText('Open shop');
    fireEvent.click(screen.getByText('Open shop'));
    await screen.findByRole('heading', { name: 'Open shop' });
    await waitFor(() => expect(screen.getByLabelText('Name *')).toHaveValue('Open shop'));
    fireEvent.change(screen.getByLabelText('Name *'), { target: { value: 'Changed' } });
    fireEvent.click(screen.getByRole('button', { name: 'Back' }));
    expect(confirmSpy).toHaveBeenCalled();
    expect(screen.getByLabelText('Name *')).toBeInTheDocument();
    confirmSpy.mockRestore();
  });

  it('shows the name error with aria-invalid for an empty name', async () => {
    render(<CommandsPage />);
    await screen.findByText('Open shop');
    fireEvent.click(screen.getByRole('button', { name: 'Create command' }));
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));
    expect(await screen.findByText('Name is required')).toBeInTheDocument();
    expect(screen.getByLabelText('Name *')).toHaveAttribute('aria-invalid', 'true');
  });
});

describe('CommandsPage delete', () => {
  const openRowDelete = async () => {
    render(<CommandsPage />);
    await screen.findByText('Open shop');
    fireEvent.click(screen.getByRole('button', { name: 'More actions for Open shop' }));
    fireEvent.click(await screen.findByRole('menuitem', { name: /Delete/ }));
    return screen.findByRole('dialog', { name: 'Delete command Open shop?' });
  };

  it('deletes from the row menu after the dialog confirm', async () => {
    deleteCommandMock.mockResolvedValue(undefined as any);
    const dialog = await openRowDelete();
    expect(within(dialog).getByText('You cannot undo this.')).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: 'Cancel' })).toHaveFocus();
    listCommandsMock.mockResolvedValue([] as any);
    fireEvent.click(within(dialog).getByRole('button', { name: 'Delete' }));
    await waitFor(() => expect(deleteCommandMock).toHaveBeenCalledTimes(1));
    expect(deleteCommandMock).toHaveBeenCalledWith('c1');
    expect(await screen.findByText('Command deleted.')).toBeInTheDocument();
    expect(screen.queryByRole('dialog')).toBeNull();
  });

  it('shows the references for a 409 and turns the Delete button off', async () => {
    deleteCommandMock.mockRejectedValue(new ApiError(409, 'Command is in use', undefined, undefined, { sequences: [{ id: 's1', name: 'Seq One' }] }));
    const dialog = await openRowDelete();
    fireEvent.click(within(dialog).getByRole('button', { name: 'Delete' }));
    expect(await within(dialog).findByText('Referenced by:')).toBeInTheDocument();
    expect(within(dialog).getByText(/Seq One/)).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: 'Delete' })).toBeDisabled();
    fireEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }));
    expect(screen.queryByRole('dialog')).toBeNull();
  });

  it('shows another error in the message area', async () => {
    deleteCommandMock.mockRejectedValue(new Error('Network failure'));
    const dialog = await openRowDelete();
    fireEvent.click(within(dialog).getByRole('button', { name: 'Delete' }));
    expect(await screen.findByText('Network failure')).toBeInTheDocument();
    expect(screen.queryByRole('dialog')).toBeNull();
  });

  it('deletes from the footer of the edit page and shows the list', async () => {
    deleteCommandMock.mockResolvedValue(undefined as any);
    const { container } = render(<CommandsPage />);
    await screen.findByText('Open shop');
    fireEvent.click(screen.getByText('Open shop'));
    await screen.findByRole('heading', { name: 'Open shop' });
    fireEvent.click(within(container.querySelector('.ui-footer-bar') as HTMLElement).getByRole('button', { name: 'Delete' }));
    const dialog = await screen.findByRole('dialog', { name: 'Delete command Open shop?' });
    listCommandsMock.mockResolvedValue([] as any);
    fireEvent.click(within(dialog).getByRole('button', { name: 'Delete' }));
    expect(await screen.findByText('Command deleted.')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Commands' })).toBeInTheDocument();
  });
});
