#!/usr/bin/env bash
set -euo pipefail

source "scripts/lib/project-config.sh"
cmd="$(command_for_gate "lint")"

if [ -z "$cmd" ] || [ "$cmd" = "N/A" ]; then
  echo "[SKIP] G4 Lint verification: no configured command"
  exit 0
fi

echo "[RUN] G4 Lint verification: $cmd"
bash -lc "$cmd"
