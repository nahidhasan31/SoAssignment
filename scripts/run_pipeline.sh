#!/usr/bin/env bash
# Runs the pipeline's verify, build and integration stages on this machine, with
# the same commands azure-pipelines.yml runs on a hosted agent.
set -uo pipefail
cd "$(dirname "$0")/.."
SDK=mcr.microsoft.com/dotnet/sdk:8.0
PROJECTS=(MaltaShop.Order MaltaShop.Orchestration MaltaShop.Inventory MaltaShop.Payment MaltaShop.Notification)
ok=0; failed=0
stage() { printf '\n\033[1m== %s ==\033[0m\n' "$1"; }
step() {
  local label=$1; shift
  local start; start=$(date +%s)
  if output=$("$@" 2>&1); then
    printf '  PASS  %-50s %4ss\n' "$label" "$(( $(date +%s) - start ))"; ok=$((ok+1))
  else
    printf '  FAIL  %-50s\n%s\n' "$label" "$output"; failed=$((failed+1))
  fi
}

stage "verify"
step "restore and compile the solution" \
  docker run --rm -v "$PWD":/src -w /src "$SDK" sh -c "dotnet build tests/MaltaShop.Tests/MaltaShop.Tests.csproj -v q --nologo"
step "saga unit tests (in-memory harness)" \
  docker run --rm -v "$PWD":/src -w /src "$SDK" sh -c "dotnet test tests/MaltaShop.Tests/MaltaShop.Tests.csproj -v q --nologo"

stage "build"
for p in "${PROJECTS[@]}"; do
  step "$p image" bash -c "docker build -q --build-arg PROJECT=$p -t maltashop/${p,,}:local ."
done

stage "integration"
step "platform healthy (11 containers)" bash -c "docker compose ps --format '{{.Service}} {{.Status}}' | grep -cq healthy"
step "place-order saga, both failure paths" ./scripts/smoke_test.sh

printf '\n'
if [ "$failed" -eq 0 ]; then printf '\033[32mpipeline passed: %s checks, 0 failures\033[0m\n' "$ok"
else printf '\033[31mpipeline failed: %s failures\033[0m\n' "$failed"; exit 1; fi
