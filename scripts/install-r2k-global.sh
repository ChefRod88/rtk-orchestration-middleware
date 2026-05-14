#!/usr/bin/env bash
set -euo pipefail

# Global RTK install for Cursor and terminal use across repositories.
# - Installs /usr/local/bin/rtk
# - Copies repo hooks.json to ~/.config/r2k/hooks.json
# - Creates command shims under ~/.local/share/r2k/shims
# - Builds the MCP server
# - Adds/updates ~/.cursor/mcp.json entry r2k-optimizer without removing other servers
# - Adds/updates ~/.cursor/hooks.json and global optimize-prompt hook where supported

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
RTK_API_URL_DEFAULT="${RTK_API_URL:-https://awv3cnqcx0.execute-api.us-east-2.amazonaws.com/OptimizeCommand}"
RTK_PROMPT_API_URL_DEFAULT="${RTK_PROMPT_API_URL:-${RTK_API_URL_DEFAULT}}"
RTK_CLI_PATH_DEFAULT="${RTK_CLI_PATH:-/usr/local/bin/rtk}"
RTK_CONFIG_DIR="${HOME}/.config/r2k"
RTK_SHIM_DIR="${HOME}/.local/share/r2k/shims"
CURSOR_CONFIG_DIR="${HOME}/.cursor"
CURSOR_MCP_JSON="${CURSOR_CONFIG_DIR}/mcp.json"
CURSOR_HOOKS_DIR="${CURSOR_CONFIG_DIR}/hooks"
CURSOR_HOOKS_JSON="${CURSOR_CONFIG_DIR}/hooks.json"
MCP_DIR="${ROOT}/extras/mcp-rtk-server"
MCP_DIST="${MCP_DIR}/dist/index.js"
PROMPT_HOOK_SOURCE="${ROOT}/.cursor/hooks/optimize-prompt.py"
PROMPT_HOOK_TARGET="${CURSOR_HOOKS_DIR}/optimize-prompt.py"
BASHRC="${HOME}/.bashrc"
START_MARKER="# >>> r2k global tool >>>"
END_MARKER="# <<< r2k global tool <<<"

bash "${ROOT}/scripts/install-rtk.sh"

mkdir -p "${RTK_CONFIG_DIR}" "${RTK_SHIM_DIR}" "${CURSOR_CONFIG_DIR}" "${CURSOR_HOOKS_DIR}"
cp "${ROOT}/hooks.json" "${RTK_CONFIG_DIR}/hooks.json"
rm -f "${RTK_SHIM_DIR}"/*

mapfile -t hook_commands < <(python3 - "${RTK_CONFIG_DIR}/hooks.json" <<'PY'
import json
import sys

with open(sys.argv[1], encoding="utf-8") as handle:
    config = json.load(handle)

hooks = config.get("hooks", config) if isinstance(config, dict) else config
for hook in hooks:
    command = hook.get("command")
    if command:
        print(command)
PY
)

for command_name in "${hook_commands[@]}"; do
  ln -sf "${RTK_CLI_PATH_DEFAULT}" "${RTK_SHIM_DIR}/${command_name}"
done

if [[ -f "${MCP_DIR}/package-lock.json" ]]; then
  npm --prefix "${MCP_DIR}" ci
else
  npm --prefix "${MCP_DIR}" install
fi
npm --prefix "${MCP_DIR}" run build

python3 - "${CURSOR_MCP_JSON}" "${MCP_DIST}" "${RTK_CLI_PATH_DEFAULT}" "${RTK_API_URL_DEFAULT}" "${RTK_PROMPT_API_URL_DEFAULT}" <<'PY'
import json
import os
import sys

config_path, mcp_dist, rtk_cli_path, api_url, prompt_api_url = sys.argv[1:6]
if os.path.exists(config_path):
    with open(config_path, encoding="utf-8") as handle:
        try:
            config = json.load(handle)
        except json.JSONDecodeError:
            config = {}
else:
    config = {}

servers = config.setdefault("mcpServers", {})
servers["r2k-optimizer"] = {
    "command": "node",
    "args": [mcp_dist],
    "env": {
        "RTK_CLI_PATH": rtk_cli_path,
        "RTK_API_URL": api_url,
        "RTK_PROMPT_API_URL": prompt_api_url,
    },
}

with open(config_path, "w", encoding="utf-8") as handle:
    json.dump(config, handle, indent=2)
    handle.write("\n")
PY

install -m 755 "${PROMPT_HOOK_SOURCE}" "${PROMPT_HOOK_TARGET}"
python3 - "${CURSOR_HOOKS_JSON}" "${PROMPT_HOOK_TARGET}" <<'PY'
import json
import os
import sys

config_path, hook_path = sys.argv[1:3]
if os.path.exists(config_path):
    with open(config_path, encoding="utf-8") as handle:
        try:
            config = json.load(handle)
        except json.JSONDecodeError:
            config = {}
else:
    config = {}

config["version"] = config.get("version", 1)
hooks = config.setdefault("hooks", {})
entries = hooks.setdefault("beforeSubmitPrompt", [])
entries = [entry for entry in entries if entry.get("command") not in (hook_path, "hooks/optimize-prompt.py")]
entries.append({
    "command": hook_path,
    "matcher": "UserPromptSubmit",
    "timeout": 10,
    "failClosed": False,
})
hooks["beforeSubmitPrompt"] = entries

with open(config_path, "w", encoding="utf-8") as handle:
    json.dump(config, handle, indent=2)
    handle.write("\n")
PY

tmp="$(mktemp)"
if [[ -f "${BASHRC}" ]]; then
  awk -v start="${START_MARKER}" -v end="${END_MARKER}" '
    $0 == start { skip = 1; next }
    $0 == end { skip = 0; next }
    skip != 1 { print }
  ' "${BASHRC}" > "${tmp}"
else
  : > "${tmp}"
fi

cat >> "${tmp}" <<EOF
${START_MARKER}
export RTK_API_URL="${RTK_API_URL_DEFAULT}"
export RTK_PROMPT_API_URL="${RTK_PROMPT_API_URL_DEFAULT}"
export RTK_CLI_PATH="${RTK_CLI_PATH_DEFAULT}"
export PATH="${RTK_SHIM_DIR}:\${PATH}"
${END_MARKER}
EOF

mv "${tmp}" "${BASHRC}"

echo "RTK global Cursor tool installed."
echo "Global hooks: ${RTK_CONFIG_DIR}/hooks.json"
echo "Command shims: ${RTK_SHIM_DIR}"
echo "Cursor MCP: ${CURSOR_MCP_JSON}"
echo "Cursor prompt hook: ${CURSOR_HOOKS_JSON}"
echo "Restart Cursor, then verify Tools & MCP shows r2k-optimizer."
echo "Open a new terminal or run: source ${BASHRC}"
