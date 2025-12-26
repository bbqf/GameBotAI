import { getBaseUrl } from './config';
import { token$ } from './token';

const buildUrl = (path: string) => {
  const base = getBaseUrl().trim();
  if (!base) return path; // same-origin
  return base.endsWith('/') ? base.slice(0, -1) + path : base + path;
};

const buildHeaders = () => {
  const headers: Record<string, string> = { 'Content-Type': 'application/json' };
  const t = token$.get();
  if (t && t.length > 0) headers['Authorization'] = `Bearer ${t}`;
  return headers;
};

export const apiGet = (path: string) => {
  return fetch(buildUrl(path), {
    method: 'GET',
    headers: buildHeaders()
  });
};

export const apiPost = (path: string, body: unknown) => {
  return fetch(buildUrl(path), {
    method: 'POST',
    headers: buildHeaders(),
    body: JSON.stringify(body)
  });
};

export const apiDelete = (path: string) => {
  return fetch(buildUrl(path), {
    method: 'DELETE',
    headers: buildHeaders()
  });
};