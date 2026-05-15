# RTK End-User Guide

RTK is a token-saving orchestration layer for Cursor and terminal workflows. It acts like a surgical filter between your repo and AI tooling: it keeps the useful context, removes noisy code, sends lean payloads to the optimizer, and reports the savings back to you.

## What RTK does

- Intercepts configured commands such as `git`, `npm`, and `cursor`.
- Reads a registry from `hooks.json` or global `~/.config/r2k/hooks.json`.
- Prunes local code context before it reaches AI workflows.
- Redacts common secrets before cloud upload.
- Sends optimized payloads to AWS Lambda.
- Records local token savings reports.
- Exposes Cursor MCP tools globally through `r2k-optimizer`.

## What RTK cannot fully see

RTK reports observed savings from prompts, files, commands, and MCP calls it can inspect. Cursor may still add hidden/system context outside the hook/MCP surface. Treat RTK numbers as observed workflow savings, not provider billing totals.

## One-time global install

From the repo root:

```bash
bash scripts/install-r2k-global.sh
```

This installs:

| Item | Location |
|------|----------|
| RTK CLI | `/usr/local/bin/rtk` |
| Global policy | `~/.config/r2k/hooks.json` |
| Command shims | `~/.local/share/r2k/shims` |
| Cursor MCP config | `~/.cursor/mcp.json` |
| Cursor prompt hook | `~/.cursor/hooks.json` |

After install, restart Cursor.

## Verify installation

Run:

```bash
which rtk
rtk --cursor-session-report
cat ~/.cursor/mcp.json
cat ~/.cursor/hooks.json
```

In Cursor, open:

```text
Settings -> Tools & MCP
```

You should see:

```text
r2k-optimizer
```

## Daily Cursor usage

### Automatic visible prompt preflight

When Cursor supports global hooks, RTK inspects visible prompts before submit. If RTK detects savings, Cursor blocks the original prompt and shows an optimized prompt to resubmit.

Example prompt:

```text
Fix line 42 in R2K.CLI/Program.cs and explain the change briefly.
```

RTK returns a pruned prompt containing:

- the original request,
- a focused code window,
- structural context,
- an instruction to append an RTK savings footer.

After the answer, Cursor should include:

```text
RTK Savings:
- Original context tokens: ...
- Pruned context tokens: ...
- Tokens saved: ...
- Savings: ...%
```

### Use the MCP tools directly

In Cursor chat, ask:

```text
Use r2k-optimizer to run r2k_session_report
```

Useful MCP tools:

| Tool | Use it for |
|------|------------|
| `run_rtk_command` | Run an RTK-routed terminal command. |
| `r2k_orchestrate_prompt` | Run prompt orchestration on visible text. |
| `r2k_dry_run_context` | Estimate savings without sending to Lambda. |
| `r2k_session_report` | Show/reset observed Cursor session totals. |

## Terminal usage

### Dry-run context pruning

```bash
rtk cursor R2K.CLI/Program.cs:42 --dry-run
```

This prints a local estimate and skips AWS/network calls.

### Latest prompt savings

```bash
rtk --last-prompt-savings
```

### Observed session savings

```bash
rtk --cursor-session-report
```

### Reset observed session

```bash
rtk --cursor-session-reset
```

## Policy configuration

Root `hooks.json`:

```json
{
  "version": "1.0.0",
  "settings": {
    "telemetry_endpoint": "https://awv3cnqcx0.execute-api.us-east-2.amazonaws.com/OptimizeCommand",
    "default_mode": "prune"
  },
  "hooks": [
    { "command": "npm", "strategy": "minimal" },
    { "command": "git", "strategy": "diff-only" },
    { "command": "cursor", "strategy": "agentic" }
  ]
}
```

Lookup order:

1. repo-local `hooks.json`
2. global `~/.config/r2k/hooks.json`
3. pass-through when neither exists

## Strategies

| Strategy | Behavior |
|----------|----------|
| `minimal` | Keeps file context mostly intact. |
| `diff-only` | Uses `git diff` and `git diff --cached` instead of whole files. |
| `agentic` | Extracts targeted line windows, signatures, declarations, and structural context. |

## Windows setup notes

If Cursor runs on Windows, the MCP config must use Windows paths. Build the MCP server and Windows RTK executable from a Windows PowerShell terminal:

```powershell
cd $env:USERPROFILE\rtk-orchestration-middleware
cd .\extras\mcp-rtk-server
npm ci
npm run build
cd ..\..
dotnet publish .\R2K.CLI\R2K.CLI.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

Then point `%USERPROFILE%\.cursor\mcp.json` at:

```text
C:\Users\<you>\rtk-orchestration-middleware\extras\mcp-rtk-server\dist\index.js
```

and:

```text
C:\Users\<you>\rtk-orchestration-middleware\R2K.CLI\bin\Release\net8.0\win-x64\publish\R2K.CLI.exe
```

## Troubleshooting

### MCP says module not found

Your `mcp.json` points to a path that does not exist. Build the MCP server and update `args[0]`.

```bash
cd extras/mcp-rtk-server
npm ci
npm run build
```

### `npm ci` runs through RTK and fails

Temporarily remove RTK shims from `PATH`:

```bash
export PATH="$(printf '%s' "$PATH" | tr ':' '\n' | grep -v "$HOME/.local/share/r2k/shims" | paste -sd: -)"
hash -r
npm ci
```

### Cursor prompt hook does not fire

Check:

```bash
cat ~/.cursor/hooks.json
which rtk
rtk --cursor-session-report
```

Restart Cursor after changing hook or MCP configuration.

### Token savings look too high or too low

RTK uses an estimate for local context tokens and cannot see all hidden Cursor context. Use the reports as observed savings for RTK-routed prompts and commands.

## Recommended workflow

1. Install globally.
2. Restart Cursor.
3. Open any repo.
4. Ask specific prompts with file paths and line numbers.
5. Resubmit the RTK-optimized prompt when the hook blocks.
6. Review savings:

```bash
rtk --last-prompt-savings
rtk --cursor-session-report
```
