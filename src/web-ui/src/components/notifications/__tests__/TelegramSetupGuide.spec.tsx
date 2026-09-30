import React from 'react';
import { render, screen, within } from '@testing-library/react';
import { TelegramSetupGuide } from '../TelegramSetupGuide';

describe('TelegramSetupGuide', () => {
  it('shows 6 steps', () => {
    render(<TelegramSetupGuide />);

    const steps = within(screen.getByRole('list')).getAllByRole('listitem');
    expect(steps).toHaveLength(6);
  });

  it('names the form field for each value', () => {
    render(<TelegramSetupGuide />);

    const steps = within(screen.getByRole('list')).getAllByRole('listitem');
    // Step 3 gives the token. Step 6 gives the chat ID.
    expect(steps[2]).toHaveTextContent('Bot token');
    expect(steps[5]).toHaveTextContent('Chat ID');
    expect(screen.getByText(/@BotFather/)).toBeInTheDocument();
    expect(screen.getByText('/newbot')).toBeInTheDocument();
  });
});
