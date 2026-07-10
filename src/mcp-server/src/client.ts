import type { Config } from "./config.js";

export type QueryParams = Record<string, string | number | boolean | undefined>;

export interface RequestOptions {
  query?: QueryParams;
  body?: unknown;
}

/** Non-2xx response from the GameBot API; `detail` carries the error body verbatim. */
export class GameBotApiError extends Error {
  constructor(
    readonly status: number,
    readonly detail: string,
  ) {
    super(`GameBot API returned ${status}: ${detail}`);
    this.name = "GameBotApiError";
  }
}

export class GameBotClient {
  constructor(private readonly config: Config) {}

  get baseUrl(): string {
    return this.config.baseUrl;
  }

  /** Send a request and parse the response as JSON (or a small status object when empty). */
  async json(method: string, path: string, options: RequestOptions = {}): Promise<unknown> {
    const response = await this.send(method, path, options);
    if (response.status === 204) return { ok: true, status: 204 };
    const text = await response.text();
    if (!text) return { ok: true, status: response.status };
    try {
      return JSON.parse(text);
    } catch {
      return { ok: true, status: response.status, body: text };
    }
  }

  /** Send a request and return the raw response bytes (for PNG endpoints). */
  async binary(
    method: string,
    path: string,
    options: RequestOptions = {},
  ): Promise<{ bytes: Buffer; contentType: string; headers: Headers }> {
    const response = await this.send(method, path, options);
    const bytes = Buffer.from(await response.arrayBuffer());
    return {
      bytes,
      contentType: response.headers.get("content-type") ?? "application/octet-stream",
      headers: response.headers,
    };
  }

  private async send(method: string, path: string, options: RequestOptions): Promise<Response> {
    const url = new URL(this.config.baseUrl + path);
    for (const [key, value] of Object.entries(options.query ?? {})) {
      if (value !== undefined) url.searchParams.set(key, String(value));
    }

    const headers: Record<string, string> = {};
    if (options.body !== undefined) headers["content-type"] = "application/json";
    if (this.config.token) headers["authorization"] = `Bearer ${this.config.token}`;

    let response: Response;
    try {
      response = await fetch(url, {
        method,
        headers,
        body: options.body !== undefined ? JSON.stringify(options.body) : undefined,
        signal: AbortSignal.timeout(this.config.timeoutMs),
      });
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error);
      throw new Error(
        `Cannot reach the GameBot API at ${this.config.baseUrl} (${message}). Is the GameBot service running?`,
      );
    }

    if (!response.ok) {
      const detail = (await response.text().catch(() => "")) || response.statusText;
      throw new GameBotApiError(response.status, detail);
    }
    return response;
  }
}
