#!/usr/bin/env bash
set -euo pipefail

PROJECT_CONFIG=".agent/project.json"

if [ ! -f "$PROJECT_CONFIG" ]; then
  echo "[FAIL] missing $PROJECT_CONFIG" >&2
  exit 1
fi

json_get() {
  node -e "const fs=require('fs'); const p=process.argv[1]; const data=JSON.parse(fs.readFileSync('$PROJECT_CONFIG','utf8')); const value=p.split('.').reduce((o,k)=>o&&o[k],data); if (value === undefined || value === null) process.exit(2); console.log(value)" "$1"
}

command_for_gate() {
  local key="$1"
  json_get "commands.$key" 2>/dev/null || true
}
