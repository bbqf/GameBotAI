import React from 'react';
import { render, screen } from '@testing-library/react';
import { Navigation } from '../Navigation';
import { navigationAreas } from '../../types/navigation';

describe('Navigation', () => {
  it('renders execution logs tab between execution and configuration', () => {
    render(
      <Navigation
        areas={navigationAreas}
        activeArea="authoring"
        onChange={() => undefined}
        isCollapsed={false}
      />
    );

    const tabs = screen.getAllByRole('tab').map((item) => item.textContent?.trim());
    expect(tabs).toEqual(['Authoring', 'Execution', 'Execution Logs', 'Configuration']);
  });
});
