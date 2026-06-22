import React from 'react';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { QueueTemplateControls } from '../QueueTemplateControls';
import { listQueueTemplates } from '../../../services/queueTemplates';
import { ApiError } from '../../../lib/api';

jest.mock('../../../services/queueTemplates');

const listMock = listQueueTemplates as jest.MockedFunction<typeof listQueueTemplates>;

beforeEach(() => {
  jest.resetAllMocks();
  listMock.mockResolvedValue([] as any);
});

const renderControls = (overrides: Partial<React.ComponentProps<typeof QueueTemplateControls>> = {}) => {
  const onSaveTemplate = overrides.onSaveTemplate ?? jest.fn().mockResolvedValue(undefined);
  const onLoadTemplate = overrides.onLoadTemplate ?? jest.fn();
  const onReload = overrides.onReload ?? jest.fn();
  render(
    <QueueTemplateControls
      associatedTemplateName={overrides.associatedTemplateName}
      status={overrides.status ?? 'Stopped'}
      onSaveTemplate={onSaveTemplate}
      onLoadTemplate={onLoadTemplate}
      onReload={onReload}
      saveResult={overrides.saveResult}
    />
  );
  return { onSaveTemplate, onLoadTemplate, onReload };
};

/** Opens the manage area via the template name button and returns its region. */
const openManageArea = async (placeholder: string) => {
  fireEvent.click(screen.getByText(placeholder));
  return screen.findByRole('region', { name: 'Load template' });
};

describe('QueueTemplateControls', () => {
  it('renders the manage area closed by default', () => {
    renderControls();
    expect(screen.queryByRole('region', { name: 'Load template' })).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Template name')).not.toBeInTheDocument();
  });

  it('opens the manage area (load list + name field) when the template name button is clicked', async () => {
    renderControls();
    const area = await openManageArea('(no template)');
    expect(within(area).getByLabelText('Template name')).toBeInTheDocument();
  });

  it('shows the associated template name on the name button and pre-fills the name field', async () => {
    renderControls({ associatedTemplateName: 'Daily Farm' });
    expect(screen.getByText('Daily Farm')).toBeInTheDocument();
    const area = await openManageArea('Daily Farm');
    expect(within(area).getByLabelText('Template name')).toHaveValue('Daily Farm');
  });

  it('does not render a separate "Save as template" popup, even with the manage area open', async () => {
    renderControls({ associatedTemplateName: 'Daily Farm' });
    await openManageArea('Daily Farm');
    expect(screen.queryByRole('region', { name: 'Save template' })).not.toBeInTheDocument();
  });

  it('disables Save Template when no template is associated (use Rename to create one)', () => {
    renderControls();
    expect(screen.getByText('Save Template')).toBeDisabled();
  });

  it('Save Template (bottom) quick-saves the associated template under its existing name', async () => {
    const { onSaveTemplate } = renderControls({ associatedTemplateName: 'Daily Farm' });
    // Save directly without opening the area or changing the name.
    fireEvent.click(screen.getByText('Save Template'));
    await waitFor(() => expect(onSaveTemplate).toHaveBeenCalledWith('Daily Farm', true));
    expect(screen.queryByRole('region', { name: 'Confirm overwrite' })).not.toBeInTheDocument();
  });

  it('Save Template (bottom) uses the OLD name even after the field was edited without clicking Rename', async () => {
    const { onSaveTemplate } = renderControls({ associatedTemplateName: 'Daily Farm' });
    const area = await openManageArea('Daily Farm');
    // Edit the name but do NOT click Rename.
    fireEvent.change(within(area).getByLabelText('Template name'), { target: { value: 'New Name' } });
    fireEvent.click(screen.getByText('Save Template'));
    await waitFor(() => expect(onSaveTemplate).toHaveBeenCalledWith('Daily Farm', true));
  });

  it('saves a brand-new name (typed in the manage area) via Rename with overwrite=false', async () => {
    const { onSaveTemplate } = renderControls();
    const area = await openManageArea('(no template)');
    fireEvent.change(within(area).getByLabelText('Template name'), { target: { value: 'Fresh' } });
    fireEvent.click(within(area).getByText('Rename'));
    await waitFor(() => expect(onSaveTemplate).toHaveBeenCalledWith('Fresh', false));
    expect(screen.queryByRole('region', { name: 'Confirm overwrite' })).not.toBeInTheDocument();
  });

  it('applies the typed name via the Rename button in the manage area', async () => {
    const { onSaveTemplate } = renderControls({ associatedTemplateName: 'Daily Farm' });
    const area = await openManageArea('Daily Farm');
    fireEvent.change(within(area).getByLabelText('Template name'), { target: { value: 'Weekly Farm' } });
    fireEvent.click(within(area).getByText('Rename'));
    await waitFor(() => expect(onSaveTemplate).toHaveBeenCalledWith('Weekly Farm', false));
  });

  it('shows a name validation error in the manage area when Rename is clicked with an empty name', async () => {
    const { onSaveTemplate } = renderControls();
    const area = await openManageArea('(no template)');
    fireEvent.change(within(area).getByLabelText('Template name'), { target: { value: '   ' } });
    fireEvent.click(within(area).getByText('Rename'));
    expect(within(area).getByRole('alert')).toHaveTextContent(/required/i);
    expect(onSaveTemplate).not.toHaveBeenCalled();
  });

  it('on a differing name that collides (409) shows overwrite confirm then re-saves with overwrite=true', async () => {
    const onSaveTemplate = jest
      .fn()
      .mockRejectedValueOnce(new ApiError(409, 'template_exists'))
      .mockResolvedValueOnce(undefined);
    renderControls({ onSaveTemplate, associatedTemplateName: 'Daily Farm' });

    const area = await openManageArea('Daily Farm');
    fireEvent.change(within(area).getByLabelText('Template name'), { target: { value: 'Arena Grind' } });
    fireEvent.click(within(area).getByText('Rename'));

    // The overwrite confirmation appears in the manage area, next to the Rename button.
    const confirm = await within(area).findByRole('region', { name: 'Confirm overwrite' });
    expect(onSaveTemplate).toHaveBeenNthCalledWith(1, 'Arena Grind', false);
    fireEvent.click(within(confirm).getByText('Overwrite'));
    await waitFor(() => expect(onSaveTemplate).toHaveBeenNthCalledWith(2, 'Arena Grind', true));
  });

  it('loads the selected template and closes the manage area', async () => {
    listMock.mockResolvedValue([{ id: 't1', name: 'Daily Farm', entryCount: 1, createdAt: null, updatedAt: null }] as any);
    const { onLoadTemplate } = renderControls();
    const area = await openManageArea('(no template)');
    fireEvent.click(within(area).getByText('Load'));
    expect(onLoadTemplate).toHaveBeenCalledWith('t1');
    await waitFor(() => expect(screen.queryByRole('region', { name: 'Load template' })).not.toBeInTheDocument());
  });

  it('disables Reload Template when no template is associated', () => {
    renderControls();
    expect(screen.getByText('Reload Template')).toBeDisabled();
  });

  it('disables Reload Template while the queue is running', () => {
    renderControls({ associatedTemplateName: 'Daily Farm', status: 'Running' });
    expect(screen.getByText('Reload Template')).toBeDisabled();
  });

  it('fires onReload when enabled and clicked', () => {
    const { onReload } = renderControls({ associatedTemplateName: 'Daily Farm' });
    fireEvent.click(screen.getByText('Reload Template'));
    expect(onReload).toHaveBeenCalled();
  });

  it('renders a success save-result message at the template controls', () => {
    renderControls({ saveResult: { kind: 'success', message: 'Template "Daily Farm" saved successfully.' } });
    expect(screen.getByRole('status')).toHaveTextContent('Template "Daily Farm" saved successfully.');
  });

  it('renders an error save-result message at the template controls', () => {
    renderControls({ saveResult: { kind: 'error', message: 'Failed to save template' } });
    expect(screen.getByRole('alert')).toHaveTextContent('Failed to save template');
  });
});
