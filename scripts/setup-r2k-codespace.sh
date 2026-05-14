#!/usr/bin/env bash
set -euo pipefail

# Installs/refreshes the local RTK CLI and wires common Codespaces shells to the AWS
# optimizer endpoint. The bashrc block is idempotent so the script is safe to
# run from devcontainer postCreateCommand and by hand.

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
BASHRC="${HOME}/.bashrc"
RTK_API_URL_DEFAULT="https://awv3cnqcx0.execute-api.us-east-2.amazonaws.com/OptimizeCommand"
RTK_PROMPT_API_URL_DEFAULT="${RTK_API_URL_DEFAULT}"
RTK_SHIM_DIR="${HOME}/.local/share/r2k/shims"
RTK_USER_CONFIG_DIR="${HOME}/.config/r2k"
START_MARKER="# >>> r2k optimizer aliases >>>"
END_MARKER="# <<< r2k optimizer aliases <<<"

bash "${ROOT}/scripts/install-rtk.sh"

mkdir -p "${RTK_SHIM_DIR}" "${RTK_USER_CONFIG_DIR}"
cp "${ROOT}/.r2k/hooks.json" "${RTK_USER_CONFIG_DIR}/hooks.json"
rm -f "${RTK_SHIM_DIR}"/*

if command -v python3 >/dev/null 2>&1; then
  mapfile -t enabled_interceptors < <(python3 - "${ROOT}/.r2k/hooks.json" <<'PY'
import json
import sys

with open(sys.argv[1], encoding="utf-8") as handle:
    config = json.load(handle)

for interceptor in config.get("interceptors", []):
    if interceptor.get("enabled", True):
        command = interceptor.get("command")
        if command:
            print(command)
PY
)
else
  enabled_interceptors=(git npm npx)
fi

for command_name in "${enabled_interceptors[@]}"; do
  ln -sf /usr/local/bin/rtk "${RTK_SHIM_DIR}/${command_name}"
done

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
export RTK_CLI_PATH="/usr/local/bin/rtk"
export PATH="${RTK_SHIM_DIR}:\${PATH}"
unset RTK_FUNCTION_KEY

# Route explicit agent/automation commands through R2K.
alias agent='rtk agent'

# Common safe aliases for agent-run terminal commands. Registry shims in
# ${RTK_SHIM_DIR} also intercept these without aliases.
alias git='rtk git'
alias npm='rtk npm'
alias npx='rtk npx'
${END_MARKER}
EOF

mv "${tmp}" "${BASHRC}"

echo "R2K Codespaces shell setup complete."
echo "Open a new terminal or run: source ${BASHRC}"
echo "Verify with: type agent && type git && rtk --optimize-prompt 'optimize    this prompt'"
