#!/usr/bin/env bash
set -euo pipefail

repo_root=$(git rev-parse --show-toplevel)
cd "$repo_root"

echo "[quality] dotnet tool restore"
dotnet tool restore

echo "[quality] dotnet restore"
dotnet restore TodoApp.slnx

echo "[quality] dotnet format --verify-no-changes"
dotnet format TodoApp.slnx --verify-no-changes --severity error --no-restore

echo "[quality] dotnet build /warnaserror"
dotnet build TodoApp.slnx /warnaserror --no-restore -c Release

echo "[quality] dotnet test"
dotnet test TodoApp.slnx --no-build -c Release "$@"
