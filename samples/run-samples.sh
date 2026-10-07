#!/usr/bin/env bash
# Runs dotnet-diag against every sample log in this folder.
#   ./samples/run-samples.sh                 builds once, then runs from source
#   ./samples/run-samples.sh --installed     uses the installed global tool
#   FILTER=package ./samples/run-samples.sh  only samples whose name contains "package"
set -euo pipefail
cd "$(dirname "$0")/.."

installed=false
[ "${1:-}" = "--installed" ] && { installed=true; shift; }
$installed || dotnet build src/DotnetDiag.Cli -nologo -v quiet

for sample in samples/*.binlog samples/*.txt; do
  name=$(basename "${sample%.*}")
  [[ "$name" == *"${FILTER:-}"* ]] || continue
  printf '\n\033[36m=== %s ===\033[0m\n' "$(basename "$sample")"
  if $installed; then
    dotnet-diag analyze "$sample" --repo "eval/scenarios/$name" "$@"
  else
    dotnet run --project src/DotnetDiag.Cli --no-build -- analyze "$sample" --repo "eval/scenarios/$name" "$@"
  fi
done
