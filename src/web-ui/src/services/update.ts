import { apiGet, apiPost } from '../lib/api';

export type UpdateCheckStatus = 'upToDate' | 'updateAvailable' | 'checkFailed';

export type UpdateState = 'downloading' | 'verifying' | 'installing' | 'restarting' | 'succeeded' | 'failed';

export interface UpdateErrorInfo {
  code: string;
  message: string;
  hint?: string | null;
}

export interface UpdateCheckResult {
  status: UpdateCheckStatus;
  installedVersion: string;
  latestVersion?: string | null;
  notes?: string | null;
  checkedAtUtc: string;
  error?: UpdateErrorInfo | null;
}

export interface UpdateAttempt {
  attemptId: string;
  state: UpdateState;
  targetVersion: string;
  fromVersion: string;
  startedAtUtc: string;
  finishedAtUtc?: string | null;
  errorCode?: string | null;
  errorMessage?: string | null;
  errorHint?: string | null;
  msiexecExitCode?: number | null;
  logPath?: string | null;
}

export type InstallBlockedReason = 'remote' | 'notInstalled';

export interface UpdateStatus {
  installedVersion: string;
  lastCheck?: UpdateCheckResult | null;
  attempt?: UpdateAttempt | null;
  lastResult?: UpdateAttempt | null;
  canInstallHere: boolean;
  installBlockedReason?: InstallBlockedReason | null;
}

export interface InstallAccepted {
  attemptId: string;
  state: UpdateState;
}

/** An error answer of an update route: the body is { error: { code, message, hint } }. */
export class UpdateApiError extends Error {
  status: number;
  code: string;
  hint?: string | null;
  constructor(status: number, code: string, message: string, hint?: string | null) {
    super(message);
    this.status = status;
    this.code = code;
    this.hint = hint;
  }
}

const readJson = async (res: Response): Promise<any> => {
  try {
    return await res.json();
  } catch {
    return undefined;
  }
};

const toError = async (res: Response): Promise<UpdateApiError> => {
  const body = await readJson(res);
  const error = body?.error;
  return new UpdateApiError(
    res.status,
    typeof error?.code === 'string' ? error.code : `http_${res.status}`,
    typeof error?.message === 'string' ? error.message : `HTTP ${res.status}`,
    typeof error?.hint === 'string' ? error.hint : null
  );
};

export async function getUpdateStatus(): Promise<UpdateStatus> {
  const res = await apiGet('/api/update/status');
  if (!res.ok) throw await toError(res);
  return (await res.json()) as UpdateStatus;
}

export async function checkForUpdate(): Promise<UpdateCheckResult> {
  const res = await apiPost('/api/update/check', undefined);
  if (!res.ok) throw await toError(res);
  return (await res.json()) as UpdateCheckResult;
}

export async function installUpdate(targetVersion: string): Promise<InstallAccepted> {
  const res = await apiPost('/api/update/install', { targetVersion, confirmStopQueues: true });
  if (!res.ok) throw await toError(res);
  return (await res.json()) as InstallAccepted;
}
