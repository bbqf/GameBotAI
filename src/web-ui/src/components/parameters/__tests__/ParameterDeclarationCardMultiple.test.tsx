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

const image: ParameterDeclaration = {
  name: 'quests', type: 'text', kind: 'image', required: false, allowedImages: ['q-a', 'q-b'],
};

describe('ParameterDeclarationCard list settings', () => {
  it('offers "Select several" for image and choice only', () => {
    render(<Harness initial={[image]} />);
    expect(screen.getByLabelText('Parameter 1 select several')).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText('Parameter 1 input'), { target: { value: 'choice' } });
    expect(screen.getByLabelText('Parameter 1 select several')).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText('Parameter 1 input'), { target: { value: 'text' } });
    expect(screen.queryByLabelText('Parameter 1 select several')).not.toBeInTheDocument();
  });

  it('shows the limits only when the box is checked', () => {
    render(<Harness initial={[image]} />);
    expect(screen.queryByLabelText('Parameter 1 minimum items')).not.toBeInTheDocument();

    fireEvent.click(screen.getByLabelText('Parameter 1 select several'));
    expect(screen.getByLabelText('Parameter 1 minimum items')).toBeInTheDocument();
    expect(screen.getByLabelText('Parameter 1 maximum items')).toBeInTheDocument();
  });

  it('stores the flag and the limits, and clears them when the box is cleared', () => {
    render(<Harness initial={[image]} />);

    fireEvent.click(screen.getByLabelText('Parameter 1 select several'));
    fireEvent.change(screen.getByLabelText('Parameter 1 minimum items'), { target: { value: '1' } });
    fireEvent.change(screen.getByLabelText('Parameter 1 maximum items'), { target: { value: '2' } });
    let saved = JSON.parse(screen.getByTestId('json').textContent ?? '[]') as ParameterDeclaration[];
    expect(saved[0]).toMatchObject({ multiple: true, minItems: 1, maxItems: 2 });

    fireEvent.click(screen.getByLabelText('Parameter 1 select several'));
    saved = JSON.parse(screen.getByTestId('json').textContent ?? '[]') as ParameterDeclaration[];
    expect(saved[0].multiple).toBeNull();
    expect(saved[0].minItems).toBeNull();
    expect(saved[0].maxItems).toBeNull();
  });

  it('lets the author pick several items as the default', () => {
    render(<Harness initial={[{ ...image, multiple: true }]} />);

    fireEvent.click(screen.getByLabelText('Default q-b'));
    fireEvent.click(screen.getByLabelText('Default q-a'));

    const saved = JSON.parse(screen.getByTestId('json').textContent ?? '[]') as ParameterDeclaration[];
    expect(saved[0].default).toBe('q-b|q-a');
  });

  it('shows an error when the minimum is more than the maximum', () => {
    render(<Harness initial={[{ ...image, multiple: true, minItems: 3, maxItems: 2 }]} />);

    expect(screen.getByRole('alert')).toHaveTextContent(/minimum is more than the maximum/);
  });
});