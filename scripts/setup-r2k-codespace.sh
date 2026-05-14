#!/usr/bin/env bash
set -euo pipefail

# Installs/refreshes the local RTK CLI and wires common Codespaces shells to the AWS
# optimizer endpoint. The bashrc block is idempotent so the script is safe to
# run from devcontainer postCreateCommand and by hand.

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
BASHRC="${HOME}/.bashrc"
RTK_API_URL_DEFAULT="https://awv3cnqcx0.execute-api.us-east-2.amazonaws.com/OptimizeCommand"
START_MARKER="# >>> r2k optimizer aliases >>>"
END_MARKER="# <<< r2k optimizer aliases <<<"

bash "${ROOT}/scripts/install-rtk.sh"

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
unset RTK_FUNCTION_KEY

# Route explicit agent/automation commands through R2K.
alias agent='rtk agent'

# Common safe aliases for agent-run terminal commands.
alias git='rtk git'
alias npm='rtk npm'
alias npx='rtk npx'
${END_MARKER}
EOF

mv "${tmp}" "${BASHRC}"

echo "R2K Codespaces shell setup complete."
echo "Open a new terminal or run: source ${BASHRC}"
echo "Verify with: type agent && type git"
