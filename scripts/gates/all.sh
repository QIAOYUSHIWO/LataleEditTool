#!/usr/bin/env bash
set -euo pipefail

DRY_RUN=0
if [ "${1:-}" = "--dry-run" ]; then
  DRY_RUN=1
fi

gates=(G1 G2 G3 G4 G5 G6 G7)
failures=0

for gate in "${gates[@]}"; do
  script="scripts/gates/${gate}-verify.sh"
  if [ ! -x "$script" ]; then
    echo "[FAIL] missing or non-executable $script"
    failures=$((failures + 1))
    continue
  fi
  if [ "$DRY_RUN" -eq 1 ]; then
    echo "[DRY-RUN] $script is schedulable"
  else
    bash "$script" || failures=$((failures + 1))
  fi
done

if [ "$DRY_RUN" -eq 1 ]; then
  echo "[OK] 干运行完成，未执行实际门控"
  exit 0
fi

if [ "$failures" -gt 0 ]; then
  echo "[FAIL] $failures gate(s) failed"
  exit 1
fi

echo "[OK] 所有门控通过"
