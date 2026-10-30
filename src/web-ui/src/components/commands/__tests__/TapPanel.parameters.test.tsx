import React from 'react';
import { fireEvent, screen } from '@testing-library/react';
import { TapPanel } from '../TapPanel';
import {
  expectFieldError,
  expectPlainInput,
  removeParameter,
  renderPanelWithParameters,
  selectParameter,
} from './parameterPanelTestHelpers';

jest.mock('../../images/ImageSelectorDropdown', () => ({
  ImageSelectorDropdown: ({ value, onChange, label }: any) => (
    <div>
      {label && <label htmlFor="mock-image-input">{label}</label>}
      <input id="mock-image-input" data-testid="image-input" value={value} onChange={(e) => onChange(e.target.value)} />
    </div>
  ),
}));

describe('TapPanel numeric parameters (feature 169)', () => {
  const start = (extra: Record<string, unknown> = {}) => {
    const onConfirm = jest.fn();
    renderPanelWithParameters(
      <TapPanel initialValue={{ referenceImageId: 'img-1', offsetX: '5' }} onConfirm={onConfirm} onCancel={() => {}} />,
      extra,
    );
    return onConfirm;
  };

  it.each([
    ['Confidence', 'primitiveTap.detectionTarget.confidence'],
    ['Offset X', 'primitiveTap.detectionTarget.offsetX'],
    ['Offset Y', 'primitiveTap.detectionTarget.offsetY'],
  ])('writes the template for %s', (label, path) => {
    const onConfirm = start();
    selectParameter(label, 'delay');
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));
    expect(onConfirm.mock.calls[0][0].fieldTemplates).toEqual({ [path]: '{{delay}}' });
  });

  it('hides the plain input, then restores it with the kept value', () => {
    start();
    selectParameter('Offset X', 'delay');
    expect(screen.queryByLabelText(/^offset x/i)).not.toBeInTheDocument();
    removeParameter('Offset X');
    expectPlainInput('Offset X', 'Offset X');
    expect(screen.getByLabelText(/^offset x/i)).toHaveValue(5);
  });

  it('accepts an out of range confidence when a parameter is set', () => {
    const onConfirm = start();
    fireEvent.change(screen.getByLabelText(/^confidence/i), { target: { value: '5' } });
    selectParameter('Confidence', 'delay');
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));
    expect(onConfirm).toHaveBeenCalledTimes(1);
  });

  it('shows a service message at the control of that path only', () => {
    start({ fieldErrors: { 'primitiveTap.detectionTarget.offsetY': 'Step 1: bad' } });
    expectFieldError('Offset Y', 'Step 1: bad');
    expect(screen.getAllByRole('alert')).toHaveLength(1);
  });
});
