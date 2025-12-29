import React from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { App } from '../App';

jest.mock('../pages/actions/ActionsListPage', () => ({ ActionsListPage: () => <div role="heading" aria-level={2}>Actions</div> }));
jest.mock('../pages/CommandsPage', () => ({ CommandsPage: () => <div role="heading" aria-level={2}>Commands</div> }));
jest.mock('../pages/GamesPage', () => ({ GamesPage: () => <div role="heading" aria-level={2}>Games</div> }));
jest.mock('../pages/SequencesPage', () => ({ SequencesPage: () => <div role="heading" aria-level={2}>Sequences</div> }));

describe('Configuration area', () => {
  it('shows host/token controls at the top', () => {
    render(<App />);
    fireEvent.click(screen.getByRole('tab', { name: 'Configuration' }));
    expect(screen.getByLabelText('API Base URL')).toBeInTheDocument();
    expect(screen.getByLabelText('Bearer Token')).toBeInTheDocument();
  });
});