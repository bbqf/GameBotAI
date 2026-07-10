import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import type { CallToolResult } from "@modelcontextprotocol/sdk/types.js";
import type { ZodRawShape, ZodTypeAny, objectOutputType } from "zod";
import { errorResult } from "./results.js";

/**
 * Register a tool whose handler proxies the GameBot API. Any thrown error
 * (connection failure, non-2xx response) is converted into an isError result
 * so the model sees the API's error body instead of a protocol failure.
 */
export function apiTool<Shape extends ZodRawShape>(
  server: McpServer,
  name: string,
  description: string,
  shape: Shape,
  handler: (args: objectOutputType<Shape, ZodTypeAny>) => Promise<CallToolResult>,
): void {
  server.registerTool(
    name,
    { description, inputSchema: shape },
    (async (args: objectOutputType<Shape, ZodTypeAny>) => {
      try {
        return await handler(args);
      } catch (error) {
        return errorResult(error);
      }
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
    }) as any,
  );
}
