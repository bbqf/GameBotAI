import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import type { GameBotClient } from "../client.js";
import { apiTool } from "../register.js";
import { jsonResult } from "../results.js";

export function registerExecutionLogTools(server: McpServer, client: GameBotClient): void {
  apiTool(
    server,
    "list_execution_logs",
    "Query the execution audit log (what queues/sequences/commands actually ran and how they ended). Returns root entries plus a nextCursor for pagination.",
    {
      objectType: z.string().optional().describe("Filter by object type, e.g. queue, sequence, command"),
      objectId: z.string().optional().describe("Filter by the executed object's id"),
      finalStatus: z.string().optional().describe("Filter by final status, e.g. success, failure"),
      fromUtc: z.string().optional().describe("ISO-8601 lower bound on timestamp (UTC)"),
      toUtc: z.string().optional().describe("ISO-8601 upper bound on timestamp (UTC)"),
      sortBy: z.string().optional().describe("Sort field; default timestamp"),
      sortDirection: z.enum(["asc", "desc"]).optional().describe("Sort direction; default desc"),
      pageSize: z.number().int().min(1).max(500).optional().describe("Page size; default 50"),
      cursor: z.string().optional().describe("Cursor from a previous page (nextCursor)"),
    },
    async (args) => jsonResult(await client.json("GET", "/api/execution-logs", { query: args })),
  );

  apiTool(
    server,
    "get_execution_log",
    "Get a single execution log entry by id.",
    { logId: z.string().describe("Execution log entry id") },
    async ({ logId }) => jsonResult(await client.json("GET", `/api/execution-logs/${encodeURIComponent(logId)}`)),
  );

  apiTool(
    server,
    "get_execution_log_subtree",
    "Get an execution log entry with its full child subtree (e.g. a queue run with every sequence/command/step outcome under it). The best tool for diagnosing why an automation run failed.",
    { logId: z.string().describe("Root execution log entry id") },
    async ({ logId }) =>
      jsonResult(await client.json("GET", `/api/execution-logs/${encodeURIComponent(logId)}/subtree`)),
  );
}
