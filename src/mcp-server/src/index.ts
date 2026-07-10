#!/usr/bin/env node
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { GameBotClient } from "./client.js";
import { loadConfig } from "./config.js";
import { registerCommandTools } from "./tools/commands.js";
import { registerExecutionLogTools } from "./tools/executionLogs.js";
import { registerGameTools } from "./tools/games.js";
import { registerQueueTools } from "./tools/queues.js";
import { registerScreenTools } from "./tools/screen.js";
import { registerSequenceTools } from "./tools/sequences.js";
import { registerSessionTools } from "./tools/sessions.js";
import { registerSystemTools } from "./tools/system.js";
import { registerTriggerTools } from "./tools/triggers.js";

async function main(): Promise<void> {
  const config = loadConfig();
  const client = new GameBotClient(config);

  const server = new McpServer({ name: "gamebot", version: "0.1.0" });

  registerSystemTools(server, client);
  registerGameTools(server, client);
  registerSessionTools(server, client);
  registerScreenTools(server, client);
  registerTriggerTools(server, client);
  registerCommandTools(server, client);
  registerSequenceTools(server, client);
  registerQueueTools(server, client);
  registerExecutionLogTools(server, client);

  await server.connect(new StdioServerTransport());
  // stdout is the MCP channel; diagnostics go to stderr only.
  console.error(`gamebot-mcp-server connected (API: ${client.baseUrl})`);
}

main().catch((error) => {
  console.error("gamebot-mcp-server failed to start:", error);
  process.exit(1);
});
