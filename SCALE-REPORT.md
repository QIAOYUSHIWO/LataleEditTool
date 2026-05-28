# SCALE Generation Report

- Project: LataleEditorTools
- Agent: cursor
- Support Level: medium
- Stack: dotnet

## Must Run

- `bash scripts/validate-config.sh`
- `bash scripts/tests/run.sh`
- `bash scripts/gates/all.sh --dry-run`

## Unsupported Or Degraded

- skill: deep-interview
- skill: autopilot

## Honest Delivery

- Do not claim tests passed unless the command was actually run and exited 0.
- Dry-run does not prove quality gates passed.
- Skipped or missing tools must be listed as unverified items.
