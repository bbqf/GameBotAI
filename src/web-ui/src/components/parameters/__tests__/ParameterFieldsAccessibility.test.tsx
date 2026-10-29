import React from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { ParameterValueField } from '../fields/ParameterValueField';
import type { ParameterDeclaration } from '../types';

jest.mock('../../../services/images', () => ({
  getImageBlob: jest.fn(() => Promise.resolve(new Blob(['x']))),
}));

beforeAll(() => {
  (URL as unknown as { createObjectURL: () => string }).createObjectURL = () => 'blob:test';
  (URL as unknown as { revokeObjectURL: () => void }).revokeObjectURL = () => undefined;
});

const text: ParameterDeclaration = { name: 'txt', type: 'text', required: false };
const num: ParameterDeclaration = { name: 'num', type: 'number', required: false, min: 1, max: 5 };
const choice: ParameterDeclaration = { name: 'cho', type: 'text', kind: 'choice', required: false, options: [{ label: 'One', value: '1' }, { label: 'Two', value: '2' }] };
const image: ParameterDeclaration = { name: 'img', type: 'text', kind: 'image', required: false, allowedImages: ['img-a', 'img-b'] };

describe('accessibility of the parameter value fields (FR-025)', () => {
  it.each([
    ['text', text, 'textbox'],
    ['number', num, 'textbox'],
    ['choice', choice, 'radiogroup'],
    ['image', image, 'radiogroup'],
  ] as const)('the %s field has a role and an accessible name', (_kind, declaration, role) => {
    render(<ParameterValueField declaration={declaration} value="" onChange={jest.fn()} />);

    expect(screen.getByRole(role, { name: `Value for ${declaration.name}` })).toBeInTheDocument();
  });

  it('the choice and image options are radios with names and one tab stop', () => {
    const { rerender } = render(<ParameterValueField declaration={choice} value="" onChange={jest.fn()} />);
    expect(screen.getByRole('radio', { name: 'One' })).toHaveAttribute('tabindex', '0');
    expect(screen.getByRole('radio', { name: 'Two' })).toHaveAttribute('tabindex', '-1');

    rerender(<ParameterValueField declaration={image} value="img-b" onChange={jest.fn()} />);
    expect(screen.getByRole('radio', { name: 'img-b' })).toHaveAttribute('tabindex', '0');
    expect(screen.getByRole('radio', { name: 'img-a' })).toHaveAttribute('tabindex', '-1');
  });

  it('the number field links its message with aria-invalid and aria-describedby', () => {
    render(<ParameterValueField declaration={num} value="" onChange={jest.fn()} />);

    fireEvent.change(screen.getByRole('textbox', { name: 'Value for num' }), { target: { value: '99' } });

    const input = screen.getByRole('textbox', { name: 'Value for num' });
    expect(input).toHaveAttribute('aria-invalid', 'true');
    const describedBy = input.getAttribute('aria-describedby') ?? '';
    expect(describedBy).toContain('parameter-number-message-num');
    expect(document.getElementById('parameter-number-message-num')).toHaveTextContent(/maximum 5/);
  });

  it('the pass-through control is named and keyboard-operable', () => {
    render(
      <ParameterValueField
        declaration={choice}
        value=""
        onChange={jest.fn()}
        sequenceDeclarations={[{ name: 'seq', type: 'text', required: false }]}
      />,
    );

    const select = screen.getByRole('combobox', { name: /Use a parameter of this sequence/ });
    select.focus();
    expect(select).toHaveFocus();
  });
});
