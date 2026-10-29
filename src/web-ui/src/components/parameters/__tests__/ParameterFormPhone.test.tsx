import React from 'react';
import { render, screen } from '@testing-library/react';
import { ParameterBindingForm } from '../ParameterBindingForm';
import { ParameterDeclarationList } from '../ParameterDeclarationList';
import type { ParameterDeclaration } from '../types';

jest.mock('../../../hooks/useLayoutMode', () => ({ useLayoutMode: () => 'phone' }));
jest.mock('../../../services/images', () => ({
  getImageBlob: jest.fn(() => Promise.resolve(new Blob(['x']))),
}));

const declarations: ParameterDeclaration[] = [
  { name: 'txt', type: 'text', required: false },
  { name: 'num', type: 'number', required: false, min: 1, max: 5 },
  { name: 'cho', type: 'text', kind: 'choice', required: false, options: [{ label: 'One', value: '1' }, { label: 'Two', value: '2' }] },
  { name: 'img', type: 'text', kind: 'image', required: false, allowedImages: ['img-a'] },
];

describe('value form at phone width', () => {
  beforeAll(() => {
    (URL as unknown as { createObjectURL: () => string }).createObjectURL = () => 'blob:test';
    (URL as unknown as { revokeObjectURL: () => void }).revokeObjectURL = () => undefined;
  });

  it('renders all four kinds', () => {
    render(
      <ParameterBindingForm
        declarations={declarations}
        bindings={declarations.map((d) => ({ name: d.name, value: d.name === 'num' ? '3' : '' }))}
        onChange={jest.fn()}
      />,
    );

    expect(screen.getByRole('textbox', { name: 'Value for txt' })).toBeInTheDocument();
    expect(screen.getByRole('textbox', { name: 'Value for num' })).toBeInTheDocument();
    expect(screen.getByRole('radiogroup', { name: 'Value for cho' })).toBeInTheDocument();
    expect(screen.getByRole('radiogroup', { name: 'Value for img' })).toBeInTheDocument();
  });

  it('uses controls that the phone styles make touch size', () => {
    const { container } = render(
      <ParameterBindingForm declarations={declarations} bindings={[]} onChange={jest.fn()} />,
    );

    // The 44 px rule is in styles.css for these classes. The classes must be on the controls.
    expect(container.querySelector('.choice-button')).not.toBeNull();
    expect(container.querySelector('.image-tile')).not.toBeNull();
    expect(container.querySelector('input.parameter-binding-value')).not.toBeNull();
  });

  it('shows "Use a larger screen" and no card controls in the declaration editor', () => {
    render(<ParameterDeclarationList parameters={[{ name: 'a', type: 'text', required: false }]} onChange={jest.fn()} />);

    expect(screen.getByText(/Use a larger screen/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Add parameter' })).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Parameter 1 name')).not.toBeInTheDocument();
  });
});
