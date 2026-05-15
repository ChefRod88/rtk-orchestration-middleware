# R2K Technical Usage Guide

This guide is for operators and contributors: runtime paths, CLI modes, Cursor integration, AWS deployment, and verification. End users should start with **[END_USER_GUIDE.md](END_USER_GUIDE.md)**.

---

## Purpose

R2K is an **agentic token firewall** and command optimizer. It:

1. Intercepts configured shell commands via `hooks.json` and PATH shims.
2. Prunes local file/git context before cloud optimization.
3. POSTs lean payloads to **`OptimizeCommand`** (AWS API Gateway → Lambda).
4. Executes optimized commands locally and records telemetry in MySQL.
5. Orchestrates **visible Cursor prompts** via `--orchestrate-prompt`, hooks, and MCP.

Cursor does **not** expose every byte sent to the model. RTK optimizes what it can observe: routed commands, hook/MCP prompt text, and discovered workspace files.

---

## Runtime paths

| Component | Path | Role |
|-----------|------|------|
| `R2K.CLI` | `/usr/local/bin/rtk` | Interception, pruning, orchestration, session meter |
| `hooks.json` | Repo root or `~/.config/r2k/hooks.json` | Policy: commands, strategies, telemetry endpoint |
| Shims | `~/.local/share/r2k/shims` | Symlinks to `rtk` for `npm`, `git`, `cursor`, … |
| AWS Lambda | `R2K.Backend.AWS/Function.cs` | `R2KOptimizer` — refine context, metrics, MySQL insert |
| Azure Functions | `R2K.Backend/` | Alternate backend (Tiktoken, richer optimizers) |
| MCP server | `extras/mcp-rtk-server` | Cursor tools → spawn `rtk` |
| Prompt hook | `.cursor/hooks/optimize-prompt.py` | `beforeSubmitPrompt` → `rtk --orchestrate-prompt` |
| Session ledger | `~/.rtk/cursor-session.jsonl` | Observed session events |
| Latest prompt | `~/.rtk/latest-prompt-savings.json` | Last orchestrated prompt metrics |
| Last command | `~/.rtk/last-metrics.json` | Last intercepted command metrics |

**Validated AWS endpoint (example):**

```bash
curl -sS -X POST 'https://awv3cnqcx0.execute-api.us-east-2.amazonaws.com/OptimizeCommand' \
  -H 'Content-Type: application/json' \
  -d '{"command":"git status"}'
```

---

## `hooks.json` (single source of truth)

Repo root [`hooks.json`](../hooks.json) drives shim generation in:

- `scripts/setup-r2k-codespace.sh`
- `scripts/install-r2k-global.sh`

Structure:

```json
{
  "version": "1.0.0",
  "settings": {
    "telemetry_endpoint": "https://…/OptimizeCommand",
    "default_mode": "prune"
  },
  "hooks": [
    { "command": "npm", "strategy": "minimal" },
    { "command": "git", "strategy": "diff-only" },
    { "command": "cursor", "strategy": "agentic" }
  ]
}
```

`HookRegistry` accepts legacy **array-only** JSON for backward compatibility.

**Lookup:** `./hooks.json` → `~/.config/r2k/hooks.json` → no hook (passthrough execution).

---

## CLI reference

### Session and reporting

| Flag | Description |
|------|-------------|
| `rtk --cursor-session-report` | Print observed session totals from `~/.rtk/cursor-session.jsonl`. |
| `rtk --cursor-session-reset` | Clear the session ledger. |
| `rtk --last-prompt-savings` | Print latest prompt orchestration savings. |

### Prompt modes

| Flag | Description |
|------|-------------|
| `rtk --optimize-prompt` | Whitespace/prompt cleanup via prompt API (stdin/args). |
| `rtk --orchestrate-prompt` | Full orchestration: `PromptIntent`, `ContextAnalyzer`, pruning, optional Lambda. Used by hook + MCP. |

**Prompt intent (`PromptIntent.cs`):**

| Input | Effect |
|-------|--------|
| `rtk` (alone) | Force workspace scan; default review prompt. |
| `rtk …` or `rtk:…` | Strip prefix; force workspace scan. |
| `--bypass-rtk` in text | Skip pruning; allow original prompt. |

**Context discovery (`ContextAnalyzer.cs`):**

1. Explicit paths and line refs (`File.cs:42`, `--line 42`).
2. Keyword workspace discovery (when forced or no explicit refs).
3. Fallback: `README.md`, `.sln`, `.csproj`, `package.json`.

### Intercepted commands

```bash
rtk git status
rtk npm install
rtk cursor R2K.CLI/Program.cs:42 --dry-run
```

| Modifier | Behavior |
|----------|----------|
| `--dry-run` | Prune + print savings estimate; no Lambda; no command execution. |
| Unregistered command | Executed directly (no RTK). |

Environment:

| Variable | Purpose |
|----------|---------|
| `RTK_API_URL` | Overrides `settings.telemetry_endpoint` |
| `RTK_PROMPT_API_URL` | Prompt optimizer URL (defaults from `RTK_API_URL`) |
| `RTK_FUNCTION_KEY` | `x-functions-key` for Azure-style auth |
| `RTK_CLI_PATH` | MCP/hook override for `rtk` binary |
| `RTK_PRINT_SAVINGS=1` | Print classic savings banner on command path |

---

## Pruning pipeline

| Class | Role |
|-------|------|
| `ContextPruner` | Regex structural prune for C#; line windows; structured text for `.md`/`.json`/`.yaml` |
| `ContextPruningEngine` | Dispatches `minimal` / `agentic` / `diff-only`; token estimates; redaction |
| `SensitiveDataRedactor` | Masks passwords, bearer tokens, API keys in context before upload |

**Agentic** strategy highlights:

- Resolves `path:line`, `--line N`, `line=N`.
- Keeps signatures, types, usings; replaces method bodies with `// ... [logic removed] ...`.
- Includes ±N lines around target line when specified.

**Lambda** (`Function.cs`) may apply a second-pass density filter and persist `StrategyUsed`, `OriginalContextTokens`, `PrunedContextTokens`, `PruningEfficiency` (see [`TokenLogs.mysql.sql`](../R2K.Backend/Schema/TokenLogs.mysql.sql)).

---

## Codespaces setup

```bash
bash scripts/setup-r2k-codespace.sh
source ~/.bashrc
```

The script:

- Builds/installs `rtk` via `scripts/install-rtk.sh`
- Sets `RTK_API_URL`, `RTK_PROMPT_API_URL`, `RTK_CLI_PATH`
- Creates shims from repo `hooks.json` (no bash aliases required)
- Prepends `~/.local/share/r2k/shims` to `PATH`

Verify:

```bash
type git    # should resolve to shims directory
rtk cursor R2K.CLI/Program.cs --dry-run
```

---

## Global Cursor install

```bash
bash scripts/install-r2k-global.sh
```

Installs CLI, `~/.config/r2k/hooks.json`, shims, MCP build, merges `r2k-optimizer` into `~/.cursor/mcp.json`, copies prompt hook to `~/.cursor/hooks.json`.

Restart Cursor; check **Settings → Tools & MCP**.

---

## Cursor prompt hook

| File | Role |
|------|------|
| `.cursor/hooks.json` | Registers `beforeSubmitPrompt` |
| `.cursor/hooks/optimize-prompt.py` | Calls `rtk --orchestrate-prompt`; blocks with optimized text when `tokens_saved >= RTK_PROMPT_MIN_SAVED` (default 1) |

Hook **fails open**: if `rtk` is missing, times out, or returns non-zero, the original prompt proceeds.

Hook sets env for orchestration:

- `RTK_CURSOR_HOOK_CHAR_COUNT`
- `RTK_CURSOR_HOOK_TOKEN_ESTIMATE`

Optimized prompts instruct the model to append an **RTK Savings** footer; terminal mirror: `rtk --last-prompt-savings`.

---

## Cursor MCP

Build:

```bash
cd extras/mcp-rtk-server
npm ci
npm run build
```

Example [`/.cursor/mcp.json.example`](../.cursor/mcp.json.example):

```json
{
  "mcpServers": {
    "r2k-optimizer": {
      "command": "node",
      "args": ["/absolute/path/to/extras/mcp-rtk-server/dist/index.js"],
      "env": {
        "RTK_CLI_PATH": "/usr/local/bin/rtk",
        "RTK_API_URL": "https://awv3cnqcx0.execute-api.us-east-2.amazonaws.com/OptimizeCommand",
        "RTK_PROMPT_API_URL": "https://awv3cnqcx0.execute-api.us-east-2.amazonaws.com/OptimizeCommand"
      }
    }
  }
}
```

| Tool | CLI mapping |
|------|-------------|
| `run_rtk_command` | `rtk <tokenized argv>` |
| `r2k_orchestrate_prompt` | `rtk --orchestrate-prompt` (+ optional `--dry-run`) |
| `r2k_dry_run_context` | command + `--dry-run` |
| `r2k_session_report` | `--cursor-session-report` or `--cursor-session-reset` |

Index MCP spec in Cursor: see [`extras/CURSOR_MCP_DOCS_SETUP.txt`](../extras/CURSOR_MCP_DOCS_SETUP.txt).

---

## Test checklist

### Build

```bash
dotnet build R2K.Backend/R2K.Backend.csproj
dotnet build R2K.Backend.AWS/R2K.Backend.AWS.csproj
dotnet build R2K.CLI/R2K.CLI.csproj
```

### Unit tests

```bash
dotnet test R2K.Backend.Tests/R2K.Backend.Tests.csproj
```

Covers optimizers, `ContextPruner`, `ContextPruningEngine`, `HookRegistry`, `PromptIntent`, `ContextAnalyzer`, `CursorSessionMeter`, `AwsLambdaClient`, `SensitiveDataRedactor`, and related paths. Use .NET 9 SDK if the test project targets `net9.0`.

### RDS probe

```bash
dotnet run --project R2K.DbProbe/R2K.DbProbe.csproj
```

Migrate analytics columns:

```bash
dotnet run --project R2K.DbProbe/R2K.DbProbe.csproj -- --migrate
```

### API Gateway smoke

```bash
curl -i -X POST "$RTK_API_URL" \
  -H 'Content-Type: application/json' \
  -d '{"command":"git status","pruned_context":"","original_token_count":0,"pruned_token_count":0,"pruning_strategy":"diff-only"}'
```

### CLI dry-run

```bash
rtk cursor R2K.CLI/Program.cs:42 --dry-run
```

### Prompt orchestration (stdin)

```bash
printf 'rtk explain Program.cs' | rtk --orchestrate-prompt --dry-run
```

---

## AWS Lambda deployment

```bash
cd R2K.Backend.AWS
dotnet lambda deploy-function R2KOptimizer
```

**Environment:**

```text
SqlConnectionString=Server=…;Port=3306;Database=r2k_telemetry;User ID=…;Password=…;SslMode=Required;
```

**API Gateway:** `POST /OptimizeCommand` → `R2KOptimizer`.

See [R2K.Backend.AWS/Readme.md](../R2K.Backend.AWS/Readme.md) for payload fields.

---

## Security

- Do not commit `local.settings.json`, `.cursor/mcp.json`, or credentials.
- Rotate any secrets exposed in chat or logs.
- Prefer AWS Secrets Manager / SSM for `SqlConnectionString`.
- `SensitiveDataRedactor` reduces accidental secret upload; it is not a guarantee.

---

## Known limitations

| Topic | Detail |
|-------|--------|
| Prompt replacement | Hooks **block + suggest** resubmit; they do not silently replace prompts in place. |
| Hidden Cursor context | Rules, tools, and internal context are outside RTK visibility. |
| Token estimate | Local heuristic: `(chars + 3) / 4`, not Tiktoken on every path. |
| Dual backends | Azure (`R2K.Backend`) and AWS (`R2K.Backend.AWS`) coexist; AWS is the primary tested path in Codespaces. |
| Background agents | May not invoke `beforeSubmitPrompt`; use MCP or `rtk` prefix explicitly. |

---

## Related docs

- [END_USER_GUIDE.md](END_USER_GUIDE.md) — beginner workflows, `rtk` prefix, troubleshooting
- [README.md](../README.md) — architecture, roadmap, CI/CD
- [R2K.Backend.AWS/Readme.md](../R2K.Backend.AWS/Readme.md) — Lambda contract
