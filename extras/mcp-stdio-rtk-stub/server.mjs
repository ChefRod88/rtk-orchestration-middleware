#!/usr/bin/env node
/**
 * Minimal MCP stdio server: tool rtk_invoke -> spawns /usr/local/bin/rtk with argv.
 * Setup: cd extras/mcp-stdio-rtk-stub && npm install
 * Cursor: add an MCP server entry whose command is this file (node .../server.mjs), cwd optional.
 */
import { McpServer } from "@modelcontextprotocol/server";
import { StdioServerTransport } from "@modelcontextprotocol/server/stdio";
import { spawn } from "node:child_process";
import { z } from "zod";

const server = new McpServer({
  name: "r2k-rtk-gateway",
  version: "1.0.0",
});

server.registerTool(
  "rtk_invoke",
  {
    description:
      "Shells out to /usr/local/bin/rtk with string arguments (no bash alias expansion).",
    inputSchema: z.object({
      argv: z.array(z.string()).describe('e.g. ["npm","install"]'),
    }),
  },
  async ({ argv }) => {
    const text = await new Promise((resolve, reject) => {
      let out = "";
      let err = "";
      const cp = spawn("/usr/local/bin/rtk", argv, {
        stdio: ["ignore", "pipe", "pipe"],
      });
      cp.stdout?.on("data", (d) => (out += d));
      cp.stderr?.on("data", (d) => (err += d));
      cp.on("error", reject);
      cp.on("close", (code, signal) => {
        resolve(
          JSON.stringify({
            exitCode: code,
            signal,
            stdout: out.trimEnd(),
            stderr: err.trimEnd(),
          })
        );
      });
    });
    return { content: [{ type: "text", text }] };
  }
);

const transport = new StdioServerTransport();
await server.connect(transport);
