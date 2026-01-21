import React from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { EmulatorCaptureCropper } from '../EmulatorCaptureCropper';
import { cropImageFromCapture, fetchEmulatorScreenshot } from '../../../services/images';
import { ApiError } from '../../../lib/api';

jest.mock('../../../services/images');

const fetchEmulatorScreenshotMock = fetchEmulatorScreenshot as jest.MockedFunction<typeof fetchEmulatorScreenshot>;
const cropImageFromCaptureMock = cropImageFromCapture as jest.MockedFunction<typeof cropImageFromCapture>;

describe('EmulatorCaptureCropper', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    (URL as any).createObjectURL = jest.fn(() => 'blob:mock-url');
    (URL as any).revokeObjectURL = jest.fn();
  });

  it('captures, selects, and saves a crop', async () => {
    fetchEmulatorScreenshotMock.mockResolvedValue({ captureId: 'cap-1', blob: new Blob(['png'], { type: 'image/png' }) });
    cropImageFromCaptureMock.mockResolvedValue({ name: 'hero', fileName: 'hero.png', storagePath: 'data/images/hero.png', bounds: { x: 10, y: 10, width: 40, height: 40 } });

    render(<EmulatorCaptureCropper />);

    fireEvent.click(screen.getByRole('button', { name: /capture emulator screenshot/i }));
    expect(fetchEmulatorScreenshotMock).toHaveBeenCalled();

    const img = await screen.findByAltText(/emulator screenshot/i);
    Object.defineProperty(img, 'naturalWidth', { value: 200, configurable: true });
    Object.defineProperty(img, 'naturalHeight', { value: 100, configurable: true });
    img.getBoundingClientRect = () => ({ left: 0, top: 0, width: 200, height: 100, right: 200, bottom: 100, x: 0, y: 0, toJSON: () => ({}) });
    fireEvent.load(img);

    const overlay = screen.getByTestId('capture-overlay');
    fireEvent.mouseDown(overlay, { clientX: 10, clientY: 10 });
    fireEvent.mouseMove(overlay, { clientX: 50, clientY: 50 });
    fireEvent.mouseUp(overlay, { clientX: 50, clientY: 50 });

    fireEvent.change(screen.getByLabelText(/Image name/i), { target: { value: 'hero' } });
    fireEvent.click(screen.getByRole('button', { name: /save crop/i }));

    await waitFor(() => expect(cropImageFromCaptureMock).toHaveBeenCalled());
    expect(cropImageFromCaptureMock).toHaveBeenCalledWith({
      name: 'hero',
      overwrite: false,
      sourceCaptureId: 'cap-1',
      bounds: { x: 10, y: 10, width: 40, height: 40 }
    });
  });

  it('shows conflict guidance when name exists', async () => {
    fetchEmulatorScreenshotMock.mockResolvedValue({ captureId: 'cap-1', blob: new Blob(['png'], { type: 'image/png' }) });
    cropImageFromCaptureMock.mockRejectedValue(new ApiError(409, 'Name already exists'));

    render(<EmulatorCaptureCropper />);

    fireEvent.click(screen.getByRole('button', { name: /capture emulator screenshot/i }));
    const img = await screen.findByAltText(/emulator screenshot/i);
    Object.defineProperty(img, 'naturalWidth', { value: 200, configurable: true });
    Object.defineProperty(img, 'naturalHeight', { value: 200, configurable: true });
    img.getBoundingClientRect = () => ({ left: 0, top: 0, width: 200, height: 200, right: 200, bottom: 200, x: 0, y: 0, toJSON: () => ({}) });
    fireEvent.load(img);

    const overlay = screen.getByTestId('capture-overlay');
    fireEvent.mouseDown(overlay, { clientX: 0, clientY: 0 });
    fireEvent.mouseMove(overlay, { clientX: 40, clientY: 40 });
    fireEvent.mouseUp(overlay, { clientX: 40, clientY: 40 });

    fireEvent.change(screen.getByLabelText(/Image name/i), { target: { value: 'dup' } });
    fireEvent.click(screen.getByRole('button', { name: /save crop/i }));

    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent(/already exists/i));
  });

  it('blocks saves below minimum size', async () => {
    fetchEmulatorScreenshotMock.mockResolvedValue({ captureId: 'cap-1', blob: new Blob(['png'], { type: 'image/png' }) });
    cropImageFromCaptureMock.mockResolvedValue({ name: 'tiny', fileName: 'tiny.png', storagePath: 'data/images/tiny.png', bounds: { x: 0, y: 0, width: 10, height: 10 } });

    render(<EmulatorCaptureCropper />);

    fireEvent.click(screen.getByRole('button', { name: /capture emulator screenshot/i }));
    const img = await screen.findByAltText(/emulator screenshot/i);
    Object.defineProperty(img, 'naturalWidth', { value: 50, configurable: true });
    Object.defineProperty(img, 'naturalHeight', { value: 50, configurable: true });
    img.getBoundingClientRect = () => ({ left: 0, top: 0, width: 50, height: 50, right: 50, bottom: 50, x: 0, y: 0, toJSON: () => ({}) });
    fireEvent.load(img);

    const overlay = screen.getByTestId('capture-overlay');
    fireEvent.mouseDown(overlay, { clientX: 0, clientY: 0 });
    fireEvent.mouseMove(overlay, { clientX: 5, clientY: 5 });
    fireEvent.mouseUp(overlay, { clientX: 5, clientY: 5 });

    fireEvent.change(screen.getByLabelText(/Image name/i), { target: { value: 'tiny' } });
    fireEvent.click(screen.getByRole('button', { name: /save crop/i }));

    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent(/at least 16x16/i));
    expect(cropImageFromCaptureMock).not.toHaveBeenCalled();
  });

  it('shows guidance when capture expires', async () => {
    fetchEmulatorScreenshotMock.mockResolvedValue({ captureId: 'cap-1', blob: new Blob(['png'], { type: 'image/png' }) });
    cropImageFromCaptureMock.mockRejectedValue(new ApiError(404, 'capture missing', undefined, { error: 'capture_missing' }));

    render(<EmulatorCaptureCropper />);

    fireEvent.click(screen.getByRole('button', { name: /capture emulator screenshot/i }));
    const img = await screen.findByAltText(/emulator screenshot/i);
    Object.defineProperty(img, 'naturalWidth', { value: 100, configurable: true });
    Object.defineProperty(img, 'naturalHeight', { value: 100, configurable: true });
    img.getBoundingClientRect = () => ({ left: 0, top: 0, width: 100, height: 100, right: 100, bottom: 100, x: 0, y: 0, toJSON: () => ({}) });
    fireEvent.load(img);

    const overlay = screen.getByTestId('capture-overlay');
    fireEvent.mouseDown(overlay, { clientX: 0, clientY: 0 });
    fireEvent.mouseMove(overlay, { clientX: 40, clientY: 40 });
    fireEvent.mouseUp(overlay, { clientX: 40, clientY: 40 });

    fireEvent.change(screen.getByLabelText(/Image name/i), { target: { value: 'capture-missing' } });
    fireEvent.click(screen.getByRole('button', { name: /save crop/i }));

    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent(/capture expired/i));
    await waitFor(() => expect(screen.queryByAltText(/emulator screenshot/i)).not.toBeInTheDocument());
  });

  it('shows guidance when bounds exceed the capture', async () => {
    fetchEmulatorScreenshotMock.mockResolvedValue({ captureId: 'cap-1', blob: new Blob(['png'], { type: 'image/png' }) });
    cropImageFromCaptureMock.mockRejectedValue(new ApiError(400, 'Bounds outside image', undefined, { error: 'bounds_out_of_range', captureSize: { width: 50, height: 50 } }));

    render(<EmulatorCaptureCropper />);

    fireEvent.click(screen.getByRole('button', { name: /capture emulator screenshot/i }));
    const img = await screen.findByAltText(/emulator screenshot/i);
    Object.defineProperty(img, 'naturalWidth', { value: 50, configurable: true });
    Object.defineProperty(img, 'naturalHeight', { value: 50, configurable: true });
    img.getBoundingClientRect = () => ({ left: 0, top: 0, width: 50, height: 50, right: 50, bottom: 50, x: 0, y: 0, toJSON: () => ({}) });
    fireEvent.load(img);

    const overlay = screen.getByTestId('capture-overlay');
    fireEvent.mouseDown(overlay, { clientX: 0, clientY: 0 });
    fireEvent.mouseMove(overlay, { clientX: 60, clientY: 60 });
    fireEvent.mouseUp(overlay, { clientX: 60, clientY: 60 });

    fireEvent.change(screen.getByLabelText(/Image name/i), { target: { value: 'too-large' } });
    fireEvent.click(screen.getByRole('button', { name: /save crop/i }));

    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent(/outside the captured image/i));
    expect(screen.getByLabelText(/Selection rectangle/i)).toBeInTheDocument();
  });
});
