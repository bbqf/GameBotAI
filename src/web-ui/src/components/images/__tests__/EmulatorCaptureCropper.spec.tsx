import React from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { EmulatorCaptureCropper } from '../EmulatorCaptureCropper';
import { cropImageFromCapture, fetchEmulatorScreenshot } from '../../../services/images';

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
});
