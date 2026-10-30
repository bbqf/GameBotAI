import React from 'react';
import { fireEvent, screen } from '@testing-library/react';
import { WaitForImagePanel } from '../WaitForImagePanel';
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

describe('WaitForImagePanel timeout parameter (feature 169)', () => {
  it('writes the template, hides the input, and keeps the plain value', () => {
    const onConfirm = jest.fn();
    renderPanelWithParameters(<WaitForImagePanel onConfirm={onConfirm} onCancel={() => {}} />);
    fireEvent.change(screen.getByLabelText(/^timeout/i), { target: { value: '2500' } });
    selectParameter('Timeout', 'delay');
    expect(screen.queryByLabelText(/^timeout/i)).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Add' }));
    expect(onConfirm.mock.calls[0][0]).toMatchObject({
      timeoutMs: '2500',
      fieldTemplates: { 'waitForImage.timeoutMs': '{{delay}}' },
    });
  });

  it('accepts an empty timeout when a parameter is set and sends 0', () => {
    const onConfirm = jest.fn();
    renderPanelWithParameters(<WaitForImagePanel onConfirm={onConfirm} onCancel={() => {}} />);
    fireEvent.change(screen.getByLabelText(/^timeout/i), { target: { value: '' } });
    selectParameter('Timeout', 'delay');
    fireEvent.click(screen.getByRole('button', { name: 'Add' }));
    expect(onConfirm.mock.calls[0][0].timeoutMs).toBe('0');
  });

  it('restores the plain input after remove', () => {
    renderPanelWithParameters(<WaitForImagePanel onConfirm={() => {}} onCancel={() => {}} />);
    selectParameter('Timeout', 'delay');
    removeParameter('Timeout');
    expectPlainInput('Timeout (ms) *', 'Timeout');
  });

  it('shows a service message at the control of that path only', () => {
    renderPanelWithParameters(<WaitForImagePanel onConfirm={() => {}} onCancel={() => {}} />, {
      fieldErrors: { 'waitForImage.timeoutMs': 'Step 2: bad' },
    });
    expectFieldError('Timeout', 'Step 2: bad');
    expect(screen.getAllByRole('alert')).toHaveLength(1);
  });
});
