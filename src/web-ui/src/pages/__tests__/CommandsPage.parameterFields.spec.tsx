import React from 'react';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { CommandsPage } from '../CommandsPage';
import { resetPageMocks, serveCommand, updateCommandMock } from './commandsPageTestSetup';

jest.mock('../../services/commands');
jest.mock('../../services/games', () => ({ listGames: jest.fn() }));
jest.mock('@dnd-kit/core', () => ({
  DndContext: jest.fn(),
  PointerSensor: class {},
  useSensor: jest.fn(),
  useSensors: jest.fn(),
}));
jest.mock('@dnd-kit/sortable', () => ({
  SortableContext: ({ children }: { children: React.ReactNode }) => <>{children}</>,
  verticalListSortingStrategy: {},
  useSortable: () => ({ attributes: {}, listeners: {}, setNodeRef: jest.fn(), transform: null, transition: undefined, isDragging: false }),
  arrayMove: jest.fn(),
}));
jest.mock('@dnd-kit/utilities', () => ({ CSS: { Translate: { toString: () => '' }, Transform: { toString: () => '' } } }));
jest.mock('../../components/images/ImageSelectorDropdown', () => ({
  ImageSelectorDropdown: ({ id, label, value, onChange }: any) => (
    <>
      {label && <label htmlFor={id}>{label}</label>}
      <input id={id} value={value} onChange={(e) => onChange(e.target.value)} />
    </>
  ),
}));

const swipeStep = (fieldTemplates?: Record<string, string>) => ({
  type: 'Swipe',
  order: 0,
  swipe: { startX: 1, startY: 2, endX: 3, endY: 4, durationMs: 10 },
  ...(fieldTemplates ? { fieldTemplates } : {}),
});

describe('CommandsPage parameter fields with the real control (feature 169)', () => {
  beforeEach(() => {
    jest.resetAllMocks();
    resetPageMocks();
    updateCommandMock.mockResolvedValue({} as any);
  });

  const openStep = async () => {
    render(<CommandsPage />);
    await screen.findByText('Cmd');
    fireEvent.click(screen.getByText('Cmd'));
    await screen.findByText('Edit Command');
    fireEvent.click(screen.getByRole('button', { name: 'Edit' }));
    return document.querySelector('.action-panel--swipe') as HTMLElement;
  };

  it('shows the hint for a text parameter in the Swipe duration, and the person can select it', async () => {
    serveCommand({
      id: 'c1', name: 'Cmd', steps: [swipeStep()],
      parameters: [{ name: 'label', type: 'text', required: false }],
    });
    const panel = await openStep();
    fireEvent.click(within(panel).getByRole('button', { name: 'Use a parameter for Duration (ms)' }));
    const option = within(panel).getByRole('option', { name: /label/ });
    expect(option).toHaveAccessibleDescription(
      'This parameter gives text. A numeric field needs a number. The save will fail.',
    );
    fireEvent.click(option);
    expect(within(panel).getByTestId('chip-swipe.durationMs')).toHaveTextContent('label');
    fireEvent.click(within(panel).getByRole('button', { name: 'Save' }));
    fireEvent.click(screen.getAllByText('Save')[0]);

    await waitFor(() => expect(updateCommandMock).toHaveBeenCalled());
    expect((updateCommandMock.mock.calls[0][1] as any).steps[0].fieldTemplates)
      .toEqual({ 'swipe.durationMs': '{{label}}' });
  });

  it('shows the empty state for a command with no parameters', async () => {
    serveCommand({ id: 'c1', name: 'Cmd', steps: [swipeStep()] });
    const panel = await openStep();
    fireEvent.click(within(panel).getByRole('button', { name: 'Use a parameter for Start X' }));
    expect(within(panel).getByText('This command has no parameters. Add one in the Parameters section.'))
      .toBeInTheDocument();
  });

  it('shows "Parameter not found" for a deleted parameter and still sends the save', async () => {
    serveCommand({
      id: 'c1', name: 'Cmd', steps: [swipeStep({ 'swipe.durationMs': '{{gone}}' })],
      parameters: [],
    });
    const panel = await openStep();
    expect(within(panel).getByText('Parameter not found')).toBeInTheDocument();
    fireEvent.click(within(panel).getByRole('button', { name: 'Save' }));
    fireEvent.click(screen.getAllByText('Save')[0]);

    await waitFor(() => expect(updateCommandMock).toHaveBeenCalled());
    expect((updateCommandMock.mock.calls[0][1] as any).steps[0].fieldTemplates)
      .toEqual({ 'swipe.durationMs': '{{gone}}' });
  });
});
