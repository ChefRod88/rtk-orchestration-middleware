# Linux-only RTK setup (no Windows install)

Use this guide if you want **RTK and r2k-optimizer MCP only on Linux** (Codespace, SSH VM, or Dev Container) and **not** on Windows desktop Cursor.

## Why Windows shows `MODULE_NOT_FOUND`

If Windows Cursor lists **r2k-optimizer** but logs:

```text
Cannot find module 'C:\workspace\extras\mcp-rtk-server\dist\index.js'
```

then Windows is trying to run an MCP path from a **Linux/Codespace install** (`/workspace/...`). That file does not exist on Windows unless you deliberately built RTK there—which you are skipping.

**Fix on Windows:** disable MCP for RTK on Windows (below). **Fix on Linux:** install and open the repo remotely.

---

## Step 1 — Disable r2k-optimizer on Windows

On your **Windows PC**, do **one** of the following.

### Option A: Cursor UI

1. **Settings → Tools & MCP**
2. Turn **off** or remove **r2k-optimizer**
3. Restart Cursor

### Option B: Edit `mcp.json`

Open:

```text
C:\Users\<you>\.cursor\mcp.json
```

Delete the entire `"r2k-optimizer": { ... }` block (keep other MCP servers).

If you opened a local clone on Windows, also check:

```text
C:\Users\<you>\rtk-orchestration-middleware\.cursor\mcp.json
```

### Option C: Script (Git Bash or WSL on Windows)

From the repo on Windows:

```bash
bash scripts/strip-r2k-mcp-entry.sh
```

Then restart Cursor.

You do **not** need `npm run build` or `dotnet publish` on Windows for this workflow.

---

## Step 2 — Install RTK on Linux only

In your **Linux** environment (this Codespace, SSH host, or dev container):

```bash
cd /path/to/rtk-orchestration-middleware
bash scripts/install-r2k-global.sh
source ~/.bashrc
```

**Codespaces** can use:

```bash
bash scripts/setup-r2k-codespace.sh
source ~/.bashrc
```

Verify:

```bash
test -f extras/mcp-rtk-server/dist/index.js && echo "MCP build OK"
which rtk
grep -A2 r2k-optimizer ~/.cursor/mcp.json
```

Linux `~/.cursor/mcp.json` should use paths like `/workspace/...` or `/home/you/...`, not `C:\...`.

---

## Step 3 — Open the repo on Linux in Cursor

| Method | What to do |
|--------|------------|
| **GitHub Codespace** | Open the repo in a Codespace; connect Cursor to that environment. |
| **Remote - SSH** | Cursor → SSH → open `~/rtk-orchestration-middleware`. |
| **Dev Container** | Open folder with [`.devcontainer/devcontainer.json`](../.devcontainer/devcontainer.json). |

Confirm the status bar shows **SSH / Codespace / Dev Container**, not only a local `C:\` workspace.

MCP then runs on Linux where `dist/index.js` and `/usr/local/bin/rtk` exist.

---

## Step 4 — Verify

In the **remote** Cursor window:

```text
Use r2k-optimizer to run r2k_session_report
```

Or in a Linux terminal:

```bash
rtk --cursor-session-report
```

**Settings → Tools & MCP → r2k-optimizer** should connect without `MODULE_NOT_FOUND`.

---

## What not to do (Linux-only)

- Do not point Windows `%USERPROFILE%\.cursor\mcp.json` at `C:\workspace\...` unless that tree exists and you built MCP there.
- Do not assume `bash scripts/install-r2k-global.sh` in Codespace configures Windows Cursor—it only writes config on the machine where the script runs.

---

## Related docs

- [END_USER_GUIDE.md](END_USER_GUIDE.md) — daily usage, `rtk` prefix, savings commands
- [USAGE.md](USAGE.md) — technical reference, hooks, Lambda
