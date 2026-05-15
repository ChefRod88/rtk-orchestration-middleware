#!/usr/bin/env bash
# Removes r2k-optimizer from ~/.cursor/mcp.json so local Windows Cursor stops
# spawning a Linux/Codespace MCP path (e.g. C:\workspace\...\dist\index.js).
# Safe to run on Linux if you want to clear a stale entry before remote-only use.
set -euo pipefail

MCP_JSON="${1:-${HOME}/.cursor/mcp.json}"

if [[ ! -f "${MCP_JSON}" ]]; then
  echo "No MCP config at ${MCP_JSON}; nothing to strip."
  exit 0
fi

python3 - "${MCP_JSON}" <<'PY'
import json
import sys

path = sys.argv[1]
with open(path, encoding="utf-8") as handle:
    config = json.load(handle)

servers = config.get("mcpServers", {})
if "r2k-optimizer" not in servers:
    print("r2k-optimizer not present; no change.")
    sys.exit(0)

del servers["r2k-optimizer"]
config["mcpServers"] = servers

with open(path, "w", encoding="utf-8") as handle:
    json.dump(config, handle, indent=2)
    handle.write("\n")

print(f"Removed r2k-optimizer from {path}")
print("Restart Cursor. Re-enable only on Linux after: bash scripts/install-r2k-global.sh")
PY
