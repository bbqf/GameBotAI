import type { CallToolResult } from "@modelcontextprotocol/sdk/types.js";

export function jsonResult(data: unknown): CallToolResult {
  return { content: [{ type: "text", text: JSON.stringify(data, null, 2) }] };
}

export function textResult(text: string): CallToolResult {
  return { content: [{ type: "text", text }] };
}

export function imageResult(bytes: Buffer, mimeType: string, note?: string): CallToolResult {
  const content: CallToolResult["content"] = [
    { type: "image", data: bytes.toString("base64"), mimeType },
  ];
  if (note) content.push({ type: "text", text: note });
  return { content };
}

export function errorResult(error: unknown): CallToolResult {
  const message = error instanceof Error ? error.message : String(error);
  return { content: [{ type: "text", text: message }], isError: true };
}
