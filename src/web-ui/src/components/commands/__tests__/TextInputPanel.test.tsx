import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { TextInputPanel, TEXT_REQUIRED, TEXT_UNSUPPORTED } from '../TextInputPanel';

describe('TextInputPanel', () => {
  const textField = () => screen.getByLabelText(/^text \*/i);

  it('shows an error and sends nothing when the text is empty', () => {
    const onConfirm = jest.fn();
    render(<TextInputPanel onConfirm={onConfirm} onCancel={() => {}} />);
    fireEvent.click(screen.getByRole('button', { name: 'Add' }));
    expect(screen.getByRole('alert')).toHaveTextContent(TEXT_REQUIRED);
    expect(onConfirm).not.toHaveBeenCalled();
  });

  it('shows an error for a character that is not printable ASCII', () => {
    const onConfirm = jest.fn();
    render(<TextInputPanel onConfirm={onConfirm} onCancel={() => {}} />);
    fireEvent.change(textField(), { target: { value: 'é' } });
    fireEvent.click(screen.getByRole('button', { name: 'Add' }));
    expect(screen.getByRole('alert')).toHaveTextContent(TEXT_UNSUPPORTED);
    expect(onConfirm).not.toHaveBeenCalled();
  });

  it('sends the text 250 when Add is clicked', () => {
    const onConfirm = jest.fn();
    render(<TextInputPanel onConfirm={onConfirm} onCancel={() => {}} />);
    fireEvent.change(textField(), { target: { value: '250' } });
    fireEvent.click(screen.getByRole('button', { name: 'Add' }));
    expect(onConfirm).toHaveBeenCalledWith(expect.objectContaining({ text: '250' }));
    expect(onConfirm.mock.calls[0][0].fieldTemplates).toBeUndefined();
  });

  it('sends the parameter as the textInput.text key and permits an empty text', () => {
    const onConfirm = jest.fn();
    render(
      <TextInputPanel
        onConfirm={onConfirm}
        onCancel={() => {}}
        parameters={[{ name: 'fighterBatch', type: 'number', required: false }]}
      />,
    );
    fireEvent.click(screen.getByRole('button', { name: 'Use a parameter for Text' }));
    fireEvent.click(screen.getByRole('option', { name: /fighterBatch/ }));
    fireEvent.click(screen.getByRole('button', { name: 'Add' }));
    expect(onConfirm).toHaveBeenCalledWith({ text: '', fieldTemplates: { 'textInput.text': '{{fighterBatch}}' } });
  });

  it('shows Save for an edit', () => {
    render(<TextInputPanel initialValue={{ text: '250' }} onConfirm={() => {}} onCancel={() => {}} />);
    expect(screen.getByRole('button', { name: 'Save' })).toBeInTheDocument();
    expect(textField()).toHaveValue('250');
  });
});
