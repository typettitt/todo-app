#!/usr/bin/env bash
set -euo pipefail

repo_root=$(git rev-parse --show-toplevel)
cd "$repo_root"

echo "[quality] client lint"
npm --prefix client run lint

echo "[quality] client unit tests"
npm --prefix client test -- --run

echo "[quality] client build"
npm --prefix client run build
