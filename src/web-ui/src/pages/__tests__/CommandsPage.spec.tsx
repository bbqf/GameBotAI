import React from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { CommandsPage } from '../CommandsPage';
import { listCommands, createCommand, getCommand, updateCommand } from '../../services/commands';
import { listGames } from '../../services/games';

jest.mock('../../services/commands');
jest.mock('../../services/games', () => ({
  listGames: jest.fn()
}));

const listCommandsMock = listCommands as jest.MockedFunction<typeof listCommands>;
const createCommandMock = createCommand as jest.MockedFunction<typeof createCommand>;
const getCommandMock = getCommand as jest.MockedFunction<typeof getCommand>;
const updateCommandMock = updateCommand as jest.MockedFunction<typeof updateCommand>;
const listGamesMock = listGames as jest.MockedFunction<typeof listGames>;

describe('CommandsPage', () => {
  beforeEach(() => {
    jest.resetAllMocks();
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

  it('reorders command steps and preserves order on save', async () => {
    listCommandsMock.mockResolvedValue([{ id: 'c1', name: 'Cmd', steps: [
      { type: 'Command', targetId: 'nested1', order: 0 },
      { type: 'Command', targetId: 'nested2', order: 1 },
    ] } as any]);
    getCommandMock.mockResolvedValue({ id: 'c1', name: 'Cmd', steps: [
      { type: 'Command', targetId: 'nested1', order: 0 },
      { type: 'Command', targetId: 'nested2', order: 1 },
    ] } as any);
    updateCommandMock.mockResolvedValue({} as any);

    render(<CommandsPage />);
    await screen.findByText('Cmd');
    fireEvent.click(screen.getByText('Cmd'));

    await screen.findByText('Edit Command');

    const stepsSection = screen.getByRole('heading', { name: 'Steps', level: 3 }).closest('section')!;
    await waitFor(() => {
      const moveUpButtons = stepsSection.querySelectorAll('button[aria-label="Move up"]');
      expect(moveUpButtons.length).toBeGreaterThan(0);
    });
    const moveUpButtons = stepsSection.querySelectorAll('button[aria-label="Move up"]');
    fireEvent.click(moveUpButtons[moveUpButtons.length - 1]);

    fireEvent.click(screen.getAllByText('Save')[0]);

    await waitFor(() => expect(updateCommandMock).toHaveBeenCalledWith('c1', {
      name: 'Cmd',
      steps: [
        { type: 'Command', targetId: 'nested2', order: 0 },
        { type: 'Command', targetId: 'nested1', order: 1 },
      ],
      detection: undefined,
    }));
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
