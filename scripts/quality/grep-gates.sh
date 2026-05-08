#!/usr/bin/env bash
set -euo pipefail

repo_root=$(git rev-parse --show-toplevel)
cd "$repo_root"

fail_on_match() {
  local name=$1
  local message=$2
  shift 2

  echo "[quality] $name"
  if git grep -nIE "$@"; then
    echo "::error::$message"
    exit 1
  fi
}

fail_on_match \
  "No EnsureCreated() in src/" \
  "EnsureCreated() forbidden - use real EF migrations." \
  'EnsureCreated\s*\(' -- 'src/**/*.cs'

fail_on_match \
  "No IgnoreQueryFilters in non-test production code" \
  "IgnoreQueryFilters forbidden in production code (allowed only in *.Tests/)." \
  'IgnoreQueryFilters\s*\(' -- 'src/TodoApp.Api/**/*.cs'

fail_on_match \
  "No bare endpoint 401/404 results" \
  "Use ProblemDetails helpers for endpoint 401/404 responses." \
  'Results\.(NotFound|Unauthorized)\s*\(\s*\)' -- 'src/TodoApp.Api/Features/**/*.cs'

fail_on_match \
  "No CORS AllowAnyOrigin paired with AllowCredentials" \
  "AllowAnyOrigin + AllowCredentials is forbidden. Whitelist exact origins." \
  'AllowAnyOrigin\s*\(.*AllowCredentials|AllowCredentials\s*\(.*AllowAnyOrigin' -- 'src/**/*.cs'

fail_on_match \
  "No localStorage / sessionStorage in auth-sensitive client modules" \
  "Auth modules must use HttpOnly cookies, not web storage." \
  'localStorage|sessionStorage' -- \
  'client/src/lib/api*' \
  'client/src/lib/api/**' \
  'client/src/auth/**' \
  'client/src/hooks/useAuth*'

echo "[quality] No ExecuteUpdate against Todo"
if git grep -nIE 'ExecuteUpdate(Async)?\s*\(' -- 'src/TodoApp.Api/**/*.cs' | grep -i 'todo'; then
  echo "::error::ExecuteUpdate bypasses RowVersion interceptor on Todo."
  exit 1
fi

fail_on_match \
  "CPM enforced - no inline PackageReference Version=" \
  "Inline PackageReference Version= forbidden - pin in Directory.Packages.props." \
  '<PackageReference [^>]*Version=' -- '**/*.csproj'

echo "[quality] Docker base images pinned by digest"
dockerfile_count=0
docker_gate_failed=0
while IFS= read -r dockerfile; do
  dockerfile_count=$((dockerfile_count + 1))
  echo "checking $dockerfile"

  while IFS= read -r line; do
    if echo "$line" | grep -Eq '^FROM[[:space:]]+\$\{[A-Z0-9_]+\}'; then
      argname=$(echo "$line" | sed -E 's/^FROM[[:space:]]+\$\{([A-Z0-9_]+)\}.*/\1/')
      if ! grep -Eq "^ARG[[:space:]]+${argname}=[^[:space:]]+@sha256:[0-9a-f]{64}$" "$dockerfile"; then
        echo "::error file=$dockerfile::ARG ${argname} must default to a tag@sha256 digest reference."
        docker_gate_failed=1
      fi
    elif ! echo "$line" | grep -Eq '^FROM[[:space:]]+[^[:space:]]+@sha256:[0-9a-f]{64}([[:space:]]+AS[[:space:]]+[A-Za-z0-9_-]+)?[[:space:]]*$'; then
      echo "::error file=$dockerfile::FROM line not pinned by tag@sha256 digest: $line"
      docker_gate_failed=1
    fi
  done < <(grep -E '^FROM[[:space:]]' "$dockerfile")
done < <(git ls-files '*Dockerfile' '**/Dockerfile' 'Dockerfile.*' '**/Dockerfile.*')

if [ "$dockerfile_count" -eq 0 ]; then
  echo "::error::No Dockerfiles found by git ls-files - gate misconfigured."
  exit 1
fi

if ! grep -Eq '^# syntax=docker/dockerfile:[^[:space:]]+@sha256:[0-9a-f]{64}$' Dockerfile; then
  echo "::error file=Dockerfile::Dockerfile syntax frontend must be pinned by digest."
  docker_gate_failed=1
fi

if [ "$docker_gate_failed" -ne 0 ]; then
  exit 1
fi

fail_on_match \
  "No transitional sprint markers in non-doc files" \
  "Stale 'Phase N will/owns/adds' marker(s) found in non-doc files. Rephrase or move to docs/." \
  'Phase [0-9]+ (will|owns?|adds?)' -- ':!docs/'

fail_on_match \
  "No hex color literals in client/src outside index.css, tokens, and assets" \
  "Hex color literals must live in client/src/index.css or **/tokens*.css. Use CSS tokens instead." \
  '#[0-9a-fA-F]{3,8}\b' -- \
  'client/src/' \
  ':!client/src/index.css' \
  ':!**/tokens*.css' \
  ':!client/src/assets/**'

echo "[quality] Theme module exists at client/src/lib/theme.ts"
test -f client/src/lib/theme.ts || {
  echo "::error::theme module must exist at client/src/lib/theme.ts (localStorage scope sentinel)"
  exit 1
}

echo "[quality] No employer-debrand references in tracked files"
# Pattern is assembled from fragments so the gate file does not itself match
# the case-insensitive ERE it enforces. Two alternatives joined with '|':
# spaced/hyphenated form, and the concatenated form.
_debrand_a='funct'
_debrand_b='ion'
_debrand_c='hea'
_debrand_d='lth'
debrand_pattern="${_debrand_a}${_debrand_b}([- ]|[[:space:]])?${_debrand_c}${_debrand_d}|${_debrand_a}${_debrand_b}${_debrand_c}${_debrand_d}"
if git grep -I -n -E -i "$debrand_pattern" -- \
    ':!docs/sprints/**' \
    ':!docs/inspiration/**' \
    ':!.claude/**'; then
  echo "::error::Debrand check failed - employer-brand variants must not appear in tracked files (sprints/inspiration/.claude excluded). Replace with 'Todo Console' / 'todo-console'."
  exit 1
fi
