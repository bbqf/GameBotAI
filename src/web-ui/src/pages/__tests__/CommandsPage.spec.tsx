import React from 'react';
import { fireEvent, render, screen, waitFor, act } from '@testing-library/react';
import { CommandsPage } from '../CommandsPage';
import { listCommands, createCommand, getCommand, updateCommand } from '../../services/commands';
import { listGames } from '../../services/games';

jest.mock('../../services/commands');
jest.mock('../../services/games', () => ({
  listGames: jest.fn()
}));

jest.mock('@dnd-kit/core', () => {
  const React = require('react');
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

import { DndContext, useSensors } from '@dnd-kit/core';
import { arrayMove } from '@dnd-kit/sortable';

const listCommandsMock = listCommands as jest.MockedFunction<typeof listCommands>;
const createCommandMock = createCommand as jest.MockedFunction<typeof createCommand>;
const getCommandMock = getCommand as jest.MockedFunction<typeof getCommand>;
const updateCommandMock = updateCommand as jest.MockedFunction<typeof updateCommand>;
const listGamesMock = listGames as jest.MockedFunction<typeof listGames>;

const dndHandlers: {
  onDragEnd?: (e: any) => void;
  onDragStart?: (e: any) => void;
  onDragOver?: (e: any) => void;
  onDragCancel?: () => void;
} = {};

describe('CommandsPage', () => {
  beforeEach(() => {
    jest.resetAllMocks();
    // Re-apply DndContext implementation after resetAllMocks clears it
    (DndContext as jest.Mock).mockImplementation(({ children, onDragEnd, onDragStart, onDragOver, onDragCancel }: any) => {
      dndHandlers.onDragEnd = onDragEnd;
      dndHandlers.onDragStart = onDragStart;
      dndHandlers.onDragOver = onDragOver;
      dndHandlers.onDragCancel = onDragCancel;
      return React.createElement(React.Fragment, null, children);
    });
    (useSensors as jest.Mock).mockReturnValue([]);
    (arrayMove as jest.Mock).mockImplementation((arr: unknown[], from: number, to: number) => {
      const result = [...arr];
      const [item] = result.splice(from, 1);
      result.splice(to, 0, item);
      return result;
    });
    listCommandsMock.mockResolvedValue([] as any);
    listGamesMock.mockResolvedValue([] as any);
  });

  it('creates a command with validation', async () => {
    render(<CommandsPage />);

    await waitFor(() => expect(listCommandsMock).toHaveBeenCalled());

    fireEvent.click(screen.getByText('Create Command'));
    fireEvent.click(screen.getByText('Save'));
    expect(await screen.findByText('Name is required')).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText('Name *'), { target: { value: 'Test Cmd' } });
    fireEvent.change(screen.getByLabelText('Primitive tap image ID'), { target: { value: 'alliance_button' } });
    fireEvent.click(screen.getByText('Add primitive tap step'));

    createCommandMock.mockResolvedValue({} as any);

    fireEvent.click(screen.getByText('Save'));

    await waitFor(() => expect(createCommandMock).toHaveBeenCalledWith({
      name: 'Test Cmd',
      steps: [{
        type: 'PrimitiveTap',
        order: 0,
        primitiveTap: {
          detectionTarget: {
            referenceImageId: 'alliance_button',
            confidence: undefined,
            offsetX: 0,
            offsetY: 0,
          }
        }
      }],
      detection: undefined
    }));
  });

  it('loads and saves an existing command', async () => {
    listCommandsMock.mockResolvedValue([{ id: 'c1', name: 'Cmd', steps: [{ type: 'Command', targetId: 'nested1', order: 0 }] } as any]);
    getCommandMock.mockResolvedValue({ id: 'c1', name: 'Cmd', steps: [{ type: 'Command', targetId: 'nested1', order: 0 }] } as any);
    updateCommandMock.mockResolvedValue({} as any);

    render(<CommandsPage />);
    await screen.findByText('Cmd');
    fireEvent.click(screen.getByText('Cmd'));

    await screen.findByText('Edit Command');
    fireEvent.change(screen.getByLabelText('Name *'), { target: { value: 'Cmd Updated' } });
    fireEvent.click(screen.getAllByText('Save')[0]);

    await waitFor(() => expect(updateCommandMock).toHaveBeenCalledWith('c1', {
      name: 'Cmd Updated',
      steps: [{ type: 'Command', targetId: 'nested1', order: 0 }],
      detection: undefined,
    }));
  });

  it('reorders command steps via drag and drop and preserves order on save', async () => {
    listCommandsMock.mockResolvedValue([{ id: 'c1', name: 'Cmd', steps: [
      { type: 'Command', targetId: 'nested1', order: 0 },
      { type: 'Command', targetId: 'nested2', order: 1 },
    ] } as any]);
    getCommandMock.mockResolvedValue({ id: 'c1', name: 'Cmd', steps: [
      { type: 'Command', targetId: 'nested1', order: 0 },
      { type: 'Command', targetId: 'nested2', order: 1 },
    ] } as any);
    updateCommandMock.mockResolvedValue({} as any);

    // Use predictable step IDs so drag handlers can reference them
    let idCount = 0;
    const uuidSpy = jest.spyOn(global.crypto, 'randomUUID').mockImplementation(() => `step-${++idCount}` as any);

    render(<CommandsPage />);
    await screen.findByText('Cmd');
    fireEvent.click(screen.getByText('Cmd'));

    await screen.findByText('Edit Command');
    // Confirm drag handles are present (no arrow buttons)
    const stepsSection = screen.getByRole('heading', { name: 'Steps', level: 3 }).closest('section')!;
    await waitFor(() => {
      expect(stepsSection.querySelectorAll('[aria-label="Drag to reorder"]').length).toBeGreaterThan(0);
    });

    // step-1 = nested1, step-2 = nested2; drag step-2 onto step-1 to move nested2 to top
    act(() => {
      dndHandlers.onDragEnd?.({ active: { id: 'step-2' }, over: { id: 'step-1' } });
    });

    fireEvent.click(screen.getAllByText('Save')[0]);

    await waitFor(() => expect(updateCommandMock).toHaveBeenCalledWith('c1', {
      name: 'Cmd',
      steps: [
        { type: 'Command', targetId: 'nested2', order: 0 },
        { type: 'Command', targetId: 'nested1', order: 1 },
      ],
      detection: undefined,
    }));

    uuidSpy.mockRestore();
  });

  it('renders long command names without clipping or losing the full title', async () => {
    const longName = 'Very Long Command Name That Should Ellipsize Without Causing Horizontal Scrollbars';
    listCommandsMock.mockResolvedValue([{ id: 'c-long', name: longName, steps: [] } as any]);

    render(<CommandsPage />);

    const nameButton = await screen.findByRole('button', { name: longName });
    expect(nameButton).toHaveAttribute('title', longName);
    const nameSpan = nameButton.querySelector('.command-name');
    expect(nameSpan?.textContent).toBe(longName);
  });

  it('creates a command with a primitive tap step', async () => {
    render(<CommandsPage />);
    await waitFor(() => expect(listCommandsMock).toHaveBeenCalled());

    fireEvent.click(screen.getByText('Create Command'));
    fireEvent.change(screen.getByLabelText('Name *'), { target: { value: 'Primitive Command' } });
    fireEvent.change(screen.getByLabelText('Primitive tap image ID'), { target: { value: 'home_button' } });
    fireEvent.change(screen.getByLabelText('Primitive confidence (0-1)'), { target: { value: '0.95' } });
    fireEvent.change(screen.getByLabelText('Primitive offset X'), { target: { value: '2' } });
    fireEvent.change(screen.getByLabelText('Primitive offset Y'), { target: { value: '-1' } });

    fireEvent.click(screen.getByText('Add primitive tap step'));

    createCommandMock.mockResolvedValue({} as any);
    fireEvent.click(screen.getByText('Save'));

    await waitFor(() => expect(createCommandMock).toHaveBeenCalledWith({
      name: 'Primitive Command',
      steps: [{
        type: 'PrimitiveTap',
        order: 0,
        primitiveTap: {
          detectionTarget: {
            referenceImageId: 'home_button',
            confidence: 0.95,
            offsetX: 2,
            offsetY: -1,
          }
        }
      }],
      detection: undefined,
    }));
  });

  it('creates a command with a command step', async () => {
    listCommandsMock.mockResolvedValue([{ id: 'existing-cmd', name: 'Existing Command', steps: [] } as any]);

    render(<CommandsPage />);
    await waitFor(() => expect(listCommandsMock).toHaveBeenCalled());

    fireEvent.click(screen.getByText('Create Command'));
    fireEvent.change(screen.getByLabelText('Name *'), { target: { value: 'Composite Command' } });
    fireEvent.change(screen.getByLabelText('Add command'), { target: { value: 'existing-cmd' } });
    fireEvent.click(screen.getByText('Add command step'));

    createCommandMock.mockResolvedValue({} as any);
    fireEvent.click(screen.getByText('Save'));

    await waitFor(() => expect(createCommandMock).toHaveBeenCalledWith({
      name: 'Composite Command',
      steps: [{ type: 'Command', targetId: 'existing-cmd', order: 0 }],
      detection: undefined,
    }));
  });

  it('creates a command with a wait for image step', async () => {
    render(<CommandsPage />);
    await waitFor(() => expect(listCommandsMock).toHaveBeenCalled());

    fireEvent.click(screen.getByText('Create Command'));
    fireEvent.change(screen.getByLabelText('Name *'), { target: { value: 'Wait Command' } });
    fireEvent.change(screen.getByLabelText('Wait image ID'), { target: { value: 'home_button' } });
    fireEvent.change(screen.getByLabelText('Wait confidence (0-1)'), { target: { value: '0.91' } });
    fireEvent.change(screen.getByLabelText('Wait timeout (ms)'), { target: { value: '2500' } });
    fireEvent.click(screen.getByText('Add wait for image step'));

    createCommandMock.mockResolvedValue({} as any);
    fireEvent.click(screen.getByText('Save'));

    await waitFor(() => expect(createCommandMock).toHaveBeenCalledWith({
      name: 'Wait Command',
      steps: [{
        type: 'WaitForImage',
        order: 0,
        waitForImage: {
          detectionTarget: {
            referenceImageId: 'home_button',
            confidence: 0.91,
            offsetX: undefined,
            offsetY: undefined,
          },
          timeoutMs: 2500,
        }
      }],
      detection: undefined,
    }));
  });

  it('loads and saves an existing wait for image step', async () => {
    listCommandsMock.mockResolvedValue([{ id: 'c-wait', name: 'Wait Cmd', steps: [{ type: 'WaitForImage', order: 0, waitForImage: { detectionTarget: { referenceImageId: 'mail_icon', confidence: 0.82 }, timeoutMs: 1500 } }] } as any]);
    getCommandMock.mockResolvedValue({ id: 'c-wait', name: 'Wait Cmd', steps: [{ type: 'WaitForImage', order: 0, waitForImage: { detectionTarget: { referenceImageId: 'mail_icon', confidence: 0.82 }, timeoutMs: 1500 } }] } as any);
    updateCommandMock.mockResolvedValue({} as any);

    render(<CommandsPage />);
    await screen.findByText('Wait Cmd');
    fireEvent.click(screen.getByText('Wait Cmd'));

    await screen.findByText('Edit Command');
    expect(screen.getByText('Wait for image: mail_icon')).toBeInTheDocument();

    fireEvent.click(screen.getAllByText('Save')[0]);

    await waitFor(() => expect(updateCommandMock).toHaveBeenCalledWith('c-wait', {
      name: 'Wait Cmd',
      steps: [{
        type: 'WaitForImage',
        order: 0,
        waitForImage: {
          detectionTarget: {
            referenceImageId: 'mail_icon',
            confidence: 0.82,
            offsetX: undefined,
            offsetY: undefined,
          },
          timeoutMs: 1500,
        }
      }],
      detection: undefined,
    }));
  });
});
