import { ApiError, buildApiUrl, buildAuthHeaders } from '../lib/api';

export interface BackupSelection {
  commandIds: string[];
  sequenceIds: string[];
}

export interface ConflictReport {
  hasConflicts: boolean;
  conflictingCommandNames: string[];
  conflictingSequenceNames: string[];
  conflictingImageIds: string[];
  totalCommands: number;
  totalSequences: number;
  totalImages: number;
}

export interface RestoreResult {
  restoredCommands: number;
  restoredSequences: number;
  restoredImages: number;
  rolledBack: boolean;
  errorMessage?: string;
}

export async function downloadBackup(selection: BackupSelection): Promise<void> {
  const res = await fetch(buildApiUrl('/api/authoring/backup'), {
    method: 'POST',
    headers: buildAuthHeaders(true),
    body: JSON.stringify(selection)
  });
  if (!res.ok) {
    const payload = await res.json().catch(() => undefined);
    const message = payload?.error ?? `HTTP ${res.status}`;
    throw new ApiError(res.status, typeof message === 'string' ? message : JSON.stringify(message), undefined, payload);
  }
  const blob = await res.blob();
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  const disposition = res.headers.get('content-disposition') ?? '';
  const match = disposition.match(/filename="?([^"]+)"?/);
  a.download = match?.[1] ?? 'gamebot-backup.zip';
  a.href = url;
  a.style.display = 'none';
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  URL.revokeObjectURL(url);
}

export async function validateRestore(file: File): Promise<ConflictReport> {
  const form = new FormData();
  form.append('archive', file);
  const res = await fetch(buildApiUrl('/api/authoring/restore/dry-run'), {
    method: 'POST',
    headers: buildAuthHeaders(false),
    body: form
  });
  if (!res.ok) {
    const payload = await res.json().catch(() => undefined);
    const message = payload?.error ?? `HTTP ${res.status}`;
    throw new ApiError(res.status, typeof message === 'string' ? message : JSON.stringify(message), undefined, payload);
  }
  return res.json() as Promise<ConflictReport>;
}

export async function applyRestore(file: File): Promise<RestoreResult> {
  const form = new FormData();
  form.append('archive', file);
  const res = await fetch(buildApiUrl('/api/authoring/restore/apply'), {
    method: 'POST',
    headers: buildAuthHeaders(false),
    body: form
  });
  const payload = await res.json().catch(() => undefined);
  if (!res.ok && res.status !== 500) {
    const message = payload?.error ?? `HTTP ${res.status}`;
    throw new ApiError(res.status, typeof message === 'string' ? message : JSON.stringify(message), undefined, payload);
  }
  return payload as RestoreResult;
}
