import React from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { App } from '../App';

jest.mock('../pages/actions/ActionsListPage', () => ({ ActionsListPage: () => <div role="heading" aria-level={2}>Actions</div> }));
jest.mock('../pages/CommandsPage', () => ({ CommandsPage: () => <div role="heading" aria-level={2}>Commands</div> }));
jest.mock('../pages/GamesPage', () => ({ GamesPage: () => <div role="heading" aria-level={2}>Games</div> }));
jest.mock('../pages/SequencesPage', () => ({ SequencesPage: () => <div role="heading" aria-level={2}>Sequences</div> }));

describe('Execution placeholder', () => {
  it('shows empty-state messaging and allows returning', () => {
    render(<App />);
    fireEvent.click(screen.getByRole('tab', { name: 'Execution' }));
    expect(screen.getByText(/execution workspace/i)).toBeInTheDocument();
    fireEvent.click(screen.getByRole('tab', { name: 'Authoring' }));
    expect(screen.getByRole('heading', { name: 'Authoring' })).toBeInTheDocument();
  });
});