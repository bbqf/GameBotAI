import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import type { GameBotClient } from "../client.js";
import { apiTool } from "../register.js";
import { jsonResult } from "../results.js";

export function registerGameTools(server: McpServer, client: GameBotClient): void {
  apiTool(
    server,
    "list_games",
    "List all registered games (id, name, packageName, optional metadata).",
    {},
    async () => jsonResult(await client.json("GET", "/api/games")),
  );

  apiTool(
    server,
    "get_game",
    "Get a registered game by id.",
    { gameId: z.string().describe("Game id") },
    async ({ gameId }) => jsonResult(await client.json("GET", `/api/games/${encodeURIComponent(gameId)}`)),
  );

  apiTool(
    server,
    "create_game",
    "Register a new game. packageName is the Android package used to launch/detect the game (e.g. com.garena.game.fmjx).",
    {
      name: z.string().describe("Display name of the game"),
      packageName: z.string().optional().describe("Android package name"),
      description: z.string().optional().describe("Free-text description"),
      metadata: z
        .record(z.unknown())
        .optional()
        .describe("Arbitrary JSON metadata object; when provided it is stored instead of description"),
    },
    async (args) => jsonResult(await client.json("POST", "/api/games", { body: args })),
  );

  apiTool(
    server,
    "update_game",
    "Update a registered game. Only the provided fields are changed.",
    {
      gameId: z.string().describe("Game id"),
      name: z.string().optional(),
      packageName: z.string().optional(),
      description: z.string().optional(),
      metadata: z.record(z.unknown()).optional(),
    },
    async ({ gameId, ...body }) =>
      jsonResult(await client.json("PUT", `/api/games/${encodeURIComponent(gameId)}`, { body })),
  );

  apiTool(
    server,
    "delete_game",
    "Delete a registered game by id. Fails if queues still reference it.",
    { gameId: z.string().describe("Game id") },
    async ({ gameId }) => jsonResult(await client.json("DELETE", `/api/games/${encodeURIComponent(gameId)}`)),
  );
}
