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

jest.mock('../../components/images/ImageSelectorDropdown', () => ({
  ImageSelectorDropdown: ({ id, label, value, onChange, disabled }: {
    id?: string; label?: string; value: string; onChange: (v: string) => void; disabled?: boolean;
  }) => (
    <>
      {label && <label htmlFor={id}>{label}</label>}
      <input id={id} value={value} disabled={disabled} onChange={(e) => onChange(e.target.value)} />
    </>
  ),
}));

jest.mock('@dnd-kit/core', () => {
  const React = jest.requireActual('react');
  return {
    DndContext: jest.fn(({ children }: any) => React.createElement(React.Fragment, null, children)),
    PointerSensor: class {},
    useSensor: jest.fn(),
    useSensors: jest.fn(() => []),
  };
});

jest.mock('@dnd-kit/sortable', () => ({
  SortableContext: ({ children }: { children: React.ReactNode }) => <>{children}</>,
  verticalListSortingStrategy: {},
  useSortable: () => ({
    attributes: {},
    listeners: {},
    setNodeRef: jest.fn(),
    transform: null,
    transition: undefined,
    isDragging: false,
  }),
  arrayMove: jest.fn((arr: unknown[], from: number, to: number) => {
    const result = [...arr];
    const [item] = result.splice(from, 1);
    result.splice(to, 0, item);
    return result;
  }),
}));

jest.mock('@dnd-kit/utilities', () => ({
  CSS: { Translate: { toString: () => '' }, Transform: { toString: () => '' } },
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
    fireEvent.change(screen.getByRole('combobox', { name: /action type/i }), { target: { value: 'PrimitiveTap' } });
    fireEvent.change(screen.getByLabelText('Reference image *'), { target: { value: tapImageId } });
    fireEvent.change(screen.getByLabelText('Confidence (0–1)'), { target: { value: `${tapConfidence}` } });
    fireEvent.click(screen.getByRole('button', { name: 'Add' }));
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
    const firstStep = payload.steps?.[0] as any;
    expect(firstStep.primitiveAction?.type).toBe('command');
    expect(firstStep.primitiveAction?.payload?.commandId).toBe(nestedCommandId);
  });
});
