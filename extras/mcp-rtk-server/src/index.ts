/**
 * MCP stdio server exposing `run_rtk_command` → spawns `rtk` with tokenized argv.
 * Handshake / JSON-RPC framing is handled by @modelcontextprotocol/server (stdio transport).
 */
import { McpServer } from "@modelcontextprotocol/server";
import { StdioServerTransport } from "@modelcontextprotocol/server/stdio";
import { spawn } from "node:child_process";
import process from "node:process";
import { parse } from "shell-quote";
import { z } from "zod";

const RTK_EXE = process.env.RTK_CLI_PATH ?? "/usr/local/bin/rtk";

function tokenizeCliCommand(raw: string): string[] {
  const trimmed = raw.trim();
  if (!trimmed) {
    throw new Error("command must be non-empty");
  }

  const env = Object.fromEntries(
    Object.entries(process.env).filter((kv): kv is [string, string] => kv[1] !== undefined),
  );

  const parts = parse(trimmed, env);
  const args: string[] = [];
  for (const piece of parts) {
    if (typeof piece !== "string") {
      throw new Error(
        "Command uses shell operators/redirection unsupported here; passthrough a simpler argv-shaped string.",
      );
    }
    args.push(piece);
  }

  return args.length > 0 ? args : [trimmed];
}

function spawnRtk(command: string): Promise<{ stdout: string; stderr: string; exitCode: number | null }> {
  const argv = tokenizeCliCommand(command);
  return new Promise((resolve, reject) => {
    let stdout = "";
    let stderr = "";

    const child = spawn(RTK_EXE, argv, {
      env: process.env,
      stdio: ["ignore", "pipe", "pipe"],
      windowsHide: true,
    });

    child.stdout?.on("data", (chunk: Buffer | string) => {
      stdout += chunk.toString();
    });
    child.stderr?.on("data", (chunk: Buffer | string) => {
      stderr += chunk.toString();
    });
    child.on("error", reject);
    child.on("close", (exitCode) => {
      resolve({ stdout: stdout.trimEnd(), stderr: stderr.trimEnd(), exitCode });
    });
  });
}

const server = new McpServer({
  name: "r2k-rtk",
  version: "1.0.0",
});

server.registerTool(
  "run_rtk_command",
  {
    description:
      "Runs `/usr/local/bin/rtk` (override with RTK_CLI_PATH) after tokenizing `command`; returns stdout.",
    inputSchema: z.object({
      command: z
        .string()
        .min(1)
        .describe('Single shell-style invocation for rtk args, e.g. "npm install" or \'git status --short\''),
    }),
  },
  async ({ command }) => {
    const { stdout, stderr, exitCode } = await spawnRtk(command);
    let text = stdout;
    if (stderr.length > 0) {
      text += (text.length > 0 ? "\n" : "") + `---stderr---\n${stderr}`;
    }
    text += `\n---exit-code---\n${exitCode ?? ""}`;
    return { content: [{ type: "text", text }] };
  },
);

const transport = new StdioServerTransport();
await server.connect(transport);
