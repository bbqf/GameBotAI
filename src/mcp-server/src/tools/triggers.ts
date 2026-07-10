import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import type { GameBotClient } from "../client.js";
import { apiTool } from "../register.js";
import { jsonResult } from "../results.js";

const triggerBody = z
  .record(z.unknown())
  .describe(
    "Full trigger JSON. Triggers are text-match (OCR over a screen region) or image-match evaluators. Fetch an existing trigger with get_trigger to copy the exact shape before authoring a new one.",
  );

export function registerTriggerTools(server: McpServer, client: GameBotClient): void {
  apiTool(
    server,
    "list_triggers",
    "List all triggers (screen-state evaluators used to gate commands).",
    {},
    async () => jsonResult(await client.json("GET", "/api/triggers")),
  );

  apiTool(
    server,
    "get_trigger",
    "Get a trigger by id, including its full match configuration.",
    { triggerId: z.string().describe("Trigger id") },
    async ({ triggerId }) => jsonResult(await client.json("GET", `/api/triggers/${encodeURIComponent(triggerId)}`)),
  );

  apiTool(
    server,
    "create_trigger",
    "Create a trigger from a full trigger JSON object.",
    { trigger: triggerBody },
    async ({ trigger }) => jsonResult(await client.json("POST", "/api/triggers", { body: trigger })),
  );

  apiTool(
    server,
    "update_trigger",
    "Replace a trigger's definition (PUT) with a full trigger JSON object.",
    { triggerId: z.string().describe("Trigger id"), trigger: triggerBody },
    async ({ triggerId, trigger }) =>
      jsonResult(await client.json("PUT", `/api/triggers/${encodeURIComponent(triggerId)}`, { body: trigger })),
  );

  apiTool(
    server,
    "delete_trigger",
    "Delete a trigger by id.",
    { triggerId: z.string().describe("Trigger id") },
    async ({ triggerId }) => jsonResult(await client.json("DELETE", `/api/triggers/${encodeURIComponent(triggerId)}`)),
  );

  apiTool(
    server,
    "test_trigger",
    "Evaluate a trigger right now against the current emulator screen and return whether it matches.",
    { triggerId: z.string().describe("Trigger id") },
    async ({ triggerId }) =>
      jsonResult(await client.json("POST", `/api/triggers/${encodeURIComponent(triggerId)}/test`)),
  );
}
