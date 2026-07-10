export interface Config {
  /** Base URL of the GameBot service, no trailing slash. */
  baseUrl: string;
  /** Optional bearer token (only needed when the service is started with auth enabled). */
  token?: string;
  /** Per-request timeout in milliseconds. */
  timeoutMs: number;
}

export function loadConfig(): Config {
  const baseUrl = (process.env.GAMEBOT_API_URL ?? "http://localhost:8080").replace(/\/+$/, "");
  const token = process.env.GAMEBOT_API_TOKEN || undefined;
  const parsedTimeout = Number(process.env.GAMEBOT_API_TIMEOUT_MS ?? "60000");
  const timeoutMs = Number.isFinite(parsedTimeout) && parsedTimeout > 0 ? parsedTimeout : 60000;
  return { baseUrl, token, timeoutMs };
}
