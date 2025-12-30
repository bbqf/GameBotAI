import { getJson } from '../lib/api';

export type ImageListResponse = {
  ids?: string[];
};

export const listImages = async (): Promise<string[]> => {
  const data = await getJson<ImageListResponse>('/api/images');
  if (data && Array.isArray(data.ids)) {
    return data.ids.filter((id): id is string => typeof id === 'string');
  }
  return [];
};
