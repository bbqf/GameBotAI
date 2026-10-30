/**
 * Shared set-up for the feature 169 page tests. A test file that uses it must call the jest.mock
 * lines for the services and the drag library itself, because jest.mock needs a literal in the file.
 */
import React from 'react';
import { DndContext, useSensors } from '@dnd-kit/core';
import { arrayMove } from '@dnd-kit/sortable';
import { listCommands, getCommand, updateCommand } from '../../services/commands';
import { listGames } from '../../services/games';

export const listCommandsMock = listCommands as jest.MockedFunction<typeof listCommands>;
export const getCommandMock = getCommand as jest.MockedFunction<typeof getCommand>;
export const updateCommandMock = updateCommand as jest.MockedFunction<typeof updateCommand>;
export const listGamesMock = listGames as jest.MockedFunction<typeof listGames>;

/** Re-applies the mocks that `jest.resetAllMocks` clears. */
export const resetPageMocks = (): void => {
  (DndContext as unknown as jest.Mock).mockImplementation(({ children }: any) =>
    React.createElement(React.Fragment, null, children));
  (useSensors as jest.Mock).mockReturnValue([]);
  (arrayMove as jest.Mock).mockImplementation((arr: unknown[], from: number, to: number) => {
    const result = [...arr];
    const [item] = result.splice(from, 1);
    result.splice(to, 0, item);
    return result;
  });
  listGamesMock.mockResolvedValue([] as any);
};

/** Makes the list and the detail call give one command. */
export const serveCommand = (command: any): void => {
  listCommandsMock.mockResolvedValue([command] as any);
  getCommandMock.mockResolvedValue(command as any);
};
