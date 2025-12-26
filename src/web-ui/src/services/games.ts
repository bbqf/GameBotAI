import { deleteJson, getJson, postJson, putJson } from '../lib/api';

export type GameDto = {
  id: string;
  name: string;
  metadata?: Record<string, unknown>;
};

export type GameCreate = {
  name: string;
  metadata?: Record<string, unknown>;
};

export type GameUpdate = GameCreate;

const base = '/api/games';

export const listGames = () => getJson<GameDto[]>(base);
export const getGame = (id: string) => getJson<GameDto>(`${base}/${id}`);
export const createGame = (input: GameCreate) => postJson<GameDto>(base, input);
export const updateGame = (id: string, input: GameUpdate) => putJson<GameDto>(`${base}/${id}`, input);
export const deleteGame = (id: string) => deleteJson<void>(`${base}/${id}`);
