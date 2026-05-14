#!/usr/bin/env node
/**
 * Minimal MCP stdio server: tool rtk_invoke -> spawns /usr/local/bin/rtk with argv.
 * Setup: npm install  (requires @modelcontextprotocol/sdk)
 */
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { spawn } from "node:child_process";
import { z } from "zod";

const RTK_EXE = process.env.RTK_CLI_PATH ?? "/usr/local/bin/rtk";

const server = new McpServer(
  { name: "r2k-rtk-gateway", version: "1.0.0" },
  { capabilities: { tools: {} } },
);

server.registerTool(
  "rtk_invoke",
  {
    description: "Shells out to rtk with argv array (no bash alias expansion).",
    inputSchema: z.object({
      argv: z.array(z.string()).describe('e.g. ["npm","install"]'),
    }),
  },
  async ({ argv }) => {
    const text = await new Promise<string>((resolve, reject) => {
      let out = "";
      let err = "";
      const cp = spawn(RTK_EXE, argv, {
        stdio: ["ignore", "pipe", "pipe"],
        env: process.env,
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
          }),
        );
      });
    });
    return { content: [{ type: "text", text }] };
  },
);

const transport = new StdioServerTransport();
await server.connect(transport);
