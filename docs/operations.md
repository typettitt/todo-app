# Operations

Long-form operational reference for Todo Console: configuration, common
commands, local development without Docker, the production-shaped compose
path, and the deeper testing and CI notes that the root README links out to.

## Configuration

The local compose path requires no environment variables. Outside Development,
the API fails fast unless a database connection string and JWT signing key are
provided.

| Variable | Default | Notes |
| --- | --- | --- |
| `ASPNETCORE_ENVIRONMENT` | `Development` in local compose and launch profile | Enables demo seed and dev key generation. |
| `ConnectionStrings__Default` | `Data Source=todoapp.db` in dev | Required outside Development. |
| `ASPNETCORE_URLS` | `http://localhost:5050` via launch profile | Use when running without launch profiles. |
| `ASPNETCORE_HTTP_PORTS` | `5000` in containers | Internal API port behind nginx. |
| `JWT_SIGNING_KEY_FILE` | unset in dev | Preferred production source; file must contain at least 32 bytes raw or base64. |
| `JWT_SIGNING_KEY` | unset in dev | Production fallback if no key file is supplied. |
| `Jwt__Issuer` / `Jwt__Audience` | `todoapp` / `todoapp` | Override together if tokens cross service boundaries. |
| `Jwt__Lifetime` | `00:30:00` | Sliding renewal is throttled server-side. |
| `Jwt__CookieSameSite` | `Strict` outside dev | Development always uses `Lax`. |
| `Internal__HealthHeader` | unset in dev | Required outside Development; send it as `X-Internal-Auth` to call `/health/ready`. |
| `TODOAPP_TRUST_KNOWN_PROXIES` | unset | Required by `docker-compose.prod.yml`; set to the trusted nginx/proxy IP or CIDR. |

## Common Commands

| Command | Purpose |
| --- | --- |
| `docker compose up --build` | Build and run the full local stack at `http://localhost:8080`. |
| `docker compose down -v` | Stop the stack and clear local compose state. |
| `dotnet test TodoApp.slnx` | Run backend tests. |
| `dotnet format TodoApp.slnx --verify-no-changes --severity error` | Verify backend formatting/analyzers. |
| `npm --prefix client run lint` | Run client lint. |
| `npm --prefix client test -- --run` | Run client tests once. |
| `npm --prefix client run build` | Type-check and build the SPA. |
| `npm --prefix client run e2e` | Run Playwright against `http://localhost:8080`; start compose first. |

## Local Development (without Docker)

Prerequisites:

- .NET 10 SDK
- Node 20+ and npm
- Docker, if you want the full compose stack or Playwright smoke tests

Run the API:

```sh
dotnet run --project src/TodoApp.Api --launch-profile http
```

The local API is pinned to `http://localhost:5050`. In `Development`, it also
exposes:

- OpenAPI JSON: `http://localhost:5050/openapi/v1.json`
- Scalar API UI: `http://localhost:5050/scalar/v1`

Run the client in another terminal:

```sh
cd client
npm install
npm run gen:api
npm run dev
```

Open the Vite URL, normally `http://localhost:5173`. Vite proxies `/api` to
`http://localhost:5050`, keeping browser requests same-origin so the HttpOnly
auth cookie works without CORS.

## Production Compose

For the production-shaped compose file:

```sh
cp .env.example .env
mkdir -p secrets
openssl rand -base64 -out secrets/jwt_signing_key 64
docker compose -f docker-compose.prod.yml --env-file .env up --build
```

`.env.example` also exposes `TODOAPP_WEB_BIND` for the published web address
and `TODOAPP_IMAGE_TAG` for image tagging. Review `TODOAPP_TRUST_KNOWN_PROXIES`
for your deployment instead of copying the example CIDR blindly. In
non-Development environments, `/health/live` stays public for container
liveness checks, while `/health/ready` requires
`X-Internal-Auth: <Internal__HealthHeader>`.

## Testing And CI (long form)

Backend tests use xUnit, `WebApplicationFactory`, and SQLite in-memory so
relational behavior, migrations, indexes, conversions, and query filters stay in
play. Frontend tests use Vitest, React Testing Library, MSW, and `vitest-axe`.
Playwright runs against the full compose stack to cover nginx proxying, cookies,
routing, and the main user flow together.

GitHub Actions validates workflows, API build/tests, client lint/tests/build,
OpenAPI generated type drift, gitleaks, Docker builds, compose quickstart, grep
guardrails, and Playwright smoke. The workflow lives at
[`.github/workflows/ci.yml`](../.github/workflows/ci.yml).

For the multi-user isolation suite (security-tagged Playwright specs), see
[`client/README.md`](../client/README.md) and run:

```sh
npm --prefix client run e2e -- --grep "@security"
```

## Local Quality Checks

A small set of local-only quality scripts live under `scripts/quality/`. They
are NOT wired to CI; they exist to give a fast manual signal before pushing
significant doc or config changes.

| Script | Purpose |
| --- | --- |
| `bash scripts/quality/grep-gates.sh` | Repo-wide grep guardrails (debrand, secrets-in-tracked-files, etc.); also runs in CI. |
| `bash scripts/quality/leak-scan.sh` | Local-only sweep for reviewer-feedback phrasing. NOT wired to CI. |

### When to run `leak-scan.sh`

Run it before pushing any doc change beyond a minor edit (anything that adds
or rewrites prose in `README.md`, `client/README.md`, `docs/decisions.md`,
`docs/operations.md`, or other tracked docs).

```sh
bash scripts/quality/leak-scan.sh
```

The script scans all tracked files; local-only paths under `docs/sprints/**`,
`docs/inspiration/**`, and `.claude/**` are excluded, along with the script
itself.

Output rules:

- **Hard phrases** (any hit is a hard fail): the script prints the offending
  file and line, marks it `HARD HIT: <phrase>`, and exits non-zero. Rewrite
  the source to remove the phrasing before pushing.
- **Soft phrases** (broad terms like `audit`, `feedback`, `inspiration`,
  `JSON Patch`): the script lists every hit but always exits zero on these
  alone. Each soft hit must be human-reviewed: keep it if the surrounding
  prose is generic technical English (e.g. `audit log`, `feedback loop`),
  rewrite it if it reads as reviewer-voice quotation.

## Decisions And Trade-offs

The narrative version of the architecture and security trade-offs lives in
[`decisions.md`](decisions.md).
