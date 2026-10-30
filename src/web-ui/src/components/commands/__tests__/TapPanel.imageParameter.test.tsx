import React from 'react';
import { fireEvent, screen } from '@testing-library/react';
import { TapPanel } from '../TapPanel';
import {
  expectFieldError,
  removeParameter,
  renderPanelWithParameters,
  selectParameter,
} from './parameterPanelTestHelpers';

const IMAGE_PATH = 'primitiveTap.detectionTarget.referenceImageId';

jest.mock('../../images/ImageSelectorDropdown', () => ({
  ImageSelectorDropdown: ({ value, onChange, label }: any) => (
    <div>
      {label && <label htmlFor="mock-image-input">{label}</label>}
      <input id="mock-image-input" data-testid="image-input" value={value} onChange={(e) => onChange(e.target.value)} />
    </div>
  ),
}));

describe('TapPanel image parameter (feature 169)', () => {
  it('hides the picker, shows the name, and writes the template', () => {
    const onConfirm = jest.fn();
    renderPanelWithParameters(
      <TapPanel initialValue={{ referenceImageId: 'img-1' }} onConfirm={onConfirm} onCancel={() => {}} />,
    );
    selectParameter('Reference image', 'buttonToTap');
    expect(screen.queryByTestId('image-input')).not.toBeInTheDocument();
    expect(screen.getByTestId(`chip-${IMAGE_PATH}`)).toHaveTextContent('buttonToTap');
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));
    expect(onConfirm.mock.calls[0][0]).toMatchObject({
      referenceImageId: 'img-1',
      fieldTemplates: { [IMAGE_PATH]: '{{buttonToTap}}' },
    });
  });

  it('restores the picker with the last chosen image after remove', () => {
    renderPanelWithParameters(
      <TapPanel initialValue={{ referenceImageId: 'img-1' }} onConfirm={() => {}} onCancel={() => {}} />,
    );
    selectParameter('Reference image', 'buttonToTap');
    removeParameter('Reference image');
    expect(screen.getByTestId('image-input')).toHaveValue('img-1');
  });

  it('asks for a stored image first when none is selected (the service refuses an empty image)', () => {
    renderPanelWithParameters(<TapPanel onConfirm={() => {}} onCancel={() => {}} />);
    expect(screen.getByText('Select a stored image first. Then select the parameter.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Use a parameter for Reference image' })).toBeDisabled();
    fireEvent.change(screen.getByTestId('image-input'), { target: { value: 'img-2' } });
    expect(screen.getByRole('button', { name: 'Use a parameter for Reference image' })).toBeEnabled();
    expect(screen.queryByText('Select a stored image first. Then select the parameter.')).not.toBeInTheDocument();
  });

  it('shows a service message at the image control', () => {
    renderPanelWithParameters(
      <TapPanel initialValue={{ referenceImageId: 'img-1' }} onConfirm={() => {}} onCancel={() => {}} />,
      { fieldErrors: { [IMAGE_PATH]: 'Step 0: image bad' } },
    );
    expectFieldError('Reference image', 'Step 0: image bad');
  });
});
