#!/usr/bin/env bash
set -euo pipefail
# Builds R2K.CLI as a standalone linux-x64 single file and installs to /usr/local/bin/rtk.
# Requires dotnet SDK + sudo write access (GitHub Codespaces default).

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
CLI_DIR="${ROOT}/R2K.CLI"
OUT_PUBLISH="${CLI_DIR}/bin/Release/net8.0/linux-x64/publish/R2K.CLI"

if [[ ! -d "${CLI_DIR}" ]]; then
  echo "R2K.CLI not found at ${CLI_DIR}" >&2
  exit 1
fi

dotnet publish "${CLI_DIR}/R2K.CLI.csproj" \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -v minimal

sudo install -m 755 "${OUT_PUBLISH}" /usr/local/bin/rtk
echo "Installed: $(command -v rtk)"
