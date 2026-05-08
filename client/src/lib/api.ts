import type { QueryClient } from '@tanstack/react-query';
import { z } from 'zod';
import { ProblemDetailsSchema, type ProblemDetails } from './problemDetails';
import { queryClient as defaultQueryClient } from './queryClient';
import { toast } from './toast';

type Navigate = (to: string, options?: { replace?: boolean }) => void;

type ApiClientRuntime = {
  navigate?: Navigate;
  queryClient: QueryClient;
};

type ApiRequestOptions<T> = Omit<RequestInit, 'body' | 'credentials'> & {
  body?: unknown;
  skipAuthHandling?: boolean;
  successSchema: z.ZodType<T>;
};

const defaultRuntime: ApiClientRuntime = {
  queryClient: defaultQueryClient,
};

let runtime: ApiClientRuntime = defaultRuntime;
let unauthorizedHandling: Promise<void> | undefined;

export class ApiProblem extends Error {
  readonly errors: Record<string, string[]>;
  readonly problem: ProblemDetails;
  readonly status: number;

  constructor(status: number, problem: ProblemDetails) {
    super(problem.detail ?? problem.title);
    this.name = 'ApiProblem';
    this.status = status;
    this.problem = problem;
    this.errors = problem.errors ?? {};
  }
}

export class ApiResponseParseError extends Error {
  readonly status: number;
  readonly cause: z.ZodError;

  constructor(status: number, cause: z.ZodError) {
    super('The server returned an unexpected response.');
    this.name = 'ApiResponseParseError';
    this.status = status;
    this.cause = cause;
  }
}

export function configureApiClient(config: Partial<ApiClientRuntime>) {
  const previous = runtime;
  runtime = {
    ...runtime,
    ...config,
    queryClient: config.queryClient ?? runtime.queryClient,
  };

  return () => {
    runtime = previous;
  };
}

export function resetApiClient() {
  runtime = defaultRuntime;
  unauthorizedHandling = undefined;
}

export async function apiRequest<T>(path: string, options: ApiRequestOptions<T>): Promise<T> {
  const { body, skipAuthHandling = false, successSchema, ...init } = options;
  const headers = new Headers(init.headers);

  if (body !== undefined && !headers.has('Content-Type')) {
    headers.set('Content-Type', 'application/json');
  }

  const response = await fetch(path, {
    ...init,
    body: body === undefined ? undefined : JSON.stringify(body),
    credentials: 'include',
    headers,
  });

  const responseBody = await readJson(response);

  if (response.ok) {
    const parsed = successSchema.safeParse(responseBody);
    if (!parsed.success) {
      toast.error('The server returned an unexpected response.');
      throw new ApiResponseParseError(response.status, parsed.error);
    }

    return parsed.data;
  }

  // Status-first: branch on 401 BEFORE attempting to parse ProblemDetails.
  // nginx and other intermediaries can return a 401 with an HTML body (or
  // empty), and a strict zod parse would throw a ZodError that the boundary
  // handlers cannot recognize as "session lost". Use safeParse and fall back
  // to a synthesized ApiProblem so the 401 path always lands on
  // handleUnauthorized().
  const problemParse = ProblemDetailsSchema.safeParse(responseBody);
  const problem: ProblemDetails = problemParse.success
    ? problemParse.data
    : {
        status: response.status,
        title: response.statusText || 'Request failed.',
        detail: undefined,
        errors: {},
      };
  const apiProblem = new ApiProblem(response.status, problem);

  if (response.status === 401 && !skipAuthHandling) {
    await handleUnauthorized();
  }

  throw apiProblem;
}

async function readJson(response: Response) {
  if (response.status === 204) {
    return null;
  }

  const text = await response.text();
  if (text.length === 0) {
    return null;
  }

  try {
    return JSON.parse(text) as unknown;
  } catch {
    // Tolerate non-JSON error bodies (e.g. nginx HTML 401). Caller will
    // safeParse the result and synthesize a ProblemDetails fallback.
    return null;
  }
}

async function handleUnauthorized() {
  // Collapse parallel 401s into one logout/redirect so startup request bursts do not race.
  unauthorizedHandling ??= (async () => {
    await fetch('/api/auth/logout', {
      credentials: 'include',
      method: 'POST',
    }).catch(() => undefined);

    runtime.queryClient.clear();
    navigateToLogin();
  })().finally(() => {
    unauthorizedHandling = undefined;
  });

  await unauthorizedHandling;
}

function navigateToLogin() {
  const currentPath = window.location.pathname + window.location.search + window.location.hash;
  const returnTo = currentPath.startsWith('/login') ? '/' : currentPath || '/';
  const target = `/login?returnTo=${encodeURIComponent(returnTo)}`;

  if (runtime.navigate) {
    runtime.navigate(target, { replace: true });
    return;
  }

  window.history.replaceState(null, '', target);
  window.dispatchEvent(new Event('popstate'));
}
