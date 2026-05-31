import React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { ImageSelectorDropdown } from '../ImageSelectorDropdown';
import { listImages } from '../../../services/images';

jest.mock('../../../services/images', () => ({
  listImages: jest.fn(),
}));

// Stub ImageThumbnail to avoid blob fetching in selector tests
jest.mock('../ImageThumbnail', () => ({
  ImageThumbnail: ({ imageId }: { imageId: string }) => (
    <span data-testid={`thumb-${imageId}`} />
  ),
}));

const mockListImages = listImages as jest.MockedFunction<typeof listImages>;

const noop = () => {};
const defaultImages = ['img-a', 'img-b', 'img-c'];

beforeEach(() => {
  jest.clearAllMocks();
  mockListImages.mockResolvedValue(defaultImages);
});

const openDropdown = () => fireEvent.click(screen.getByTestId('image-selector-trigger'));

describe('ImageSelectorDropdown', () => {
  it('renders trigger with placeholder when value is empty', () => {
    render(<ImageSelectorDropdown value="" onChange={noop} />);
    expect(screen.getByText('Select image…')).toBeInTheDocument();
  });

  it('renders trigger showing the current image ID when value is set', () => {
    render(<ImageSelectorDropdown value="img-a" onChange={noop} />);
    expect(screen.getByText('img-a')).toBeInTheDocument();
  });

  it('opens the panel on trigger click', () => {
    render(<ImageSelectorDropdown value="" onChange={noop} />);
    expect(screen.queryByTestId('image-selector-panel')).toBeNull();
    openDropdown();
    expect(screen.getByTestId('image-selector-panel')).toBeInTheDocument();
  });

  it('shows a loading indicator while fetching the image list', () => {
    mockListImages.mockReturnValueOnce(new Promise(() => {})); // never resolves
    render(<ImageSelectorDropdown value="" onChange={noop} />);
    openDropdown();
    expect(screen.getByText('Loading…')).toBeInTheDocument();
  });

  it('renders the image list after the fetch resolves', async () => {
    render(<ImageSelectorDropdown value="" onChange={noop} />);
    openDropdown();
    await waitFor(() => expect(screen.getAllByTestId('image-selector-option')).toHaveLength(3));
    expect(screen.getByText('img-a')).toBeInTheDocument();
    expect(screen.getByText('img-b')).toBeInTheDocument();
    expect(screen.getByText('img-c')).toBeInTheDocument();
  });

  it('filters the list in real time as the user types in the search field', async () => {
    render(<ImageSelectorDropdown value="" onChange={noop} />);
    openDropdown();
    await waitFor(() => screen.getAllByTestId('image-selector-option'));
    fireEvent.change(screen.getByPlaceholderText('Search…'), { target: { value: 'img-b' } });
    expect(screen.getAllByTestId('image-selector-option')).toHaveLength(1);
    expect(screen.getByText('img-b')).toBeInTheDocument();
  });

  it('calls onChange with the image ID and closes the panel when an option is clicked', async () => {
    const onChange = jest.fn();
    render(<ImageSelectorDropdown value="" onChange={onChange} />);
    openDropdown();
    await waitFor(() => screen.getAllByTestId('image-selector-option'));
    fireEvent.click(screen.getAllByTestId('image-selector-option')[0]);
    expect(onChange).toHaveBeenCalledWith('img-a');
    expect(screen.queryByTestId('image-selector-panel')).toBeNull();
  });

  it('shows the empty state message when the image library returns an empty array', async () => {
    mockListImages.mockResolvedValueOnce([]);
    render(<ImageSelectorDropdown value="" onChange={noop} />);
    openDropdown();
    await waitFor(() => expect(screen.getByText('No images available')).toBeInTheDocument());
  });

  it('shows an error message and retry button when the fetch fails', async () => {
    mockListImages.mockRejectedValueOnce(new Error('Network error'));
    render(<ImageSelectorDropdown value="" onChange={noop} />);
    openDropdown();
    await waitFor(() => expect(screen.getByText('Network error')).toBeInTheDocument());
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument();
  });

  it('retries the fetch when the retry button is clicked', async () => {
    mockListImages
      .mockRejectedValueOnce(new Error('fail'))
      .mockResolvedValueOnce(defaultImages);
    render(<ImageSelectorDropdown value="" onChange={noop} />);
    openDropdown();
    await waitFor(() => screen.getByRole('button', { name: 'Retry' }));
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
    await waitFor(() => expect(screen.getAllByTestId('image-selector-option')).toHaveLength(3));
    expect(mockListImages).toHaveBeenCalledTimes(2);
  });

  it('shows a clear button for an optional field (required omitted)', async () => {
    render(<ImageSelectorDropdown value="img-a" onChange={noop} />);
    openDropdown();
    await waitFor(() => screen.getAllByTestId('image-selector-option'));
    expect(screen.getByTestId('image-selector-clear')).toBeInTheDocument();
  });

  it('does not show a clear button when required={true}', async () => {
    render(<ImageSelectorDropdown value="img-a" onChange={noop} required />);
    openDropdown();
    await waitFor(() => screen.getAllByTestId('image-selector-option'));
    expect(screen.queryByTestId('image-selector-clear')).toBeNull();
  });

  it('calls onChange with empty string and fires onStaleChange(false) when clear is clicked', async () => {
    const onChange = jest.fn();
    const onStaleChange = jest.fn();
    render(<ImageSelectorDropdown value="img-a" onChange={onChange} onStaleChange={onStaleChange} />);
    openDropdown();
    await waitFor(() => screen.getByTestId('image-selector-clear'));
    fireEvent.click(screen.getByTestId('image-selector-clear'));
    expect(onChange).toHaveBeenCalledWith('');
    expect(onStaleChange).toHaveBeenCalledWith(false);
  });

  it('fires onStaleChange(true) when the current value is not found in the loaded list', async () => {
    const onStaleChange = jest.fn();
    render(<ImageSelectorDropdown value="ghost-img" onChange={noop} onStaleChange={onStaleChange} />);
    openDropdown();
    await waitFor(() => expect(onStaleChange).toHaveBeenCalledWith(true));
  });

  it('shows the stale warning text when the value is absent from the library', async () => {
    render(<ImageSelectorDropdown value="ghost-img" onChange={noop} />);
    openDropdown();
    await waitFor(() =>
      expect(screen.getByText(/Image "ghost-img" not found in library/)).toBeInTheDocument()
    );
  });

  it('renders the external error prop as a role="alert" element', () => {
    render(<ImageSelectorDropdown value="" onChange={noop} error="Value is required" />);
    expect(screen.getByRole('alert')).toHaveTextContent('Value is required');
  });

  it('closes the panel on a mousedown event outside the component', async () => {
    render(
      <div>
        <ImageSelectorDropdown value="" onChange={noop} />
        <button data-testid="outside">Outside</button>
      </div>
    );
    openDropdown();
    expect(screen.getByTestId('image-selector-panel')).toBeInTheDocument();
    fireEvent.mouseDown(screen.getByTestId('outside'));
    expect(screen.queryByTestId('image-selector-panel')).toBeNull();
  });
});

describe('ImageSelectorDropdown performance', () => {
  it('renders a 50-item list within 500ms of the trigger click (SC-002 / p95 goal)', async () => {
    const fiftyImages = Array.from({ length: 50 }, (_, i) => `image-${i.toString().padStart(2, '0')}`);
    mockListImages.mockResolvedValueOnce(fiftyImages);

    render(<ImageSelectorDropdown value="" onChange={noop} />);

    const start = performance.now();
    fireEvent.click(screen.getByTestId('image-selector-trigger'));
    await waitFor(() => expect(screen.getAllByTestId('image-selector-option')).toHaveLength(50));
    const elapsed = performance.now() - start;

    expect(elapsed).toBeLessThan(500);
  });
});
