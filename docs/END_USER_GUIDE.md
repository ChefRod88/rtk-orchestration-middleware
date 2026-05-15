# RTK End-User Guide

RTK is a **token-saving orchestration layer** for Cursor and your terminal. It sits between your repo and AI tooling like a surgical filter: it keeps the context the model needs, strips noisy code, optionally sends lean payloads to the cloud optimizer, and reports savings back to you.

**New here?** Read [What RTK does](#what-rtk-does), run [One-time global install](#one-time-global-install), then use the [`rtk` prompt prefix](#the-rtk-prompt-prefix-recommended) for reliable workspace scans.

---

## What RTK does

| Capability | What you get |
|------------|----------------|
| Command interception | Configured tools (`git`, `npm`, `cursor`, …) route through `rtk` via shims. |
| Context pruning | Local code is reduced with `minimal`, `diff-only`, or `agentic` strategies before cloud calls. |
| Prompt orchestration | Visible Cursor prompts can be analyzed, pruned, and resubmitted with an RTK savings footer. |
| Secret redaction | Common password/token patterns are masked before cloud upload. |
| Measurement | Local reports for the latest prompt and full observed session. |
| Cursor MCP | Global `r2k-optimizer` tools call `rtk` from chat without memorizing flags. |

## What RTK cannot fully see

RTK reports **observed** savings from prompts, files, commands, and MCP calls it can inspect. Cursor may still attach hidden/system context (rules, tool output, background agents) outside the hook/MCP surface. Treat RTK numbers as **workflow savings for RTK-routed traffic**, not exact provider billing totals.

---

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
| Command shims | `~/.local/share/r2k/shims` (prepended to `PATH`) |
| Cursor MCP | `~/.cursor/mcp.json` → `r2k-optimizer` |
| Cursor prompt hook | `~/.cursor/hooks.json` + `optimize-prompt.py` |

**Codespaces only:** `scripts/setup-r2k-codespace.sh` does the same for the dev container (repo `hooks.json`, shims, env vars).

After install, **restart Cursor**.

---

## Verify installation

```bash
which rtk
rtk --cursor-session-report
cat ~/.cursor/mcp.json
cat ~/.cursor/hooks.json
```

In Cursor: **Settings → Tools & MCP** → confirm **`r2k-optimizer`** is listed and connected.

---

## The `rtk` prompt prefix (recommended)

Starting a prompt with **`rtk`** tells RTK to **force a workspace scan** and attach pruned project context—even when you do not name a file.

| You type | RTK behavior |
|----------|----------------|
| `rtk` | Scans the workspace and uses a default review prompt. |
| `rtk fix the auth bug in login` | Strips `rtk`, scans for relevant files, sends a lean prompt. |
| `rtk: explain Program.cs line 42` | Same as `rtk ` (colon form). |
| `Fix line 42 in Program.cs` (no prefix) | Uses explicit paths/lines when present; otherwise lighter discovery. |

**Real example**

```text
rtk scan focus.cs and explain how Function.cs is used
```

RTK will:

1. Remove the `rtk` trigger from the text Cursor sees.
2. Discover `focus.cs`, `Function.cs`, and related files in the repo.
3. Prune those files (signatures, line windows, headers—not full 500-line dumps).
4. Return an optimized prompt for you to submit (via hook block or MCP).
5. Ask the model to end its answer with an **RTK Savings** footer.

---

## Daily Cursor usage

### Automatic visible prompt preflight

When Cursor supports global hooks, RTK runs **before** you submit a visible prompt:

1. Hook calls `rtk --orchestrate-prompt` with your text.
2. If RTK finds meaningful savings, Cursor **blocks** the original and shows an **optimized prompt to resubmit**.
3. After the answer, the model should append:

```text
RTK Savings:
- Original context tokens: ...
- Pruned context tokens: ...
- Tokens saved: ...
- Savings: ...%
```

You can also print the same numbers in a terminal:

```bash
rtk --last-prompt-savings
```

### Skip RTK for one prompt

Add anywhere in the prompt:

```text
--bypass-rtk
```

RTK passes your text through without pruning (hook allows submission as-is).

### Use MCP tools in chat

Examples:

```text
Use r2k-optimizer to run r2k_session_report
```

```text
Use r2k-optimizer r2k_orchestrate_prompt with dryRun true for: rtk explain the orchestrator
```

| Tool | Use it for |
|------|------------|
| `run_rtk_command` | Run an RTK-routed terminal command (`git status`, `cursor File.cs:42 --dry-run`, …). |
| `r2k_orchestrate_prompt` | Orchestrate visible prompt text (optional dry run). |
| `r2k_dry_run_context` | Estimate command/context savings without Lambda. |
| `r2k_session_report` | Show or reset observed session totals. |

---

## Terminal usage

### Dry-run (no cloud, no command execution)

```bash
rtk cursor R2K.CLI/Program.cs:42 --dry-run
```

Prints a **Mission 2026 Token Savings Estimate** locally.

### Latest prompt savings

```bash
rtk --last-prompt-savings
```

### Observed session report / reset

```bash
rtk --cursor-session-report
rtk --cursor-session-reset
```

### Legacy prompt cleanup (whitespace only)

```bash
printf 'please      clean     this' | rtk --optimize-prompt
```

For full context pruning, prefer **`--orchestrate-prompt`** (used by the Cursor hook and MCP).

---

## Policy configuration

Root [`hooks.json`](../hooks.json) (or global `~/.config/r2k/hooks.json`):

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

**Lookup order:** repo `hooks.json` → `~/.config/r2k/hooks.json` → pass-through (command runs normally).

---

## Pruning strategies

| Strategy | Behavior |
|----------|----------|
| `minimal` | Keeps file context mostly intact (light trim). |
| `diff-only` | Uses `git diff` and `git diff --cached` instead of whole files. |
| `agentic` | Line windows (`File.cs:42`), method signatures, class headers; bodies become `// ... [logic removed] ...`. |

Non-code files (Markdown, JSON, YAML) use a lighter structural pass so README-style content is not destroyed.

---

## Linux-only setup (recommended if you use Codespace / SSH)

If you use **Windows Cursor** but develop on **Linux** (Codespace, Remote-SSH, Dev Container), do **not** install RTK on Windows. A Linux install that wrote `/workspace/...` into MCP config will break on Windows as:

```text
Cannot find module 'C:\workspace\extras\mcp-rtk-server\dist\index.js'
```

**On Windows:** remove or disable `r2k-optimizer` in `C:\Users\<you>\.cursor\mcp.json`, or run:

```bash
bash scripts/strip-r2k-mcp-entry.sh
```

**On Linux only:** `bash scripts/install-r2k-global.sh`, then open the repo in a **remote** Cursor session.

Full walkthrough: **[LINUX_ONLY_SETUP.md](LINUX_ONLY_SETUP.md)**.

---

## Troubleshooting

### MCP: module not found (`C:\workspace\...` on Windows)

**Cause:** Windows Cursor is loading a Linux/Codespace MCP path that does not exist locally.

**Fix (Linux-only):** disable `r2k-optimizer` on Windows (see [LINUX_ONLY_SETUP.md](LINUX_ONLY_SETUP.md)); install RTK on Linux and use Remote-SSH/Codespace/Dev Container.

**Fix (same machine):** build MCP where Cursor runs Node:

```bash
cd extras/mcp-rtk-server && npm ci && npm run build
```

Ensure `~/.cursor/mcp.json` `args[0]` points at that machine's real `dist/index.js` path.

### `npm ci` intercepted by RTK

Temporarily remove shims from `PATH`:

```bash
export PATH="$(printf '%s' "$PATH" | tr ':' '\n' | grep -v "$HOME/.local/share/r2k/shims" | paste -sd: -)"
hash -r
npm ci
```

### Prompt hook never fires

- Confirm `~/.cursor/hooks.json` exists (global installer).
- Confirm `which rtk` works.
- Restart Cursor after hook/MCP changes.
- Some Cursor surfaces (e.g. certain background agents) may not run `beforeSubmitPrompt`.

### Savings look too high or too low

RTK estimates tokens as **characters ÷ 4** for local context and cannot see all hidden Cursor context. Use reports as **directional** metrics for RTK-routed flows.

### Need real `git` / `npm` without RTK

```bash
command git status
/usr/bin/git status
```

Or remove shims from `PATH` as above.

---

## Recommended workflow

1. Run `bash scripts/install-r2k-global.sh` once.
2. Restart Cursor; verify **r2k-optimizer** under Tools & MCP.
3. Open any repo.
4. Prefer prompts like: **`rtk`** + your question, or include `path/to/File.cs:line`.
5. When the hook blocks, **resubmit** the optimized prompt.
6. Check savings:

```bash
rtk --last-prompt-savings
rtk --cursor-session-report
```

---

## More documentation

| Doc | Audience |
|-----|----------|
| [README.md](../README.md) | Project overview, architecture, CI/CD |
| [LINUX_ONLY_SETUP.md](LINUX_ONLY_SETUP.md) | Linux-only / no Windows MCP (Codespace, SSH) |
| [USAGE.md](USAGE.md) | Operators: AWS, Lambda, hooks, MCP, test checklist |
| [R2K.Backend.AWS/Readme.md](../R2K.Backend.AWS/Readme.md) | AWS Lambda deploy and payload contract |
