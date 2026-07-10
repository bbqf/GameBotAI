import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import type { GameBotClient } from "../client.js";
import { apiTool } from "../register.js";
import { imageResult, jsonResult } from "../results.js";

export function registerScreenTools(server: McpServer, client: GameBotClient): void {
  apiTool(
    server,
    "emulator_screenshot",
    "Capture the emulator screen as PNG. Returns the image plus a captureId (needed by crop_capture and detect_all_reference_images). Without sessionId, the first running session is used.",
    { sessionId: z.string().optional().describe("Session id; defaults to the first running session") },
    async ({ sessionId }) => {
      const { bytes, contentType, headers } = await client.binary("GET", "/api/emulator/screenshot", {
        query: { sessionId },
      });
      const captureId = headers.get("x-capture-id");
      return imageResult(bytes, contentType, captureId ? `captureId: ${captureId}` : undefined);
    },
  );

  apiTool(
    server,
    "crop_capture",
    "Crop a region out of a previous emulator_screenshot capture and save it as a named reference image (used by image-match triggers and detection). Minimum crop size is 16x16 px. Captures expire, so crop soon after taking the screenshot.",
    {
      sourceCaptureId: z.string().describe("captureId returned by emulator_screenshot"),
      name: z.string().describe("Name for the saved reference image (file becomes <name>.png)"),
      x: z.number().int().min(0),
      y: z.number().int().min(0),
      width: z.number().int().min(16),
      height: z.number().int().min(16),
      overwrite: z.boolean().optional().describe("Replace an existing image with the same name"),
    },
    async ({ sourceCaptureId, name, x, y, width, height, overwrite }) =>
      jsonResult(
        await client.json("POST", "/api/images/crop", {
          body: { name, overwrite: overwrite ?? false, sourceCaptureId, bounds: { x, y, width, height } },
        }),
      ),
  );

  apiTool(
    server,
    "detect_reference_image",
    "Find occurrences of a saved reference image on the current emulator screen. Returns matches with bbox and confidence.",
    {
      referenceImageId: z.string().describe("Reference image id (see list_images)"),
      threshold: z.number().min(0).max(1).optional().describe("Minimum match confidence (default per service config)"),
      maxResults: z.number().int().min(1).optional().describe("Maximum number of matches to return"),
      overlap: z.number().min(0).max(1).optional().describe("Allowed overlap between matches"),
    },
    async (args) => jsonResult(await client.json("POST", "/api/images/detect", { body: args })),
  );

  apiTool(
    server,
    "detect_all_reference_images",
    "Run detection of ALL saved reference images against a specific screenshot capture. Useful to discover which known UI elements are visible.",
    { captureId: z.string().describe("captureId returned by emulator_screenshot") },
    async ({ captureId }) => jsonResult(await client.json("POST", "/api/images/detect-all", { body: { captureId } })),
  );

  apiTool(
    server,
    "list_images",
    "List saved reference images (id, name, dimensions).",
    {},
    async () => jsonResult(await client.json("GET", "/api/images")),
  );

  apiTool(
    server,
    "get_image",
    "Fetch a saved reference image as PNG.",
    { imageId: z.string().describe("Reference image id") },
    async ({ imageId }) => {
      const { bytes, contentType } = await client.binary("GET", `/api/images/${encodeURIComponent(imageId)}`);
      return imageResult(bytes, contentType);
    },
  );

  apiTool(
    server,
    "get_image_metadata",
    "Get metadata for a saved reference image (name, size, content type) without downloading the pixels.",
    { imageId: z.string().describe("Reference image id") },
    async ({ imageId }) =>
      jsonResult(await client.json("GET", `/api/images/${encodeURIComponent(imageId)}/metadata`)),
  );

  apiTool(
    server,
    "delete_image",
    "Delete a saved reference image. Fails if triggers or commands still reference it.",
    { imageId: z.string().describe("Reference image id") },
    async ({ imageId }) => jsonResult(await client.json("DELETE", `/api/images/${encodeURIComponent(imageId)}`)),
  );
}
