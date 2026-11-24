import React from 'react';
import { render, screen, within } from '@testing-library/react';
import { SessionList } from '../SessionList';
import { makeSession } from '../../../testUtils/monitoring';

describe('SessionList "Not live" mark (feature 199)', () => {
  it('shows the mark on the row of a session whose owner queue device is not live', () => {
    render(
      <SessionList
        sessions={[makeSession('1', { ownerQueueDeviceLiveness: 'not_live' }), makeSession('2', { ownerQueueDeviceLiveness: 'live' }), makeSession('3')]}
        selectedId={null}
        onSelect={() => {}}
      />
    );

    expect(within(screen.getByTestId('session-row-1')).getByText('Not live')).toBeInTheDocument();
    expect(within(screen.getByTestId('session-row-2')).queryByText('Not live')).not.toBeInTheDocument();
    expect(within(screen.getByTestId('session-row-3')).queryByText('Not live')).not.toBeInTheDocument();
  });

  it('shows the mark on a phone card', () => {
    render(
      <SessionList sessions={[makeSession('1', { ownerQueueDeviceLiveness: 'not_live' })]} selectedId={null} onSelect={() => {}} phone />
    );

    expect(within(screen.getByTestId('session-card-1')).getByText('Not live')).toBeInTheDocument();
  });
});
