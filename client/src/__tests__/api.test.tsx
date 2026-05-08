import { http, HttpResponse } from 'msw';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { z } from 'zod';
import { apiRequest, ApiProblem, ApiResponseParseError, configureApiClient } from '../lib/api';
import { toast } from '../lib/toast';
import { getMe } from '../auth/api';
import { server } from '../test/msw';
import { createTestQueryClient } from '../test/render';

const successSchema = z.object({
  ok: z.boolean(),
});

describe('api client', () => {
  beforeEach(() => {
    window.history.pushState(null, '', '/');
  });

  it('apiClient_On400_ParsesProblemDetails_ThrowsApiProblem_NotZodError', async () => {
    server.use(
      http.get('/api/example', () =>
        HttpResponse.json(problemDetails({ status: 400 }), { status: 400 }),
      ),
    );

    await expect(
      apiRequest('/api/example', {
        method: 'GET',
        successSchema,
      }),
    ).rejects.toBeInstanceOf(ApiProblem);

    await expect(
      apiRequest('/api/example', {
        method: 'GET',
        successSchema,
      }),
    ).rejects.not.toBeInstanceOf(z.ZodError);
  });

  it('apiClient_On401_CallsLogout_ClearsCache_RedirectsToLogin', async () => {
    const queryClient = createTestQueryClient();
    const navigations: string[] = [];
    let logoutCalls = 0;
    configureApiClient({
      navigate: (to) => navigations.push(to),
      queryClient,
    });
    queryClient.setQueryData(['cached'], { value: true });
    window.history.pushState(null, '', '/todos');

    server.use(
      http.get('/api/protected', () =>
        HttpResponse.json(problemDetails({ status: 401 }), { status: 401 }),
      ),
      http.post('/api/auth/logout', () => {
        logoutCalls += 1;
        return new HttpResponse(null, { status: 204 });
      }),
    );

    await expect(
      apiRequest('/api/protected', {
        method: 'GET',
        successSchema,
      }),
    ).rejects.toBeInstanceOf(ApiProblem);

    expect(logoutCalls).toBe(1);
    expect(queryClient.getQueryCache().findAll()).toHaveLength(0);
    expect(navigations).toEqual(['/login?returnTo=%2Ftodos']);
  });

  it('apiClient_On401_DoesNotRecurse_WhenLogoutItselfReturns401', async () => {
    const queryClient = createTestQueryClient();
    let logoutCalls = 0;
    configureApiClient({
      navigate: vi.fn(),
      queryClient,
    });

    server.use(
      http.get('/api/protected', () =>
        HttpResponse.json(problemDetails({ status: 401 }), { status: 401 }),
      ),
      http.post('/api/auth/logout', () => {
        logoutCalls += 1;
        return HttpResponse.json(problemDetails({ status: 401 }), {
          status: 401,
        });
      }),
    );

    await expect(
      apiRequest('/api/protected', {
        method: 'GET',
        successSchema,
      }),
    ).rejects.toBeInstanceOf(ApiProblem);

    expect(logoutCalls).toBe(1);
  });

  it('apiClient_OnMe401_DoesNotShowToast_DoesNotRedirect_DuringInitialProbe', async () => {
    const queryClient = createTestQueryClient();
    const navigate = vi.fn();
    const toastSpy = vi.spyOn(toast, 'error');
    configureApiClient({
      navigate,
      queryClient,
    });

    server.use(
      http.get('/api/auth/me', () =>
        HttpResponse.json(problemDetails({ status: 401 }), { status: 401 }),
      ),
    );

    await expect(getMe()).rejects.toBeInstanceOf(ApiProblem);

    expect(navigate).not.toHaveBeenCalled();
    expect(toastSpy).not.toHaveBeenCalled();
  });

  it('apiClient_OnZodParseFailure_ThrowsTypedError_ShowsToast', async () => {
    const toastSpy = vi.spyOn(toast, 'error');

    server.use(http.get('/api/drift', () => HttpResponse.json({ ok: 'yes' })));

    await expect(
      apiRequest('/api/drift', {
        method: 'GET',
        successSchema,
      }),
    ).rejects.toBeInstanceOf(ApiResponseParseError);

    expect(toastSpy).toHaveBeenCalledWith('The server returned an unexpected response.');
  });
});

function problemDetails({ errors, status }: { errors?: Record<string, string[]>; status: number }) {
  return {
    type: 'about:blank',
    title: status === 401 ? 'Unauthorized.' : 'Request failed.',
    status,
    detail: status === 401 ? 'Authentication is required.' : 'Bad request.',
    instance: '/api/test',
    traceId: 'trace-test',
    ...(errors ? { errors } : {}),
  };
}
