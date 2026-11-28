import type { Page } from '@playwright/test';

/**
 * A mocked API for the Commands and Games page tests (feature 206). It holds 3 games and 3 commands.
 * Every other API call gets an empty list.
 */
export const json = { 'content-type': 'application/json' };

export const LONG_GAME_NAME = 'Very long game name '.repeat(12).trim().slice(0, 200);

export type MockGame = { id: string; name: string; packageName?: string };
export type MockCommand = { id: string; name: string; steps: Array<{ type: string; order: number }> };

export type AuthoringMockState = {
  games: MockGame[];
  commands: MockCommand[];
  calls: string[];
  /** The ids whose delete answers 409 with references. */
  conflictIds: Set<string>;
};

export const defaultGames = (): MockGame[] => [
  { id: 'g1', name: 'Alpha Game', packageName: 'com.example.alpha' },
  { id: 'g2', name: 'Beta Game' },
  { id: 'g3', name: LONG_GAME_NAME, packageName: 'com.example.long' },
];

export const defaultCommands = (): MockCommand[] => [
  { id: 'c1', name: 'Open shop', steps: [{ type: 'GoToHomeScreen', order: 0 }, { type: 'GoToHomeScreen', order: 1 }] },
  { id: 'c2', name: 'Close shop', steps: [{ type: 'GoToHomeScreen', order: 0 }] },
  { id: 'c3', name: 'Collect reward', steps: [{ type: 'GoToHomeScreen', order: 0 }] },
];

const references = { sequences: [{ id: 's1', name: 'Daily farm' }, { id: 's2', name: 'Night farm' }] };

export const mockAuthoringApi = async (page: Page): Promise<AuthoringMockState> => {
  const state: AuthoringMockState = { games: defaultGames(), commands: defaultCommands(), calls: [], conflictIds: new Set() };

  await page.route(/fonts\.(googleapis|gstatic)\.com/, (route) =>
    route.fulfill({ status: 200, headers: { 'content-type': 'text/css' }, body: '' }));
  await page.route('**/health', (route) =>
    route.fulfill({ status: 200, headers: json, body: JSON.stringify({ status: 'ok' }) }));
  await page.route('**/api/**', async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname.toLowerCase();
    const method = request.method();
    state.calls.push(`${method} ${path}`);
    const ok = (body: unknown) => route.fulfill({ status: 200, headers: json, body: JSON.stringify(body) });

    const match = /^\/api\/(games|commands)(?:\/([^/]+))?$/.exec(path);
    if (!match) return ok([]);
    const kind = match[1] as 'games' | 'commands';
    const id = match[2];
    const items = (kind === 'games' ? state.games : state.commands) as Array<{ id: string; name: string }>;

    if (!id) {
      if (method === 'GET') return ok(items);
      if (method === 'POST') {
        const created = { id: `${kind[0]}${items.length + 10}`, ...request.postDataJSON() };
        items.push(created);
        return ok(created);
      }
      return ok([]);
    }

    const index = items.findIndex((item) => item.id === id);
    if (method === 'GET') {
      return index >= 0
        ? ok(items[index])
        : route.fulfill({ status: 404, headers: json, body: JSON.stringify({ message: 'Not found' }) });
    }
    if (method === 'PUT') {
      if (index >= 0) items[index] = { ...items[index], ...request.postDataJSON() };
      return ok(items[index] ?? {});
    }
    if (method === 'DELETE') {
      if (state.conflictIds.has(id)) {
        return route.fulfill({
          status: 409,
          headers: json,
          body: JSON.stringify({ message: 'The item is in use.', references }),
        });
      }
      if (index >= 0) items.splice(index, 1);
      return route.fulfill({ status: 204, headers: json, body: '' });
    }
    return ok({});
  });

  return state;
};
