import React from 'react';
import { render, screen } from '@testing-library/react';
import { Navigation } from '../Navigation';
import { navigationAreas } from '../../types/navigation';

describe('Navigation', () => {
  it('renders top-level areas in order with Queues after Authoring and Monitoring after Queues', () => {
    render(
      <Navigation
        areas={navigationAreas}
        activeArea="authoring"
        onChange={() => undefined}
      />
    );

    const tabs = screen.getAllByRole('tab').map((item) => item.textContent?.trim());
    expect(tabs).toEqual(['Queues', 'Monitoring', 'Authoring', 'Execution', 'Execution Logs', 'Configuration', 'Update', 'Gamepacks', 'Notifications']);
  });
});
