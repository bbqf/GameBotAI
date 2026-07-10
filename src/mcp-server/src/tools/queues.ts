import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import type { GameBotClient } from "../client.js";
import { apiTool } from "../register.js";
import { jsonResult } from "../results.js";

const templateEntrySchema = z
  .object({
    sequenceId: z.string().describe("Sequence id to run"),
    scheduleType: z
      .enum(["OncePerRun", "EveryStep", "Timer", "AtQueueStart"])
      .optional()
      .describe("When the entry fires; defaults to OncePerRun"),
    timerTimeOfDay: z
      .string()
      .optional()
      .describe('Timer mode: wall-clock HH:mm in SERVER-LOCAL time (not UTC). Mutually exclusive with timerRelativeOffset.'),
    timerRelativeOffset: z
      .string()
      .optional()
      .describe('Timer mode: offset from queue start as "HH:mm:ss", max 24:00:00. Mutually exclusive with timerTimeOfDay.'),
  })
  .describe("One template entry");

export function registerQueueTools(server: McpServer, client: GameBotClient): void {
  apiTool(
    server,
    "list_queues",
    "List all queues (the scheduling layer that runs sequences against a bound emulator).",
    {},
    async () => jsonResult(await client.json("GET", "/api/queues")),
  );

  apiTool(
    server,
    "get_queue",
    "Get a queue by id, including its runtime entries and execution status.",
    { queueId: z.string().describe("Queue id") },
    async ({ queueId }) => jsonResult(await client.json("GET", `/api/queues/${encodeURIComponent(queueId)}`)),
  );

  apiTool(
    server,
    "create_queue",
    "Create a queue. The emulator binding (emulatorSerial) is immutable after creation.",
    {
      name: z.string().describe("Queue name"),
      emulatorSerial: z.string().optional().describe("ADB serial to bind, e.g. emulator-5558"),
      cycleExecution: z.boolean().optional().describe("Restart from the first entry after the last one finishes"),
    },
    async (args) => jsonResult(await client.json("POST", "/api/queues", { body: args })),
  );

  apiTool(
    server,
    "update_queue",
    "Update a queue's name and/or cycleExecution flag. Not allowed while the queue is running.",
    {
      queueId: z.string().describe("Queue id"),
      name: z.string().optional(),
      cycleExecution: z.boolean().optional(),
    },
    async ({ queueId, ...body }) =>
      jsonResult(await client.json("PUT", `/api/queues/${encodeURIComponent(queueId)}`, { body })),
  );

  apiTool(
    server,
    "delete_queue",
    "Delete a queue. Not allowed while it is running.",
    { queueId: z.string().describe("Queue id") },
    async ({ queueId }) => jsonResult(await client.json("DELETE", `/api/queues/${encodeURIComponent(queueId)}`)),
  );

  apiTool(
    server,
    "add_queue_entry",
    "Append a sequence to a queue's runtime entries.",
    {
      queueId: z.string().describe("Queue id"),
      sequenceId: z.string().describe("Sequence id to append"),
    },
    async ({ queueId, sequenceId }) =>
      jsonResult(
        await client.json("POST", `/api/queues/${encodeURIComponent(queueId)}/entries`, { body: { sequenceId } }),
      ),
  );

  apiTool(
    server,
    "replace_queue_entries",
    "Replace ALL of a queue's runtime entries with an ordered list of sequence ids (empty list clears the queue). Not allowed while running.",
    {
      queueId: z.string().describe("Queue id"),
      sequenceIds: z.array(z.string()).describe("Ordered sequence ids"),
    },
    async ({ queueId, sequenceIds }) =>
      jsonResult(
        await client.json("PUT", `/api/queues/${encodeURIComponent(queueId)}/entries`, { body: { sequenceIds } }),
      ),
  );

  apiTool(
    server,
    "remove_queue_entry",
    "Remove a single entry from a queue by entry id (see get_queue for entry ids).",
    {
      queueId: z.string().describe("Queue id"),
      entryId: z.string().describe("Queue entry id"),
    },
    async ({ queueId, entryId }) =>
      jsonResult(
        await client.json(
          "DELETE",
          `/api/queues/${encodeURIComponent(queueId)}/entries/${encodeURIComponent(entryId)}`,
        ),
      ),
  );

  apiTool(
    server,
    "link_queue_template",
    "Link a queue template to a queue (null templateId unlinks). The template's entries are auto-loaded into the queue.",
    {
      queueId: z.string().describe("Queue id"),
      templateId: z.string().nullable().describe("Queue template id, or null to unlink"),
    },
    async ({ queueId, templateId }) =>
      jsonResult(
        await client.json("PUT", `/api/queues/${encodeURIComponent(queueId)}/template`, { body: { templateId } }),
      ),
  );

  apiTool(
    server,
    "link_queue_game",
    "Link a game to a queue (null gameId unlinks). Needed for ensure-game-running steps.",
    {
      queueId: z.string().describe("Queue id"),
      gameId: z.string().nullable().describe("Game id, or null to unlink"),
    },
    async ({ queueId, gameId }) =>
      jsonResult(await client.json("PUT", `/api/queues/${encodeURIComponent(queueId)}/game`, { body: { gameId } })),
  );

  apiTool(
    server,
    "start_queue",
    "Start executing a queue against its bound emulator. Returns 409 if already running.",
    { queueId: z.string().describe("Queue id") },
    async ({ queueId }) => jsonResult(await client.json("POST", `/api/queues/${encodeURIComponent(queueId)}/start`)),
  );

  apiTool(
    server,
    "stop_queue",
    "Stop a running queue.",
    { queueId: z.string().describe("Queue id") },
    async ({ queueId }) => jsonResult(await client.json("POST", `/api/queues/${encodeURIComponent(queueId)}/stop`)),
  );

  apiTool(
    server,
    "live_schedule_sequence",
    "Schedule a sequence to fire ONCE after a relative offset from now, against a queue's active run (queue must be running).",
    {
      queueId: z.string().describe("Queue id"),
      sequenceId: z.string().describe("Sequence id to fire"),
      offset: z.string().describe('Relative offset from now as "HH:mm:ss", e.g. "00:10:00"'),
    },
    async ({ queueId, sequenceId, offset }) =>
      jsonResult(
        await client.json("POST", `/api/queues/${encodeURIComponent(queueId)}/live-schedule`, {
          body: { sequenceId, offset },
        }),
      ),
  );

  apiTool(
    server,
    "list_queue_templates",
    "List saved queue templates (reusable ordered entry lists with schedule types).",
    {},
    async () => jsonResult(await client.json("GET", "/api/queue-templates")),
  );

  apiTool(
    server,
    "get_queue_template",
    "Get a queue template by id, including its entries.",
    { templateId: z.string().describe("Queue template id") },
    async ({ templateId }) =>
      jsonResult(await client.json("GET", `/api/queue-templates/${encodeURIComponent(templateId)}`)),
  );

  apiTool(
    server,
    "save_queue_template",
    "Create or overwrite-by-name a queue template. If a template with the same name exists, set overwrite=true or the call fails with 409.",
    {
      name: z.string().describe("Template name (case-insensitive unique)"),
      entries: z.array(templateEntrySchema).describe("Ordered template entries"),
      overwrite: z.boolean().optional().describe("Replace an existing template with the same name"),
    },
    async (args) => jsonResult(await client.json("POST", "/api/queue-templates", { body: args })),
  );

  apiTool(
    server,
    "delete_queue_template",
    "Delete a queue template by id.",
    { templateId: z.string().describe("Queue template id") },
    async ({ templateId }) =>
      jsonResult(await client.json("DELETE", `/api/queue-templates/${encodeURIComponent(templateId)}`)),
  );
}
