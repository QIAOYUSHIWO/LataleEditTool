#!/usr/bin/env bash
set -euo pipefail

source "scripts/lib/project-config.sh"
cmd="$(command_for_gate "test")"

if [ -z "$cmd" ] || [ "$cmd" = "N/A" ]; then
  echo "[SKIP] G5 Test verification: no configured command"
  exit 0
fi

echo "[RUN] G5 Test verification: $cmd"
bash -lc "$cmd"
