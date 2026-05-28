#!/usr/bin/env bash
set -euo pipefail

source "scripts/lib/project-config.sh"
cmd="$(command_for_gate "coverage")"

if [ -z "$cmd" ] || [ "$cmd" = "N/A" ]; then
  echo "[SKIP] G6 Coverage verification: no configured command"
  exit 0
fi

echo "[RUN] G6 Coverage verification: $cmd"
bash -lc "$cmd"
