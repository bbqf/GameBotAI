import React, { useState } from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { AllowedImagesEditor } from '../AllowedImagesEditor';

jest.mock('../../../services/images', () => ({
  getImageBlob: jest.fn(() => Promise.resolve(new Blob(['x'], { type: 'image/png' }))),
  listImageItems: jest.fn(() => Promise.resolve([
    { id: 'mine-1', owner: { kind: 'user' } },
    { id: 'mine-2', owner: { kind: 'user' } },
    { id: 'pack-1', owner: { kind: 'gamepack', name: 'Nova' } },
  ])),
}));

beforeAll(() => {
  (URL as unknown as { createObjectURL: () => string }).createObjectURL = () => 'blob:test';
  (URL as unknown as { revokeObjectURL: () => void }).revokeObjectURL = () => undefined;
});

const Harness: React.FC<{ initial?: string[] }> = ({ initial = [] }) => {
  const [allowed, setAllowed] = useState(initial);
  return (
    <>
      <AllowedImagesEditor allowed={allowed} onChange={setAllowed} />
      <output data-testid="allowed">{allowed.join(',')}</output>
    </>
  );
};

describe('AllowedImagesEditor', () => {
  it('groups the stored images by owner in the dialog', async () => {
    render(<Harness />);

    fireEvent.click(screen.getByRole('button', { name: 'Choose images' }));

    await waitFor(() => expect(screen.getByText('mine-1')).toBeInTheDocument());
    expect(screen.getByText('Your images')).toBeInTheDocument();
    expect(screen.getByText('Pack: Nova')).toBeInTheDocument();
  });

  it('saves the checked images and refuses an empty choice', async () => {
    render(<Harness />);
    fireEvent.click(screen.getByRole('button', { name: 'Choose images' }));
    await waitFor(() => expect(screen.getByText('mine-1')).toBeInTheDocument());

    expect(screen.getByRole('button', { name: /Use 0 image/ })).toBeDisabled();
    fireEvent.click(screen.getByLabelText('mine-1'));
    fireEvent.click(screen.getByLabelText('pack-1'));
    fireEvent.click(screen.getByRole('button', { name: /Use 2 image/ }));

    expect(screen.getByTestId('allowed')).toHaveTextContent('mine-1,pack-1');
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('removes an allowed image with its button', () => {
    render(<Harness initial={['mine-1', 'mine-2']} />);

    fireEvent.click(screen.getByRole('button', { name: 'Remove image mine-1' }));

    expect(screen.getByTestId('allowed')).toHaveTextContent('mine-2');
  });

  it('filters the dialog list with the search box', async () => {
    render(<Harness />);
    fireEvent.click(screen.getByRole('button', { name: 'Choose images' }));
    await waitFor(() => expect(screen.getByText('mine-1')).toBeInTheDocument());

    fireEvent.change(screen.getByLabelText('Search stored images'), { target: { value: 'pack' } });

    expect(screen.queryByText('mine-1')).not.toBeInTheDocument();
    expect(screen.getByText('pack-1')).toBeInTheDocument();
  });
});
