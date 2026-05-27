import React from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { CommandsPage } from '../CommandsPage';
import { SequencesPage } from '../SequencesPage';
import { createCommand, listCommands } from '../../services/commands';
import { createSequence, listSequences } from '../../services/sequences';
import { listGames } from '../../services/games';
import { primitiveTapFixture, primitiveCommandFixture } from '../../test/fixtures/primitiveActions';

jest.mock('../../services/commands');
jest.mock('../../services/sequences');
jest.mock('../../services/games', () => ({
  listGames: jest.fn()
}));

const listCommandsMock = listCommands as jest.MockedFunction<typeof listCommands>;
const createCommandMock = createCommand as jest.MockedFunction<typeof createCommand>;
const listSequencesMock = listSequences as jest.MockedFunction<typeof listSequences>;
const createSequenceMock = createSequence as jest.MockedFunction<typeof createSequence>;
const listGamesMock = listGames as jest.MockedFunction<typeof listGames>;

describe('Commands and sequences primitive authoring', () => {
  const tapImageId = primitiveTapFixture.payload.referenceImageId as string;
  const tapConfidence = primitiveTapFixture.payload.confidence as number;
  const nestedCommandId = primitiveCommandFixture.payload.commandId as string;

  beforeEach(() => {
    jest.resetAllMocks();
    listCommandsMock.mockResolvedValue([] as any);
    createCommandMock.mockResolvedValue({ id: 'cmd-1', name: 'Cmd', steps: [] } as any);
    listSequencesMock.mockResolvedValue([] as any);
    createSequenceMock.mockResolvedValue({ id: 'seq-1', name: 'Seq', steps: [] } as any);
    listGamesMock.mockResolvedValue([{ id: 'g1', name: 'Game 1' }] as any);
  });

  it('creates a command with primitive tap selection', async () => {
    render(<CommandsPage />);

    await waitFor(() => expect(listCommandsMock).toHaveBeenCalled());
    fireEvent.click(screen.getByText('Create Command'));
    fireEvent.change(screen.getByLabelText('Name *'), { target: { value: 'Primitive Command' } });
    fireEvent.change(screen.getByLabelText('Primitive tap image ID'), { target: { value: tapImageId } });
    fireEvent.change(screen.getByLabelText('Primitive confidence (0-1)'), { target: { value: `${tapConfidence}` } });
    fireEvent.click(screen.getByText('Add primitive tap step'));
    fireEvent.click(screen.getByText('Save'));

    await waitFor(() => expect(createCommandMock).toHaveBeenCalled());
    const payload = createCommandMock.mock.calls[0][0];
    expect(payload.steps?.[0].type).toBe('PrimitiveTap');
  });

  it('creates a sequence with inline primitive command step', async () => {
    listCommandsMock.mockResolvedValue([{ id: nestedCommandId, name: 'Nested command' }] as any);

    render(<SequencesPage />);

    await waitFor(() => expect(listSequencesMock).toHaveBeenCalled());
    fireEvent.click(screen.getByText('Create Sequence'));
    fireEvent.change(screen.getByLabelText('Name *'), { target: { value: 'Primitive Sequence' } });
    fireEvent.change(screen.getByLabelText('Add command'), { target: { value: nestedCommandId } });
    fireEvent.click(screen.getByText('Add to steps'));
    fireEvent.click(screen.getByText('Save'));

    await waitFor(() => expect(createSequenceMock).toHaveBeenCalled());
    const payload = createSequenceMock.mock.calls[0][0];
    expect(payload.steps?.[0].action?.type).toBe('command');
    expect(payload.steps?.[0].action?.parameters?.commandId).toBe(nestedCommandId);
  });
});
