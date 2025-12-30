import { ApiError, buildApiUrl, buildAuthHeaders, getJson } from '../lib/api';

export type ImageListResponse = {
  ids?: string[];
};

export type ImageMetadata = {
  id: string;
  contentType: string;
  sizeBytes: number;
  filename?: string;
  createdAtUtc?: string;
  updatedAtUtc?: string;
};

const MAX_SIZE_BYTES = 10 * 1024 * 1024;
const ALLOWED_TYPES = ['image/png', 'image/jpeg', 'image/jpg'];

export type DetectMatch = {
  templateId: string;
  score: number;
  x: number;
  y: number;
  width: number;
  height: number;
  overlap: number;
};

export type DetectResponse = {
  matches: DetectMatch[];
  limitsHit: boolean;
};

export const listImages = async (): Promise<string[]> => {
  const data = await getJson<ImageListResponse>('/api/images');
  if (data && Array.isArray(data.ids)) {
    return data.ids.filter((id): id is string => typeof id === 'string');
  }
  return [];
};

export const getImageMetadata = async (id: string) => getJson<ImageMetadata>(`/api/images/${encodeURIComponent(id)}/metadata`);

export const getImageBlob = async (id: string): Promise<Blob> => {
  const res = await fetch(buildApiUrl(`/api/images/${encodeURIComponent(id)}`), {
    method: 'GET',
    headers: buildAuthHeaders(false)
  });
  if (!res.ok) {
    const payload = await res.json().catch(() => undefined);
    const message = (payload?.error && (payload.error.message ?? payload.error.code)) || `HTTP ${res.status}`;
    throw new ApiError(res.status, message, undefined, payload);
  }
  return await res.blob();
};

const validateFile = (file: File) => {
  if (file.size > MAX_SIZE_BYTES) throw new Error('File too large (max 10 MB)');
  if (!ALLOWED_TYPES.includes(file.type.toLowerCase())) throw new Error('Only PNG and JPEG are supported');
};

const uploadForm = (id: string, file: File): FormData => {
  validateFile(file);
  const fd = new FormData();
  fd.append('id', id);
  fd.append('file', file);
  return fd;
};

export const uploadImage = async (id: string, file: File) => {
  const res = await fetch(buildApiUrl('/api/images'), {
    method: 'POST',
    headers: buildAuthHeaders(false),
    body: uploadForm(id, file)
  });
  if (!res.ok) {
    const payload = await res.json().catch(() => undefined);
    const message = (payload?.error && (payload.error.message ?? payload.error.code)) || `HTTP ${res.status}`;
    throw new ApiError(res.status, message, undefined, payload);
  }
  return res.json().catch(() => undefined);
};

export const overwriteImage = async (id: string, file: File) => {
  const res = await fetch(buildApiUrl(`/api/images/${encodeURIComponent(id)}`), {
    method: 'PUT',
    headers: buildAuthHeaders(false),
    body: uploadForm(id, file)
  });
  if (!res.ok) {
    const payload = await res.json().catch(() => undefined);
    const message = (payload?.error && (payload.error.message ?? payload.error.code)) || `HTTP ${res.status}`;
    throw new ApiError(res.status, message, undefined, payload);
  }
  return res.json().catch(() => undefined);
};

export const deleteImage = async (id: string) => {
  const res = await fetch(buildApiUrl(`/api/images/${encodeURIComponent(id)}`), {
    method: 'DELETE',
    headers: buildAuthHeaders(false)
  });
  if (!res.ok && res.status !== 404) {
    const payload = await res.json().catch(() => undefined);
    if (res.status === 409) {
      const blockers: string[] | undefined = payload?.error?.blockingTriggerIds ?? payload?.blockingTriggerIds;
      const msg = blockers && blockers.length > 0
        ? `Image is referenced by triggers: ${blockers.join(', ')}`
        : (payload?.error?.message ?? 'Image is referenced by triggers');
      throw new ApiError(res.status, msg, undefined, payload);
    }

    const message = (payload?.error && (payload.error.message ?? payload.error.code)) || `HTTP ${res.status}`;
    throw new ApiError(res.status, message, undefined, payload);
  }
};

export const detectImage = async (referenceImageId: string, opts?: { threshold?: number; maxResults?: number; overlap?: number; }): Promise<DetectResponse> => {
  const res = await fetch(buildApiUrl('/api/images/detect'), {
    method: 'POST',
    headers: buildAuthHeaders(true),
    body: JSON.stringify({ referenceImageId, ...opts })
  });
  if (!res.ok) {
    const payload = await res.json().catch(() => undefined);
    const message = (payload?.message ?? payload?.error?.message ?? payload?.error?.code) || `HTTP ${res.status}`;
    throw new ApiError(res.status, message, undefined, payload);
  }
  return res.json();
};
