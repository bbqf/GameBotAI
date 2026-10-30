import React from 'react';
import { fireEvent, screen } from '@testing-library/react';
import { SwipePanel } from '../SwipePanel';
import {
  expectFieldError,
  expectPlainInput,
  removeParameter,
  renderPanelWithParameters,
  selectParameter,
} from './parameterPanelTestHelpers';

const fill = (label: string, value: string) =>
  fireEvent.change(screen.getByLabelText(label), { target: { value } });

describe('SwipePanel parameters (feature 169)', () => {
  it('writes the template for the duration and hides the plain input', () => {
    const onConfirm = jest.fn();
    renderPanelWithParameters(<SwipePanel onConfirm={onConfirm} onCancel={() => {}} />);
    fill('Start X *', '1');
    fill('Start Y *', '2');
    fill('End X *', '3');
    fill('End Y *', '4');
    selectParameter('Duration (ms)', 'delay');
    expect(screen.queryByLabelText('Duration (ms)')).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Add' }));
    expect(onConfirm).toHaveBeenCalledWith(
      expect.objectContaining({ fieldTemplates: { 'swipe.durationMs': '{{delay}}' } }),
    );
  });

  it('accepts a parameter for a coordinate and sends 0 for the empty required field', () => {
    const onConfirm = jest.fn();
    renderPanelWithParameters(<SwipePanel onConfirm={onConfirm} onCancel={() => {}} />);
    fill('Start Y *', '2');
    fill('End X *', '3');
    fill('End Y *', '4');
    selectParameter('Start X', 'delay');
    fireEvent.click(screen.getByRole('button', { name: 'Add' }));
    expect(onConfirm).toHaveBeenCalledTimes(1);
    expect(onConfirm.mock.calls[0][0]).toMatchObject({
      startX: '0',
      fieldTemplates: { 'swipe.startX': '{{delay}}' },
    });
  });

  it('restores the plain input with the kept value when the parameter is removed', () => {
    renderPanelWithParameters(<SwipePanel onConfirm={() => {}} onCancel={() => {}} />);
    fill('Duration (ms)', '250');
    selectParameter('Duration (ms)', 'delay');
    removeParameter('Duration (ms)');
    expectPlainInput('Duration (ms)', 'Duration (ms)');
    expect(screen.getByLabelText('Duration (ms)')).toHaveValue(250);
  });

  it('keeps a key that has no control and a key of another field', () => {
    const onConfirm = jest.fn();
    renderPanelWithParameters(
      <SwipePanel
        initialValue={{ startX: '1', startY: '2', endX: '3', endY: '4' }}
        fieldTemplates={{ 'ensureGameRunning.readinessTimeoutMs': '{{delay}}' }}
        onConfirm={onConfirm}
        onCancel={() => {}}
      />,
    );
    selectParameter('End Y', 'delay');
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));
    expect(onConfirm.mock.calls[0][0].fieldTemplates).toEqual({
      'ensureGameRunning.readinessTimeoutMs': '{{delay}}',
      'swipe.endY': '{{delay}}',
    });
  });

  it('shows a service message at the control of that path only', () => {
    renderPanelWithParameters(<SwipePanel onConfirm={() => {}} onCancel={() => {}} />, {
      fieldErrors: { 'swipe.durationMs': 'Step 0: parameter bad' },
    });
    expectFieldError('Duration (ms)', 'Step 0: parameter bad');
    expect(screen.getAllByRole('alert')).toHaveLength(1);
  });

  it('sends no template map when none is set', () => {
    const onConfirm = jest.fn();
    renderPanelWithParameters(<SwipePanel onConfirm={onConfirm} onCancel={() => {}} />);
    fill('Start X *', '1');
    fill('Start Y *', '2');
    fill('End X *', '3');
    fill('End Y *', '4');
    fireEvent.click(screen.getByRole('button', { name: 'Add' }));
    expect(onConfirm.mock.calls[0][0].fieldTemplates).toBeUndefined();
  });
});
