import { apiGet, apiPost, buildApiUrl, buildAuthHeaders } from '../lib/api';

export interface CatalogSettings {
  location: string | null;
  allowInsecure: boolean;
  tokenSet: boolean;
}

/** The settings to save. A null token keeps the saved token. An empty text clears it. */
export interface CatalogSettingsRequest {
  location: string | null;
  allowInsecure: boolean;
  token: string | null;
}

export interface CatalogVersion {
  version: string;
  changelog: string;
  installed: boolean;
  updateAvailable: boolean;
}

export interface CatalogGamepack {
  name: string;
  installedVersion?: string | null;
  versions: CatalogVersion[];
}

export interface CatalogList {
  gamepacks: CatalogGamepack[];
}

export interface CatalogInstallResult {
  dryRun: boolean;
  valid: boolean;
  problems?: Array<{ code?: string; message?: string }>;
  gamepack?: { name: string; version: string } | null;
  created: number;
  updated: number;
  removed: number;
  result?: string | null;
}

/** An error answer of a catalog route. The body has the problem format with `code` and `message`. */
export class CatalogApiError extends Error {
  status: number;
  code: string;
  constructor(status: number, code: string, message: string) {
    super(message);
    this.status = status;
    this.code = code;
  }
}

const readJson = async (res: Response): Promise<any> => {
  try {
    return await res.json();
  } catch {
    return undefined;
  }
};

const toError = async (res: Response): Promise<CatalogApiError> => {
  const body = await readJson(res);
  const code = typeof body?.code === 'string' ? body.code : `http_${res.status}`;
  const message = typeof body?.message === 'string' ? body.message : typeof body?.title === 'string' ? body.title : `HTTP ${res.status}`;
  return new CatalogApiError(res.status, code, message);
};

export async function getCatalogSettings(): Promise<CatalogSettings> {
  const res = await apiGet('/api/gamepacks/catalog/settings');
  if (!res.ok) throw await toError(res);
  return (await res.json()) as CatalogSettings;
}

export async function putCatalogSettings(request: CatalogSettingsRequest): Promise<CatalogSettings> {
  const res = await fetch(buildApiUrl('/api/gamepacks/catalog/settings'), {
    method: 'PUT',
    headers: buildAuthHeaders(true),
    body: JSON.stringify(request)
  });
  if (!res.ok) throw await toError(res);
  return (await res.json()) as CatalogSettings;
}

export async function listCatalog(): Promise<CatalogList> {
  const res = await apiGet('/api/gamepacks/catalog');
  if (!res.ok) throw await toError(res);
  return (await res.json()) as CatalogList;
}

/** Installs one listed version. With dryRun true, the server checks the install and changes nothing. */
export async function installFromCatalog(name: string, version: string, dryRun: boolean): Promise<CatalogInstallResult> {
  const res = await apiPost('/api/gamepacks/catalog/install', { name, version, dryRun });
  // A 422 carries the install result body (for example a signature rejection).
  if (res.ok || res.status === 422) {
    const body = (await readJson(res)) as CatalogInstallResult;
    if (res.ok) return body;
    const first = body?.problems?.[0]?.message;
    throw new CatalogApiError(422, body?.result ?? 'install_rejected', first ?? 'The install was rejected. Nothing changed.');
  }
  throw await toError(res);
}
