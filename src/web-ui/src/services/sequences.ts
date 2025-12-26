import { deleteJson, getJson, postJson, putJson } from '../lib/api';

export type SequenceDto = {
  id: string;
  name: string;
  steps: string[];
};

export type SequenceCreate = {
  name: string;
  steps?: string[];
};

export type SequenceUpdate = SequenceCreate;

const base = '/api/sequences';

export const listSequences = () => getJson<SequenceDto[]>(base);
export const getSequence = (id: string) => getJson<SequenceDto>(`${base}/${id}`);
export const createSequence = (input: SequenceCreate) => postJson<SequenceDto>(base, input);
export const updateSequence = (id: string, input: SequenceUpdate) => putJson<SequenceDto>(`${base}/${id}`, input);
export const deleteSequence = (id: string) => deleteJson<void>(`${base}/${id}`);
