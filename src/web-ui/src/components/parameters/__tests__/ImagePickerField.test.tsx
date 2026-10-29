import React, { useState } from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { ImagePickerField } from '../fields/ImagePickerField';
import type { ParameterDeclaration } from '../types';

jest.mock('../../../services/images', () => ({
  getImageBlob: jest.fn(() => Promise.resolve(new Blob(['x'], { type: 'image/png' }))),
}));

beforeAll(() => {
  (URL as unknown as { createObjectURL: () => string }).createObjectURL = () => 'blob:test';
  (URL as unknown as { revokeObjectURL: () => void }).revokeObjectURL = () => undefined;
});

const declaration = (allowed: string[]): ParameterDeclaration => ({
  name: 'pic', type: 'text', kind: 'image', required: false, allowedImages: allowed,
});

const Harness: React.FC<{ allowed: string[]; initial?: string }> = ({ allowed, initial = '' }) => {
  const [value, setValue] = useState(initial);
  return (
    <>
      <ImagePickerField declaration={declaration(allowed)} value={value} onChange={setValue} ariaLabel="Value for pic" />
      <output data-testid="value">{value}</output>
    </>
  );
};

describe('ImagePickerField', () => {
  it('shows only the allowed images, each with a text name', () => {
    render(<Harness allowed={['img-a', 'img-b']} />);

    expect(screen.getAllByRole('radio')).toHaveLength(2);
    expect(screen.getByRole('radio', { name: 'img-a' })).toBeInTheDocument();
    expect(screen.getByText('img-b')).toBeInTheDocument();
  });

  it('selects an image with a click', () => {
    render(<Harness allowed={['img-a', 'img-b']} />);

    fireEvent.click(screen.getByRole('radio', { name: 'img-b' }));

    expect(screen.getByTestId('value')).toHaveTextContent('img-b');
    expect(screen.getByRole('radio', { name: 'img-b' })).toHaveAttribute('aria-checked', 'true');
  });

  it('selects with the arrow keys', () => {
    render(<Harness allowed={['img-a', 'img-b', 'img-c']} initial="img-a" />);

    fireEvent.keyDown(screen.getByRole('radio', { name: 'img-a' }), { key: 'ArrowRight' });

    expect(screen.getByTestId('value')).toHaveTextContent('img-b');
  });

  it('shows the search box only for more than 12 images', () => {
    const many = Array.from({ length: 13 }, (_, i) => `img-${i}`);
    const { unmount } = render(<Harness allowed={many} />);
    expect(screen.getByRole('searchbox')).toBeInTheDocument();
    fireEvent.change(screen.getByRole('searchbox'), { target: { value: 'img-12' } });
    expect(screen.getAllByRole('radio')).toHaveLength(1);
    unmount();

    render(<Harness allowed={['img-a']} />);
    expect(screen.queryByRole('searchbox')).not.toBeInTheDocument();
  });

  it('shows "Image not found" for a value that is not allowed any more', () => {
    render(<Harness allowed={['img-a']} initial="removed" />);

    expect(screen.getByRole('alert')).toHaveTextContent(/Image not found: removed/);
    expect(screen.getByRole('alert')).toHaveTextContent(/Select one of the allowed images/);
    expect(screen.getByTestId('value')).toHaveTextContent('removed');
  });
});
