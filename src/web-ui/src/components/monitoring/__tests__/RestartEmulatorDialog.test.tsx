import React from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { RestartEmulatorDialog } from '../RestartEmulatorDialog';

const setup = (props: Partial<React.ComponentProps<typeof RestartEmulatorDialog>> = {}) => {
  const onCancel = jest.fn();
  const onConfirm = jest.fn();
  render(
    <RestartEmulatorDialog
      emulatorName="pns (emulator-5558)"
      queueName={null}
      hasQueue={false}
      onCancel={onCancel}
      onConfirm={onConfirm}
      {...props}
    />
  );
  return { onCancel, onConfirm };
};

describe('RestartEmulatorDialog (feature 141, M-31 and M-32)', () => {
  it('names the emulator and has the buttons Cancel and Restart', () => {
    setup();
    expect(screen.getByRole('dialog', { name: 'Restart emulator' })).toBeInTheDocument();
    expect(screen.getByText('pns (emulator-5558)')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Cancel' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Restart' })).toBeInTheDocument();
  });

  it('shows the queue warning only when a queue owns the session', () => {
    const first = render(
      <RestartEmulatorDialog emulatorName="x" queueName={null} hasQueue={false} onCancel={() => undefined} onConfirm={() => undefined} />
    );
    expect(screen.queryByTestId('restart-queue-warning')).toBeNull();
    first.unmount();
    render(
      <RestartEmulatorDialog emulatorName="x" queueName="PNS Daily 5558" hasQueue onCancel={() => undefined} onConfirm={() => undefined} />
    );
    expect(screen.getByTestId('restart-queue-warning')).toHaveTextContent(
      'Queue PNS Daily 5558 uses this emulator. The queue work will stop for a short time.'
    );
  });

  it('closes with Cancel and sends nothing', () => {
    const { onCancel, onConfirm } = setup();
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(onCancel).toHaveBeenCalledTimes(1);
    expect(onConfirm).not.toHaveBeenCalled();
  });

  it('closes with Escape and sends nothing', () => {
    const { onCancel, onConfirm } = setup();
    fireEvent.keyDown(screen.getByRole('dialog'), { key: 'Escape' });
    expect(onCancel).toHaveBeenCalledTimes(1);
    expect(onConfirm).not.toHaveBeenCalled();
  });

  it('confirms with Restart', () => {
    const { onCancel, onConfirm } = setup();
    fireEvent.click(screen.getByRole('button', { name: 'Restart' }));
    expect(onConfirm).toHaveBeenCalledTimes(1);
    expect(onCancel).not.toHaveBeenCalled();
  });

  it('starts with focus on Cancel and keeps focus inside the dialog', () => {
    setup();
    const cancel = screen.getByRole('button', { name: 'Cancel' });
    const restart = screen.getByRole('button', { name: 'Restart' });
    expect(cancel).toHaveFocus();
    restart.focus();
    fireEvent.keyDown(restart, { key: 'Tab' });
    expect(cancel).toHaveFocus();
    fireEvent.keyDown(cancel, { key: 'Tab', shiftKey: true });
    expect(restart).toHaveFocus();
  });
});
