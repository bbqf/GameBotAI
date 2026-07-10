import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import type { GameBotClient } from "../client.js";
import { apiTool } from "../register.js";
import { jsonResult } from "../results.js";

const sequenceBody = z
  .record(z.unknown())
  .describe(
    "Full sequence JSON. Simple shape: {\"name\":\"...\",\"steps\":[\"commandId1\",\"commandId2\"]}. Per-step shape supports inline actions, per-step delays, conditions (if/then/else) and timeouts. In any polymorphic action/step object put the \"type\" property FIRST. Fetch an existing sequence with get_sequence to copy the exact shape. Note: a failed step aborts the rest of the sequence, so put recovery steps first (recovery-first pattern).",
  );

export function registerSequenceTools(server: McpServer, client: GameBotClient): void {
  apiTool(
    server,
    "list_sequences",
    "List all sequences (orchestrations of commands/actions with delays and conditions).",
    {},
    async () => jsonResult(await client.json("GET", "/api/sequences")),
  );

  apiTool(
    server,
    "get_sequence",
    "Get a sequence by id, including its full step list.",
    { sequenceId: z.string().describe("Sequence id") },
    async ({ sequenceId }) =>
      jsonResult(await client.json("GET", `/api/sequences/${encodeURIComponent(sequenceId)}`)),
  );

  apiTool(
    server,
    "create_sequence",
    "Create a sequence from a full sequence JSON object.",
    { sequence: sequenceBody },
    async ({ sequence }) => jsonResult(await client.json("POST", "/api/sequences", { body: sequence })),
  );

  apiTool(
    server,
    "replace_sequence",
    "Replace a sequence's definition (PUT) with a full sequence JSON object.",
    { sequenceId: z.string().describe("Sequence id"), sequence: sequenceBody },
    async ({ sequenceId, sequence }) =>
      jsonResult(await client.json("PUT", `/api/sequences/${encodeURIComponent(sequenceId)}`, { body: sequence })),
  );

  apiTool(
    server,
    "patch_sequence",
    "Partially update a sequence (PATCH) with only the fields to change.",
    { sequenceId: z.string().describe("Sequence id"), patch: sequenceBody },
    async ({ sequenceId, patch }) =>
      jsonResult(await client.json("PATCH", `/api/sequences/${encodeURIComponent(sequenceId)}`, { body: patch })),
  );

  apiTool(
    server,
    "delete_sequence",
    "Delete a sequence by id.",
    { sequenceId: z.string().describe("Sequence id") },
    async ({ sequenceId }) =>
      jsonResult(await client.json("DELETE", `/api/sequences/${encodeURIComponent(sequenceId)}`)),
  );

  apiTool(
    server,
    "validate_sequence",
    "Validate a candidate sequence payload against an existing sequence id without saving it. Returns validation errors, if any.",
    {
      sequenceId: z.string().describe("Sequence id"),
      payload: z.record(z.unknown()).describe("Candidate sequence payload to validate"),
    },
    async ({ sequenceId, payload }) =>
      jsonResult(
        await client.json("POST", `/api/sequences/${encodeURIComponent(sequenceId)}/validate`, { body: payload }),
      ),
  );

  apiTool(
    server,
    "execute_sequence",
    "Execute a sequence now and return the per-step outcome. Check the result: a failed step aborts the remaining steps.",
    {
      sequenceId: z.string().describe("Sequence id"),
      sessionId: z.string().optional().describe("Session to run against; defaults to the active session"),
    },
    async ({ sequenceId, sessionId }) =>
      jsonResult(
        await client.json("POST", `/api/sequences/${encodeURIComponent(sequenceId)}/execute`, {
          body: sessionId ? { sessionId } : {},
        }),
      ),
  );
}
