import React, { useState } from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { ParameterDeclarationList } from '../ParameterDeclarationList';
import type { ParameterDeclaration } from '../types';

jest.mock('../../../services/images', () => ({
  getImageBlob: jest.fn(() => Promise.resolve(new Blob(['x']))),
  listImageItems: jest.fn(() => Promise.resolve([])),
}));

jest.mock('../../../hooks/useLayoutMode', () => ({ useLayoutMode: () => 'desktop' }));

const Harness: React.FC<{ initial: ParameterDeclaration[] }> = ({ initial }) => {
  const [parameters, setParameters] = useState(initial);
  return (
    <>
      <ParameterDeclarationList parameters={parameters} onChange={setParameters} />
      <output data-testid="json">{JSON.stringify(parameters)}</output>
    </>
  );
};

const blank: ParameterDeclaration = { name: '', type: 'text', required: false };

describe('ParameterDeclarationCard', () => {
  it('suggests a name from the label when the name is empty', () => {
    render(<Harness initial={[blank]} />);

    fireEvent.change(screen.getByLabelText('Parameter 1 label'), { target: { value: 'Emulator instance' } });

    expect(screen.getByLabelText('Parameter 1 name')).toHaveValue('emulatorInstance');
  });

  it('keeps the name when the author has edited it', () => {
    render(<Harness initial={[blank]} />);
    fireEvent.change(screen.getByLabelText('Parameter 1 label'), { target: { value: 'Speed' } });
    fireEvent.change(screen.getByLabelText('Parameter 1 name'), { target: { value: 'myName' } });

    fireEvent.change(screen.getByLabelText('Parameter 1 label'), { target: { value: 'Speed up' } });

    expect(screen.getByLabelText('Parameter 1 name')).toHaveValue('myName');
  });

  it('shows only the settings of the chosen kind', () => {
    render(<Harness initial={[{ ...blank, name: 'a' }]} />);
    expect(screen.queryByRole('button', { name: 'Add option' })).not.toBeInTheDocument();

    fireEvent.change(screen.getByLabelText('Parameter 1 input'), { target: { value: 'choice' } });
    expect(screen.getByRole('button', { name: 'Add option' })).toBeInTheDocument();
    expect(screen.queryByLabelText('Minimum')).not.toBeInTheDocument();

    fireEvent.change(screen.getByLabelText('Parameter 1 input'), { target: { value: 'image' } });
    expect(screen.getByRole('button', { name: 'Choose images' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Add option' })).not.toBeInTheDocument();
  });

  it('offers number settings for a number and clears them when the kind changes', () => {
    render(<Harness initial={[{ ...blank, name: 'n', type: 'number', min: 1, max: 5 }]} />);
    expect(screen.getByLabelText('Minimum')).toHaveValue('1');

    fireEvent.change(screen.getByLabelText('Parameter 1 input'), { target: { value: 'choice' } });

    expect(JSON.parse(screen.getByTestId('json').textContent ?? '[]')[0].min).toBeNull();
  });

  it('shows a live preview that matches the value form and follows the card order', () => {
    render(
      <Harness
        initial={[
          { name: 'first', type: 'text', required: false, label: 'First one' },
          { name: 'second', type: 'text', required: false, label: 'Second one' },
        ]}
      />,
    );

    const preview = screen.getByRole('complementary', { name: 'Preview of the value form' });
    const names = () => Array.from(preview.querySelectorAll('.parameter-binding-name')).map((n) => n.textContent);
    expect(names()).toEqual(['First one', 'Second one']);
    expect(preview.querySelector('.parameter-binding-inherit')).toBeNull();

    fireEvent.click(screen.getByRole('button', { name: 'Move first down' }));
    expect(names()).toEqual(['Second one', 'First one']);
  });

  it('keeps the move, remove, and name checks of the plain row', () => {
    render(<Harness initial={[{ ...blank, name: 'bad name' }]} />);

    fireEvent.blur(screen.getByLabelText('Parameter 1 name'));
    expect(screen.getByRole('alert')).toHaveTextContent(/Use letters, digits and underscore/);
    fireEvent.click(screen.getByRole('button', { name: 'Remove bad name' }));
    expect(screen.queryByLabelText('Parameter 1 name')).not.toBeInTheDocument();
  });
});
