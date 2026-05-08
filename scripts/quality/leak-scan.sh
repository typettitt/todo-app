#!/usr/bin/env bash
# Local-only leak scan for reviewer-feedback phrasing.
# Run before pushing significant doc changes. NOT wired to CI.
#
# Excludes:
#   - docs/sprints/**
#   - docs/inspiration/**
#   - .claude/**
#   - this script itself
#
# Exit code:
#   0 — no hits
#   1 — at least one phrase matched (triage required)

set -euo pipefail

cd "$(git rev-parse --show-toplevel)"

PHRASES=(
  'interview bar'
  'senior take-?home'
  'candidate note'
  '123qwe'
  'FH\.ToDo'
  'role swap'
  'as some reviewers'
)

SOFT_PHRASES=(
  'JSON Patch'
  'feedback'
  'inspiration'
  'audit'
  'localStorage.*(bad|wrong|avoid|vulnerable)'
)

EXCLUDES=(
  ':!docs/sprints/**'
  ':!docs/inspiration/**'
  ':!.claude/**'
  ':!scripts/quality/leak-scan.sh'
)

failed=0
echo "=== Hard phrases (any hit = leak) ==="
for p in "${PHRASES[@]}"; do
  if git grep -I -n -E -i "$p" -- "${EXCLUDES[@]}" 2>/dev/null; then
    echo "  ↑ HARD HIT: $p"
    failed=1
  fi
done

echo
echo "=== Soft phrases (review hits manually) ==="
for p in "${SOFT_PHRASES[@]}"; do
  echo "--- $p ---"
  git grep -I -n -E -i "$p" -- "${EXCLUDES[@]}" 2>/dev/null || echo "  (no hits)"
done

if [ "$failed" -ne 0 ]; then
  echo
  echo "FAIL: hard-phrase hits above. Rewrite or document as legitimate."
  exit 1
fi
echo
echo "OK: leak scan clean (soft-phrase hits, if any, must be human-reviewed)."
