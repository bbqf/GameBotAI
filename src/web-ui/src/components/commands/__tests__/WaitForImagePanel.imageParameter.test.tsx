import React from 'react';
import { fireEvent, screen } from '@testing-library/react';
import { WaitForImagePanel } from '../WaitForImagePanel';
import {
  expectFieldError,
  removeParameter,
  renderPanelWithParameters,
  selectParameter,
} from './parameterPanelTestHelpers';

const IMAGE_PATH = 'waitForImage.detectionTarget.referenceImageId';

jest.mock('../../images/ImageSelectorDropdown', () => ({
  ImageSelectorDropdown: ({ value, onChange, label }: any) => (
    <div>
      {label && <label htmlFor="mock-image-input">{label}</label>}
      <input id="mock-image-input" data-testid="image-input" value={value} onChange={(e) => onChange(e.target.value)} />
    </div>
  ),
}));

describe('WaitForImagePanel image parameter (feature 169)', () => {
  it('hides the picker, shows the name, and writes the template (empty inline image is accepted)', () => {
    const onConfirm = jest.fn();
    renderPanelWithParameters(<WaitForImagePanel onConfirm={onConfirm} onCancel={() => {}} />);
    selectParameter('Reference image', 'buttonToTap');
    expect(screen.queryByTestId('image-input')).not.toBeInTheDocument();
    expect(screen.getByTestId(`chip-${IMAGE_PATH}`)).toHaveTextContent('buttonToTap');
    fireEvent.click(screen.getByRole('button', { name: 'Add' }));
    expect(onConfirm.mock.calls[0][0]).toMatchObject({
      fieldTemplates: { [IMAGE_PATH]: '{{buttonToTap}}' },
    });
  });

  it('restores the picker with the last chosen image after remove', () => {
    renderPanelWithParameters(
      <WaitForImagePanel initialValue={{ timeoutMs: '100', referenceImageId: 'img-9' }} onConfirm={() => {}} onCancel={() => {}} />,
    );
    selectParameter('Reference image', 'buttonToTap');
    removeParameter('Reference image');
    expect(screen.getByTestId('image-input')).toHaveValue('img-9');
  });

  it('shows a service message at the image control', () => {
    renderPanelWithParameters(<WaitForImagePanel onConfirm={() => {}} onCancel={() => {}} />, {
      fieldErrors: { [IMAGE_PATH]: 'Step 1: image bad' },
    });
    expectFieldError('Reference image', 'Step 1: image bad');
  });
});
