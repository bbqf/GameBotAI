import { getJson, putJson } from '../lib/api';

export type ConfigurationParameter = {
  name: string;
  source: 'Default' | 'File' | 'Environment';
  value: unknown;
  isSecret: boolean;
};

export type ConfigurationSnapshot = {
  generatedAtUtc: string;
  serviceVersion: string | null;
  dynamicPort: number | null;
  refreshCount: number;
  envScanned: number;
  envIncluded: number;
  envExcluded: number;
  parameters: Record<string, ConfigurationParameter>;
};

const base = '/api/config';

export const getConfigSnapshot = () => getJson<ConfigurationSnapshot>(base);

export const updateParameters = (updates: Record<string, string | null>) =>
  putJson<ConfigurationSnapshot>(`${base}/parameters`, { updates });

export const reorderParameters = (orderedKeys: string[]) =>
  putJson<ConfigurationSnapshot>(`${base}/parameters/reorder`, { orderedKeys });

export type ConfigFileSnapshot = {
  fileName: string;
  parameters: Record<string, ConfigurationParameter>;
};

export const getConfigFileParams = (name: string) =>
  getJson<ConfigFileSnapshot>(`${base}/files/${encodeURIComponent(name)}`);

export const updateConfigFileParams = (name: string, updates: Record<string, string | null>) =>
  putJson<ConfigFileSnapshot>(`${base}/files/${encodeURIComponent(name)}`, { updates });
