#!/usr/bin/env bash
set -euo pipefail

source "scripts/lib/project-config.sh"
cmd="$(command_for_gate "security")"

if [ -z "$cmd" ] || [ "$cmd" = "N/A" ]; then
  echo "[SKIP] G7 Security verification: no configured command"
  exit 0
fi

echo "[RUN] G7 Security verification: $cmd"
bash -lc "$cmd"
