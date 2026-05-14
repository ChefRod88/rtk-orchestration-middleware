/**
 * MCP stdio server exposing `run_rtk_command` → spawns `rtk` with tokenized argv.
 * Uses @modelcontextprotocol/sdk (JSON-RPC lifecycle via stdio transport).
 */
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
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
        "Command uses shell operators/redirection unsupported here; use a simpler argv-shaped command string.",
      );
    }
    args.push(piece);
  }

  return args.length > 0 ? args : [trimmed];
}

function spawnRtk(command: string): Promise<{ stdout: string; stderr: string; exitCode: number | null }> {
  return spawnRtkArgs(tokenizeCliCommand(command));
}

function spawnRtkArgs(
  argv: string[],
  stdin?: string,
): Promise<{ stdout: string; stderr: string; exitCode: number | null }> {
  return new Promise((resolve, reject) => {
    let stdout = "";
    let stderr = "";

    const child = spawn(RTK_EXE, argv, {
      env: process.env,
      stdio: [stdin === undefined ? "ignore" : "pipe", "pipe", "pipe"],
      windowsHide: true,
    });

    if (stdin !== undefined) {
      child.stdin?.write(stdin);
      child.stdin?.end();
    }
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

function renderProcessResult(result: { stdout: string; stderr: string; exitCode: number | null }): string {
  let text = result.stdout;
  if (result.stderr.length > 0) {
    text += (text.length > 0 ? "\n" : "") + `---stderr---\n${result.stderr}`;
  }
  text += `\n---exit-code---\n${result.exitCode ?? ""}`;
  return text;
}

const server = new McpServer(
  { name: "r2k-rtk", version: "1.0.0" },
  { capabilities: { tools: {} } },
);

server.registerTool(
  "run_rtk_command",
  {
    description:
      "Runs rtk (see RTK_CLI_PATH, default /usr/local/bin/rtk) after tokenizing command; returns stdout plus stderr/exitCode.",
    inputSchema: z.object({
      command: z
        .string()
        .min(1)
        .describe('Invocation for rtk argv, e.g. "npm install" or \'git status --short\''),
    }),
  },
  async ({ command }) => {
    return { content: [{ type: "text", text: renderProcessResult(await spawnRtk(command)) }] };
  },
);

server.registerTool(
  "r2k_orchestrate_prompt",
  {
    description:
      "Runs RTK prompt orchestration against visible prompt text; use dryRun to avoid Lambda/network calls.",
    inputSchema: z.object({
      prompt: z.string().min(1).describe("Visible Cursor prompt text to inspect and orchestrate."),
      dryRun: z.boolean().optional().default(false).describe("When true, skip AWS Lambda calls."),
    }),
  },
  async ({ prompt, dryRun }) => {
    const argv = ["--orchestrate-prompt"];
    if (dryRun) {
      argv.push("--dry-run");
    }
    return { content: [{ type: "text", text: renderProcessResult(await spawnRtkArgs(argv, prompt)) }] };
  },
);

server.registerTool(
  "r2k_dry_run_context",
  {
    description:
      "Runs RTK in dry-run mode for a command such as 'cursor src/File.cs:42' and returns token savings.",
    inputSchema: z.object({
      command: z
        .string()
        .min(1)
        .describe('Registered command plus args, e.g. "cursor R2K.CLI/Program.cs:42" or "git status"'),
    }),
  },
  async ({ command }) => {
    const argv = tokenizeCliCommand(command);
    if (!argv.includes("--dry-run")) {
      argv.push("--dry-run");
    }
    return { content: [{ type: "text", text: renderProcessResult(await spawnRtkArgs(argv)) }] };
  },
);

server.registerTool(
  "r2k_session_report",
  {
    description: "Returns RTK observed Cursor session token totals.",
    inputSchema: z.object({
      reset: z.boolean().optional().default(false).describe("When true, reset the observed session ledger."),
    }),
  },
  async ({ reset }) => {
    const argv = [reset ? "--cursor-session-reset" : "--cursor-session-report"];
    return { content: [{ type: "text", text: renderProcessResult(await spawnRtkArgs(argv)) }] };
  },
);

const transport = new StdioServerTransport();
await server.connect(transport);
