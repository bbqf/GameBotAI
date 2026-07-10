import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import type { GameBotClient } from "../client.js";
import { apiTool } from "../register.js";
import { jsonResult } from "../results.js";

export function registerSystemTools(server: McpServer, client: GameBotClient): void {
  apiTool(
    server,
    "get_service_health",
    "Check that the GameBot service is up. Returns the service health payload from GET /health.",
    {},
    async () => jsonResult(await client.json("GET", "/health")),
  );

  apiTool(
    server,
    "list_adb_devices",
    "List ADB devices (emulators) currently visible to the GameBot service, with their serials (e.g. emulator-5558). Use a serial as adbSerial when starting a session.",
    {},
    async () => jsonResult(await client.json("GET", "/api/adb/devices")),
  );
}
