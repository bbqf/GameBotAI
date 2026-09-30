import { deleteJson, getJson, postJson, putJson } from '../lib/api';

/** A field of a target type. The form is built from the field list. */
export type NotificationFieldDto = {
  key: string;
  label: string;
  secret: boolean;
  required: boolean;
};

export type NotificationTypeDto = {
  type: string;
  displayName: string;
  fields: NotificationFieldDto[];
};

/** A target as the API shows it. The secret value is never in it (feature 120). */
export type NotificationTargetDto = {
  id: string;
  type: string;
  name: string;
  enabled: boolean;
  /** Public values only. For Telegram: chatId. */
  settings: Record<string, string>;
  hasSecret: boolean;
  /** A mask and the last characters of the secret, or null when there is no secret. */
  secretHint: string | null;
  createdAt?: string | null;
  updatedAt?: string | null;
};

/**
 * Body to create or replace a target. On an update, an absent or empty secret keeps the stored
 * secret, so the UI sends no secrets when the token field is empty.
 */
export type NotificationTargetInput = {
  type: string;
  name: string;
  enabled: boolean;
  settings: Record<string, string>;
  secrets?: Record<string, string>;
};

/** The HTTP call worked, so this is also the answer for a failed send. */
export type NotificationTestResult = {
  ok: boolean;
  reason: string | null;
};

const base = '/api/notifications/targets';

export const listNotificationTargets = () => getJson<NotificationTargetDto[]>(base);
export const createNotificationTarget = (input: NotificationTargetInput) =>
  postJson<NotificationTargetDto>(base, input);
export const updateNotificationTarget = (id: string, input: NotificationTargetInput) =>
  putJson<NotificationTargetDto>(`${base}/${id}`, input);
export const deleteNotificationTarget = (id: string) => deleteJson<void>(`${base}/${id}`);
export const testNotificationTarget = (id: string) =>
  postJson<NotificationTestResult>(`${base}/${id}/test`, {});
export const listNotificationTypes = () => getJson<NotificationTypeDto[]>('/api/notifications/types');
