import React from 'react';
import { fireEvent, screen } from '@testing-library/react';
import { EnsureEmulatorRunningPanel } from '../EnsureEmulatorRunningPanel';
import {
  expectFieldError,
  expectPlainInput,
  removeParameter,
  renderPanelWithParameters,
  selectParameter,
} from './parameterPanelTestHelpers';

describe('EnsureEmulatorRunningPanel index parameter (feature 169)', () => {
  const fillSerial = () =>
    fireEvent.change(screen.getByLabelText(/^adb serial/i), { target: { value: 'emulator-5554' } });

  it('writes the template and sends 0 as the kept index when no name is given', () => {
    const onConfirm = jest.fn();
    renderPanelWithParameters(<EnsureEmulatorRunningPanel onConfirm={onConfirm} onCancel={() => {}} />);
    fillSerial();
    selectParameter('Instance index', 'delay');
    expect(screen.queryByLabelText(/^instance index/i)).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Add' }));
    expect(onConfirm.mock.calls[0][0]).toMatchObject({
      instanceIndex: '0',
      fieldTemplates: { 'ensureEmulatorRunning.instanceIndex': '{{delay}}' },
    });
  });

  it('keeps the plain index and restores the input after remove', () => {
    renderPanelWithParameters(
      <EnsureEmulatorRunningPanel
        initialValue={{ instanceIndex: '3', adbSerial: 'emulator-5554' }}
        onConfirm={() => {}}
        onCancel={() => {}}
      />,
    );
    selectParameter('Instance index', 'delay');
    removeParameter('Instance index');
    expectPlainInput('Instance index', 'Instance index');
    expect(screen.getByLabelText(/^instance index/i)).toHaveValue(3);
  });

  it('shows a service message at the control of that path only', () => {
    renderPanelWithParameters(<EnsureEmulatorRunningPanel onConfirm={() => {}} onCancel={() => {}} />, {
      fieldErrors: { 'ensureEmulatorRunning.instanceIndex': 'Step 0: bad' },
    });
    expectFieldError('Instance index', 'Step 0: bad');
    expect(screen.getAllByRole('alert')).toHaveLength(1);
  });
});
