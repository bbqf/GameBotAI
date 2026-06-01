import React from 'react';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { TemplatePickerDialog } from '../TemplatePickerDialog';
import { listQueueTemplates, deleteQueueTemplate } from '../../../services/queueTemplates';

jest.mock('../../../services/queueTemplates');

const listMock = listQueueTemplates as jest.MockedFunction<typeof listQueueTemplates>;
const deleteMock = deleteQueueTemplate as jest.MockedFunction<typeof deleteQueueTemplate>;

const summary = (id: string, name: string) => ({ id, name, entryCount: 1, createdAt: null, updatedAt: null });

beforeEach(() => {
  jest.resetAllMocks();
  listMock.mockResolvedValue([summary('t1', 'Daily Farm'), summary('t2', 'Arena Push')] as any);
});

const renderPicker = (overrides: Partial<React.ComponentProps<typeof TemplatePickerDialog>> = {}) => {
  const onLoad = overrides.onLoad ?? jest.fn();
  const onClose = overrides.onClose ?? jest.fn();
  render(<TemplatePickerDialog open onLoad={onLoad} onClose={onClose} loadDisabled={overrides.loadDisabled} />);
  return { onLoad, onClose };
};

describe('TemplatePickerDialog', () => {
  it('lists templates by name', async () => {
    renderPicker();
    expect(await screen.findByText('Daily Farm')).toBeInTheDocument();
    expect(screen.getByText('Arena Push')).toBeInTheDocument();
  });

  it('shows an empty state when there are no templates', async () => {
    listMock.mockResolvedValue([] as any);
    renderPicker();
    expect(await screen.findByText('No templates saved yet.')).toBeInTheDocument();
  });

  it('fires onLoad with the template id when Load is clicked', async () => {
    const { onLoad } = renderPicker();
    const row = (await screen.findByText('Daily Farm')).closest('li') as HTMLElement;
    fireEvent.click(within(row).getByText('Load'));
    expect(onLoad).toHaveBeenCalledWith('t1');
  });

  it('disables Load when loadDisabled is set', async () => {
    renderPicker({ loadDisabled: true });
    const row = (await screen.findByText('Daily Farm')).closest('li') as HTMLElement;
    expect(within(row).getByText('Load')).toBeDisabled();
  });

  it('deletes a template after confirmation and refreshes the list', async () => {
    deleteMock.mockResolvedValue(undefined as any);
    renderPicker();
    const row = (await screen.findByText('Daily Farm')).closest('li') as HTMLElement;
    fireEvent.click(within(row).getByText('Delete'));

    const confirm = await screen.findByRole('dialog', { name: 'Confirm Delete' });
    listMock.mockResolvedValue([summary('t2', 'Arena Push')] as any);
    fireEvent.click(within(confirm).getByRole('button', { name: 'Delete' }));

    await waitFor(() => expect(deleteMock).toHaveBeenCalledWith('t1'));
    await waitFor(() => expect(screen.queryByText('Daily Farm')).not.toBeInTheDocument());
  });
});
