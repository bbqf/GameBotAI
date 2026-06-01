import React from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { SaveTemplateDialog } from '../SaveTemplateDialog';
import { ApiError } from '../../../lib/api';

const renderDialog = (overrides: Partial<React.ComponentProps<typeof SaveTemplateDialog>> = {}) => {
  const onSave = overrides.onSave ?? jest.fn().mockResolvedValue(undefined);
  const onClose = overrides.onClose ?? jest.fn();
  render(
    <SaveTemplateDialog
      open={overrides.open ?? true}
      originName={overrides.originName}
      onSave={onSave}
      onClose={onClose}
    />
  );
  return { onSave, onClose };
};

describe('SaveTemplateDialog', () => {
  it('pre-fills the origin template name', () => {
    renderDialog({ originName: 'Daily Farm' });
    expect(screen.getByLabelText('Template name')).toHaveValue('Daily Farm');
  });

  it('blocks saving with an invalid name and does not call onSave', () => {
    const { onSave } = renderDialog();
    fireEvent.change(screen.getByLabelText('Template name'), { target: { value: 'bad/name' } });
    fireEvent.click(screen.getByText('Save'));
    expect(screen.getByRole('alert')).toHaveTextContent(/letters, digits, spaces/i);
    expect(onSave).not.toHaveBeenCalled();
  });

  it('saves a new template with overwrite=false', async () => {
    const onSave = jest.fn().mockResolvedValue(undefined);
    const { onClose } = renderDialog({ onSave });
    fireEvent.change(screen.getByLabelText('Template name'), { target: { value: 'Daily Farm' } });
    fireEvent.click(screen.getByText('Save'));
    await waitFor(() => expect(onSave).toHaveBeenCalledWith('Daily Farm', false));
    await waitFor(() => expect(onClose).toHaveBeenCalled());
  });

  it('on conflict shows overwrite confirm then re-saves with overwrite=true', async () => {
    const onSave = jest
      .fn()
      .mockRejectedValueOnce(new ApiError(409, 'template_exists'))
      .mockResolvedValueOnce(undefined);
    renderDialog({ onSave, originName: 'Daily Farm' });

    fireEvent.click(screen.getByText('Save'));
    await screen.findByText('Overwrite');
    expect(screen.getByText(/already exists/i)).toBeInTheDocument();

    fireEvent.click(screen.getByText('Overwrite'));
    await waitFor(() => expect(onSave).toHaveBeenNthCalledWith(2, 'Daily Farm', true));
  });

  it('cancel saves nothing', () => {
    const onSave = jest.fn();
    const { onClose } = renderDialog({ onSave });
    fireEvent.click(screen.getByText('Cancel'));
    expect(onSave).not.toHaveBeenCalled();
    expect(onClose).toHaveBeenCalled();
  });
});
