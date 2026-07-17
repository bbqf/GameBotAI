import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import type { GameBotClient } from "../client.js";
import { apiTool } from "../register.js";
import { jsonResult } from "../results.js";

const commandBody = z
  .record(z.unknown())
  .describe(
    "Full command JSON: ordered steps of primitive actions (PrimitiveTap, WaitForImage, KeyInput, Swipe, EnsureGameRunning, GoToHomeScreen, EnsureEmulatorRunning, ...), optionally gated by triggers. GoToHomeScreen presses the Android HOME button so the device returns to the home screen, leaving the game running in the background. EnsureEmulatorRunning verifies the target LDPlayer instance is running and responsive (starting/restarting it if needed); it takes an ensureEmulatorRunning config with instanceName or instanceIndex plus adbSerial. In any polymorphic step object, put the \"type\" property FIRST - the service's JSON deserializer requires the discriminator before other fields. Fetch an existing command with get_command to copy the exact shape.",
  );

export function registerCommandTools(server: McpServer, client: GameBotClient): void {
  apiTool(
    server,
    "list_commands",
    "List all commands (reusable ordered step lists, optionally trigger-gated).",
    {},
    async () => jsonResult(await client.json("GET", "/api/commands")),
  );

  apiTool(
    server,
    "get_command",
    "Get a command by id, including its full step list.",
    { commandId: z.string().describe("Command id") },
    async ({ commandId }) => jsonResult(await client.json("GET", `/api/commands/${encodeURIComponent(commandId)}`)),
  );

  apiTool(
    server,
    "create_command",
    "Create a command from a full command JSON object.",
    { command: commandBody },
    async ({ command }) => jsonResult(await client.json("POST", "/api/commands", { body: command })),
  );

  apiTool(
    server,
    "update_command",
    "Update a command (PATCH) with the fields to change.",
    { commandId: z.string().describe("Command id"), patch: commandBody },
    async ({ commandId, patch }) =>
      jsonResult(await client.json("PATCH", `/api/commands/${encodeURIComponent(commandId)}`, { body: patch })),
  );

  apiTool(
    server,
    "delete_command",
    "Delete a command by id. Fails if sequences still reference it.",
    { commandId: z.string().describe("Command id") },
    async ({ commandId }) => jsonResult(await client.json("DELETE", `/api/commands/${encodeURIComponent(commandId)}`)),
  );

  apiTool(
    server,
    "force_execute_command",
    "Execute a command immediately, SKIPPING its trigger gate. Use for testing a command's steps.",
    {
      commandId: z.string().describe("Command id"),
      sessionId: z.string().optional().describe("Session to run against; defaults to the active session"),
    },
    async ({ commandId, sessionId }) =>
      jsonResult(
        await client.json("POST", `/api/commands/${encodeURIComponent(commandId)}/force-execute`, {
          query: { sessionId },
        }),
      ),
  );

  apiTool(
    server,
    "evaluate_and_execute_command",
    "Evaluate a command's trigger gate against the current screen and execute the command only if it matches (the normal production path).",
    {
      commandId: z.string().describe("Command id"),
      sessionId: z.string().optional().describe("Session to run against; defaults to the active session"),
    },
    async ({ commandId, sessionId }) =>
      jsonResult(
        await client.json("POST", `/api/commands/${encodeURIComponent(commandId)}/evaluate-and-execute`, {
          query: { sessionId },
        }),
      ),
  );

  apiTool(
    server,
    "execute_step",
    "Execute a single ad-hoc command step (PrimitiveTap, WaitForImage, KeyInput, Swipe, EnsureGameRunning, GoToHomeScreen, EnsureEmulatorRunning) without saving it. 10 second execution timeout. Command-type steps are not allowed here.",
    {
      step: z
        .record(z.unknown())
        .describe(
          'Step JSON with "type" first, e.g. {"type":"PrimitiveTap","primitiveTap":{"detectionTarget":{"referenceImageId":"...","confidence":0.8,"offsetX":0,"offsetY":0}}} or {"type":"Swipe","swipe":{"startX":100,"startY":800,"endX":100,"endY":200,"durationMs":300}}.',
        ),
      sessionId: z.string().optional().describe("Session to run against; defaults to the active session"),
    },
    async ({ step, sessionId }) =>
      jsonResult(await client.json("POST", "/api/steps/execute", { body: { step, sessionId } })),
  );
}
