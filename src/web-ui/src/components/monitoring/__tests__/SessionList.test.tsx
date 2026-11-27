import React from 'react';
import { fireEvent, render, screen, within } from '@testing-library/react';
import { SessionList } from '../SessionList';
import { makeSession } from '../../../testUtils/monitoring';

describe('SessionList desktop', () => {
  it('shows the columns and the cell texts', () => {
    render(
      <SessionList
        mode="desktop"
        onSelect={() => {}}
        sessions={[
          makeSession('1', { name: 'Device A', ownerQueueName: 'Farm gold', currentSequenceName: 'Daily', ownerQueueDeviceLiveness: 'live' }),
          makeSession('2', { name: 'Device B' })
        ]}
      />
    );
    ['Name', 'State', 'Device', 'Queue', 'Sequence'].forEach((h) =>
      expect(screen.getByRole('columnheader', { name: h })).toBeInTheDocument());
    expect(screen.queryByRole('columnheader', { name: 'Actions' })).toBeNull();
    const first = screen.getByTestId('session-row-1');
    expect(within(first).getByText('Farm gold')).toBeInTheDocument();
    expect(within(first).getByText('Daily')).toBeInTheDocument();
    expect(within(first).getByText('Live', { selector: 'td' })).toBeInTheDocument();
    const second = screen.getByTestId('session-row-2');
    expect(within(second).getAllByText('—')).toHaveLength(3);
  });

  it('shows the chip text and class for each state', () => {
    render(
      <SessionList
        mode="desktop"
        onSelect={() => {}}
        sessions={[
          makeSession('1', { state: 'live' }),
          makeSession('2', { state: 'no_answer' }),
          makeSession('3', { state: 'restarting' }),
          makeSession('4', { state: 'stopping' })
        ]}
      />
    );
    const chip = (id: string) => within(screen.getByTestId(`session-row-${id}`)).getByTestId('session-state');
    expect(chip('1')).toHaveTextContent('Live');
    expect(chip('1')).toHaveClass('ui-chip--done');
    expect(chip('2')).toHaveTextContent('No answer');
    expect(chip('2')).toHaveClass('ui-chip--failed');
    expect(chip('3')).toHaveTextContent('Restarting');
    expect(chip('3')).toHaveClass('ui-chip--paused');
    expect(chip('4')).toHaveTextContent('Stopping');
    expect(chip('4')).toHaveClass('ui-chip--stopped');
  });

  it('shows the "Not live" mark only for a device that is not live (feature 199)', () => {
    render(
      <SessionList
        mode="desktop"
        onSelect={() => {}}
        sessions={[makeSession('1', { ownerQueueDeviceLiveness: 'not_live' }), makeSession('2', { ownerQueueDeviceLiveness: 'live' }), makeSession('3')]}
      />
    );
    expect(within(screen.getByTestId('session-row-1')).getByText('Not live')).toBeInTheDocument();
    expect(within(screen.getByTestId('session-row-2')).queryByText('Not live')).toBeNull();
    expect(within(screen.getByTestId('session-row-3')).queryByText('Not live')).toBeNull();
  });

  it('calls onSelect for a row click and for the open button', () => {
    const onSelect = jest.fn();
    render(<SessionList mode="desktop" onSelect={onSelect} sessions={[makeSession('1', { name: 'Device A' })]} />);
    fireEvent.click(screen.getByTestId('session-row-1'));
    expect(onSelect).toHaveBeenLastCalledWith('1');
    fireEvent.click(screen.getByRole('button', { name: 'Open Device A' }));
    expect(onSelect).toHaveBeenCalledTimes(2);
    expect(onSelect).toHaveBeenLastCalledWith('1');
  });
});

describe('SessionList phone', () => {
  it('shows the name, the chip, the queue name or "No queue", and the mark', () => {
    const onSelect = jest.fn();
    render(
      <SessionList
        mode="phone"
        onSelect={onSelect}
        sessions={[
          makeSession('1', { name: 'Device A', ownerQueueName: 'Farm gold', ownerQueueDeviceLiveness: 'not_live' }),
          makeSession('2', { name: 'Device B' })
        ]}
      />
    );
    const first = screen.getByTestId('session-row-1');
    expect(within(first).getByText('Device A')).toBeInTheDocument();
    expect(within(first).getByTestId('session-state')).toHaveTextContent('Live');
    expect(within(first).getByText(/Farm gold/)).toBeInTheDocument();
    expect(within(first).getByText('Not live')).toBeInTheDocument();
    const second = screen.getByTestId('session-row-2');
    expect(within(second).getByText('No queue')).toBeInTheDocument();
    expect(within(second).queryByText('Not live')).toBeNull();
    fireEvent.click(within(first).getByRole('button', { name: 'Open Device A' }));
    expect(onSelect).toHaveBeenCalledWith('1');
  });
});

describe('SessionList states', () => {
  it.each(['desktop', 'phone'] as const)('shows the loading, empty, and error states (%s)', (mode) => {
    const onRetry = jest.fn();
    const view = render(<SessionList mode={mode} sessions={[]} state={{ kind: 'loading' }} onSelect={() => {}} />);
    expect(screen.getByText('Loading…')).toBeInTheDocument();
    view.rerender(<SessionList mode={mode} sessions={[]} state={{ kind: 'empty', text: 'No running sessions.' }} onSelect={() => {}} />);
    expect(screen.getByText('No running sessions.')).toBeInTheDocument();
    view.rerender(<SessionList mode={mode} sessions={[]} state={{ kind: 'error', text: 'Boom', onRetry }} onSelect={() => {}} />);
    expect(screen.getByRole('alert')).toHaveTextContent('Boom');
    fireEvent.click(screen.getByRole('button', { name: 'Try again' }));
    expect(onRetry).toHaveBeenCalledTimes(1);
  });
});
