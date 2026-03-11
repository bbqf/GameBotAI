import React from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import { SequencesPage } from '../SequencesPage';
import { listSequences } from '../../services/sequences';
import { listCommands } from '../../services/commands';

jest.mock('../../services/sequences');
jest.mock('../../services/commands');

const listSequencesMock = listSequences as jest.MockedFunction<typeof listSequences>;
const listCommandsMock = listCommands as jest.MockedFunction<typeof listCommands>;

describe('SequencesPage no-branching authoring', () => {
  beforeEach(() => {
    jest.resetAllMocks();
    listSequencesMock.mockResolvedValue([] as any);
    listCommandsMock.mockResolvedValue([
      { id: 'cmd-home', name: 'Home' },
      { id: 'cmd-back', name: 'Back' }
    ] as any);
  });

  it('does not render entry-step or branch-link controls', async () => {
    render(<SequencesPage />);

    await waitFor(() => expect(listSequencesMock).toHaveBeenCalled());

    expect(screen.queryByLabelText(/Entry Step/i)).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/True Target/i)).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/False Target/i)).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/Enable per-step conditions/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/Per-Step Conditions/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/Conditional Flow/i)).not.toBeInTheDocument();
  });
});
