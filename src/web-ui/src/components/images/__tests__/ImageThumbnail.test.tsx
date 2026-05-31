import React from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import { ImageThumbnail } from '../ImageThumbnail';
import { getImageBlob } from '../../../services/images';

jest.mock('../../../services/images', () => ({
  getImageBlob: jest.fn(),
}));

// Module-level cache is shared across tests — clear it by resetting the module between test groups
// that rely on fresh cache state.

const mockGetImageBlob = getImageBlob as jest.MockedFunction<typeof getImageBlob>;

beforeEach(() => {
  jest.clearAllMocks();
  // Stub URL methods not available in jsdom
  global.URL.createObjectURL = jest.fn(() => 'blob:mock-url');
  global.URL.revokeObjectURL = jest.fn();
});

describe('ImageThumbnail', () => {
  it('renders an img with the object URL after the blob fetch resolves', async () => {
    mockGetImageBlob.mockResolvedValueOnce(new Blob(['img'], { type: 'image/png' }));
    render(<ImageThumbnail imageId="test-img" alt="test alt" />);
    const img = await screen.findByRole('img', { name: 'test alt' });
    expect(img).toHaveAttribute('src', 'blob:mock-url');
  });

  it('renders a placeholder span while the blob fetch is pending', () => {
    mockGetImageBlob.mockReturnValueOnce(new Promise(() => {})); // never resolves
    render(<ImageThumbnail imageId="pending-img" />);
    // img should not yet appear
    expect(screen.queryByRole('img')).toBeNull();
    expect(document.querySelector('.image-thumbnail--placeholder')).not.toBeNull();
  });

  it('renders a placeholder span when the blob fetch rejects', async () => {
    mockGetImageBlob.mockRejectedValueOnce(new Error('Not found'));
    render(<ImageThumbnail imageId="missing-img" />);
    await waitFor(() =>
      expect(document.querySelector('.image-thumbnail--placeholder')).not.toBeNull()
    );
    expect(screen.queryByRole('img')).toBeNull();
  });

  it('uses the cached URL on a second render and does not fetch again', async () => {
    // First render — populates cache
    mockGetImageBlob.mockResolvedValueOnce(new Blob(['img'], { type: 'image/png' }));
    const { unmount } = render(<ImageThumbnail imageId="cached-img" />);
    await screen.findByRole('img');
    unmount();

    // Second render — should use cache, not call getImageBlob again
    render(<ImageThumbnail imageId="cached-img" />);
    expect(await screen.findByRole('img')).toHaveAttribute('src', 'blob:mock-url');
    expect(mockGetImageBlob).toHaveBeenCalledTimes(1);
  });
});
