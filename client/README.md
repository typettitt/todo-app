# Todo Console SPA

React, Vite, TypeScript, TanStack Query, React Hook Form, Zod, MSW, Vitest, and
Playwright for the Todo Console frontend.

For the one-command full-stack path, use the repo root:

```sh
docker compose up --build
```

Then open `http://localhost:8080`.

## Local SPA Development

Run the API first so OpenAPI type generation has a live contract:

```sh
dotnet run --project ../src/TodoApp.Api --launch-profile http
```

The API listens at `http://localhost:5050`.

In another terminal:

```sh
npm install
npm run gen:api
npm run dev
```

Open the Vite URL, normally `http://localhost:5173`. Vite proxies `/api` to the
API, so browser calls stay same-origin and the HttpOnly auth cookie works
without CORS.

## Commands

| Command                           | Purpose                                                                             |
| --------------------------------- | ----------------------------------------------------------------------------------- |
| `npm run dev`                     | Start the Vite dev server.                                                          |
| `npm run gen:api`                 | Regenerate `src/lib/openapi-types.ts` from `http://localhost:5050/openapi/v1.json`. |
| `npm run build`                   | Type-check with `tsc -b` and build `dist/`.                                         |
| `npm run preview`                 | Preview the production build locally.                                               |
| `npm run lint`                    | Run ESLint.                                                                         |
| `npm test -- --run`               | Run Vitest once.                                                                    |
| `npm run test`                    | Run Vitest in watch mode.                                                           |
| `npm run test:ui`                 | Run the Vitest UI.                                                                  |
| `npm run e2e`                     | Run Playwright against `http://localhost:8080`; start compose first.                |
| `npm run e2e -- --grep @security` | Run the multi-user isolation security specs only.                                   |
| `npm run format`                  | Format with Prettier.                                                               |

## E2E Tests

Playwright targets the Docker compose stack, not the Vite dev server:

```sh
cd ..
docker compose up -d --build
cd client
npm run e2e
```

This catches nginx proxy, cookie, and routing regressions that isolated frontend
tests cannot see.

The multi-user isolation suite is tagged `@security` and exercises Alice/Bob
cookie boundaries, wrong-owner item routes, list isolation, and concurrent
creates:

```sh
npx playwright test e2e/multi-user-isolation.spec.ts --reporter=line
```

CI runs the compose-backed Playwright job with `--grep "@smoke|@security"` so
the main smoke path and cross-user isolation regressions gate every PR.

## Folder Layout

```text
.
├── e2e/                 # Playwright specs
├── scripts/             # screenshot helper
└── src/
    ├── auth/            # forms, auth API, schemas, route guard
    ├── components/ui/   # shared UI primitives
    ├── lib/             # API client, query client, ProblemDetails, OpenAPI types
    ├── todos/           # todo list, create/edit/delete/complete flows
    ├── test/            # Vitest setup, MSW, render helpers
    └── __tests__/       # RTL/MSW/unit/a11y coverage
```

## Auth Boundary

The JWT lives in an HttpOnly cookie set by the API. Client auth modules should
not use `localStorage` or `sessionStorage`; repo-level lint and CI grep gates
enforce that boundary.

## Styling

The app uses CSS modules plus global tokens in `src/index.css`. Keep new color
literals in token/global CSS locations and consume them through CSS variables in
components.
