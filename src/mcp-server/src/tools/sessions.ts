import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import type { GameBotClient } from "../client.js";
import { apiTool } from "../register.js";
import { imageResult, jsonResult } from "../results.js";

const inputActionSchema = z
  .object({
    type: z.enum(["tap", "swipe", "key"]).describe("Input action type"),
    args: z
      .record(z.unknown())
      .describe(
        'Action arguments. tap: {"x":123,"y":456}. swipe: {"x1":..,"y1":..,"x2":..,"y2":..}. key: {"keyCode":3} or {"key":"HOME"}.',
      ),
    delayMs: z.number().int().min(0).optional().describe("Delay after this action before the next one"),
    durationMs: z.number().int().min(0).optional().describe("Duration (swipe only)"),
  })
  .describe("One emulator input action");

export function registerSessionTools(server: McpServer, client: GameBotClient): void {
  apiTool(
    server,
    "start_session",
    "Start (or reuse) an emulator session bound to a game via the connect-to-game primitive. Returns sessionId plus the list of running sessions. Use list_adb_devices to find the adbSerial.",
    {
      gameId: z.string().describe("Registered game id (see list_games)"),
      adbSerial: z.string().describe("ADB device serial, e.g. emulator-5558"),
    },
    async ({ gameId, adbSerial }) =>
      jsonResult(
        await client.json("POST", "/api/sessions/start", {
          body: {
            primitiveAction: {
              type: "connect-to-game",
              payload: { gameId, adbSerial },
            },
          },
        }),
      ),
  );

  apiTool(
    server,
    "list_running_sessions",
    "List currently running emulator sessions (sessionId, gameId, emulator, status, capture rate).",
    {},
    async () => jsonResult(await client.json("GET", "/api/sessions/running")),
  );

  apiTool(
    server,
    "get_session",
    "Get a session by id (status, game, bound device).",
    { sessionId: z.string().describe("Session id") },
    async ({ sessionId }) => jsonResult(await client.json("GET", `/api/sessions/${encodeURIComponent(sessionId)}`)),
  );

  apiTool(
    server,
    "get_session_health",
    "Check the ADB/device health of a session.",
    { sessionId: z.string().describe("Session id") },
    async ({ sessionId }) =>
      jsonResult(await client.json("GET", `/api/sessions/${encodeURIComponent(sessionId)}/health`)),
  );

  apiTool(
    server,
    "session_snapshot",
    "Take a fresh PNG screenshot of the session's emulator screen and return it as an image. Prefer emulator_screenshot when you also need a captureId for cropping or detect-all.",
    { sessionId: z.string().describe("Session id") },
    async ({ sessionId }) => {
      const { bytes, contentType } = await client.binary(
        "GET",
        `/api/sessions/${encodeURIComponent(sessionId)}/snapshot`,
      );
      return imageResult(bytes, contentType);
    },
  );

  apiTool(
    server,
    "send_inputs",
    "Send a batch of input actions (tap/swipe/key) to the session's emulator. Actions run in order; use delayMs on an action to pause before the next one.",
    {
      sessionId: z.string().describe("Session id"),
      actions: z.array(inputActionSchema).min(1).describe("Ordered input actions to execute"),
    },
    async ({ sessionId, actions }) =>
      jsonResult(
        await client.json("POST", `/api/sessions/${encodeURIComponent(sessionId)}/inputs`, {
          body: { actions },
        }),
      ),
  );

  apiTool(
    server,
    "stop_session",
    "Stop a running session and release its emulator binding.",
    { sessionId: z.string().describe("Session id") },
    async ({ sessionId }) => jsonResult(await client.json("DELETE", `/api/sessions/${encodeURIComponent(sessionId)}`)),
  );
}
