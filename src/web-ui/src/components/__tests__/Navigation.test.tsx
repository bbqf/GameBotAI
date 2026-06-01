import React from 'react';
import { render, screen } from '@testing-library/react';
import { Navigation } from '../Navigation';
import { navigationAreas } from '../../types/navigation';

describe('Navigation', () => {
  it('renders top-level areas in order with Queues after Authoring', () => {
    render(
      <Navigation
        areas={navigationAreas}
        activeArea="authoring"
        onChange={() => undefined}
        isCollapsed={false}
      />
    );

    const tabs = screen.getAllByRole('tab').map((item) => item.textContent?.trim());
    expect(tabs).toEqual(['Authoring', 'Queues', 'Execution', 'Execution Logs', 'Configuration']);
  });
});
