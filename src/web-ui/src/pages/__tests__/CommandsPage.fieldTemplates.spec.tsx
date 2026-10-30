import React from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { CommandsPage } from '../CommandsPage';
import { getCommandMock, resetPageMocks, serveCommand, updateCommandMock } from './commandsPageTestSetup';

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

describe('CommandsPage field templates round trip (feature 169)', () => {
  beforeEach(() => {
    jest.resetAllMocks();
    resetPageMocks();
    updateCommandMock.mockResolvedValue({} as any);
  });

  const open = async () => {
    render(<CommandsPage />);
    await screen.findByText('Cmd');
    fireEvent.click(screen.getByText('Cmd'));
    await screen.findByText('Edit Command');
  };

  it('loads and saves the templates of every step type without change', async () => {
    serveCommand({
      id: 'c1',
      name: 'Cmd',
      steps: [
        {
          type: 'Swipe', order: 0,
          swipe: { startX: 1, startY: 2, endX: 3, endY: 4, durationMs: 100 },
          fieldTemplates: { 'swipe.durationMs': '{{delay}}' },
        },
        {
          // This path has no control in the editor. Its template must stay.
          type: 'EnsureGameRunning', order: 1,
          fieldTemplates: { 'ensureGameRunning.readinessTimeoutMs': '{{delay}}' },
        },
        {
          type: 'PrimitiveTap', order: 2,
          primitiveTap: { detectionTarget: { referenceImageId: 'img-1' } },
          fieldTemplates: { 'primitiveTap.detectionTarget.referenceImageId': '{{button}}' },
        },
      ],
      parameters: [{ name: 'delay', type: 'number', required: false }],
    });
    await open();
    fireEvent.click(screen.getAllByText('Save')[0]);

    await waitFor(() => expect(updateCommandMock).toHaveBeenCalled());
    const body = updateCommandMock.mock.calls[0][1] as any;
    expect(body.steps[0].fieldTemplates).toEqual({ 'swipe.durationMs': '{{delay}}' });
    expect(body.steps[1].fieldTemplates).toEqual({ 'ensureGameRunning.readinessTimeoutMs': '{{delay}}' });
    expect(body.steps[2].fieldTemplates).toEqual({ 'primitiveTap.detectionTarget.referenceImageId': '{{button}}' });
  });

  it('does not send an empty map', async () => {
    serveCommand({
      id: 'c1',
      name: 'Cmd',
      steps: [
        { type: 'Swipe', order: 0, swipe: { startX: 1, startY: 2, endX: 3, endY: 4 }, fieldTemplates: {} },
        { type: 'GoToHomeScreen', order: 1 },
      ],
    });
    await open();
    fireEvent.click(screen.getAllByText('Save')[0]);

    await waitFor(() => expect(updateCommandMock).toHaveBeenCalled());
    const body = updateCommandMock.mock.calls[0][1] as any;
    expect(body.steps[0]).not.toHaveProperty('fieldTemplates');
    expect(body.steps[1]).not.toHaveProperty('fieldTemplates');
    expect(getCommandMock).toHaveBeenCalled();
  });
});
