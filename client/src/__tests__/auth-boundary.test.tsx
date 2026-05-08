import { http, HttpResponse } from 'msw';
import { Route, Routes } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import userEvent from '@testing-library/user-event';
import { screen, waitFor } from '@testing-library/react';
import { LoginForm } from '../auth/LoginForm';
import { RequireAuth } from '../auth/RequireAuth';
import { TodoListPage } from '../todos/TodoListPage';
import { server } from '../test/msw';
import { renderWithProviders } from '../test/render';
import { apiRequest, ApiProblem, configureApiClient } from '../lib/api';
import { createTestQueryClient } from '../test/render';
import { z } from 'zod';

describe('auth boundary discipline', () => {
  it('AliceLogsOut_BobLogsIn_BobsFirstPaint_ShowsNoAliceRows', async () => {
    const user = userEvent.setup();

    const aliceTodo = {
      id: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
      title: 'Alice secret todo',
      description: '',
      dueDate: null,
      priority: 'Medium' as const,
      isCompleted: false,
      completedAt: null,
      tags: [],
      rowVersion: 1,
      createdAt: '2026-05-08T12:00:00Z',
      updatedAt: '2026-05-08T12:00:00Z',
    };

    const bobTodo = {
      id: 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
      title: 'Bob own todo',
      description: '',
      dueDate: null,
      priority: 'Low' as const,
      isCompleted: false,
      completedAt: null,
      tags: [],
      rowVersion: 1,
      createdAt: '2026-05-08T12:00:00Z',
      updatedAt: '2026-05-08T12:00:00Z',
    };

    let activeUser: 'alice' | 'bob' | 'none' = 'alice';
    server.use(
      http.get('/api/auth/me', () => {
        if (activeUser === 'alice') {
          return HttpResponse.json({
            id: '11111111-1111-4111-8111-111111111111',
            email: 'alice@example.com',
            role: 'Basic',
          });
        }
        if (activeUser === 'bob') {
          return HttpResponse.json({
            id: '22222222-2222-4222-8222-222222222222',
            email: 'bob@example.com',
            role: 'Basic',
          });
        }
        return HttpResponse.json(
          {
            type: 'about:blank',
            title: 'Unauthorized.',
            status: 401,
            detail: 'Authentication required.',
            traceId: 'trace-test',
          },
          { status: 401 },
        );
      }),
      http.get('/api/todos', () => {
        const items = activeUser === 'alice' ? [aliceTodo] : activeUser === 'bob' ? [bobTodo] : [];
        return HttpResponse.json({
          items,
          page: 1,
          pageSize: 20,
          total: items.length,
          hasNext: false,
        });
      }),
      http.post('/api/auth/logout', () => {
        activeUser = 'none';
        return new HttpResponse(null, { status: 204 });
      }),
      http.post('/api/auth/login', async ({ request }) => {
        const body = (await request.json()) as { email: string };
        if (body.email.includes('bob')) {
          activeUser = 'bob';
          return HttpResponse.json({
            id: '22222222-2222-4222-8222-222222222222',
            email: 'bob@example.com',
            role: 'Basic',
          });
        }
        return HttpResponse.json(
          {
            type: 'about:blank',
            title: 'Bad request.',
            status: 400,
            detail: 'Bad credentials.',
            traceId: 'trace-test',
          },
          { status: 400 },
        );
      }),
    );

    renderWithProviders(
      <Routes>
        <Route element={<RequireAuth />}>
          <Route path="/" element={<TodoListPage />} />
          <Route path="/todos" element={<TodoListPage />} />
        </Route>
        <Route path="/login" element={<LoginForm />} />
      </Routes>,
      { route: '/todos' },
    );

    expect(await screen.findByText('Alice secret todo')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: /logout/i }));
    expect(await screen.findByLabelText(/email/i)).toBeInTheDocument();

    await user.type(screen.getByLabelText(/email/i), 'bob@example.com');
    await user.type(screen.getByLabelText('Password'), 'Password1!');
    await user.click(screen.getByRole('button', { name: /sign in/i }));

    expect(await screen.findByText('Bob own todo')).toBeInTheDocument();
    // Alice's row must NOT be in Bob's first paint.
    expect(screen.queryByText('Alice secret todo')).not.toBeInTheDocument();
  });

  it('Empty401Body_FromNginx_TriggersBoundaryClear_AndRedirect', async () => {
    const queryClient = createTestQueryClient();
    const navigations: string[] = [];
    let logoutCalls = 0;
    configureApiClient({
      navigate: (to) => navigations.push(to),
      queryClient,
    });
    queryClient.setQueryData(['private', 'user-1', 'todos', 'list', 'x'], { items: ['stale'] });
    queryClient.setQueryData(['public', 'cached'], { value: true });
    window.history.pushState(null, '', '/todos');

    server.use(
      // 401 body is HTML/empty — what nginx serves before the API gets a chance.
      http.get('/api/protected', () => new HttpResponse('<html>401</html>', { status: 401 })),
      http.post('/api/auth/logout', () => {
        logoutCalls += 1;
        return new HttpResponse(null, { status: 204 });
      }),
    );

    await expect(
      apiRequest('/api/protected', {
        method: 'GET',
        successSchema: z.object({ ok: z.boolean() }),
      }),
    ).rejects.toBeInstanceOf(ApiProblem);

    expect(logoutCalls).toBe(1);
    // queryClient.clear() drops everything, including unrelated keys — that's
    // the existing handleUnauthorized contract; the new behavior we care about
    // here is that a non-JSON 401 still REACHES this branch instead of
    // throwing a ZodError out of api.ts.
    expect(queryClient.getQueryCache().findAll()).toHaveLength(0);
    expect(navigations).toEqual(['/login?returnTo=%2Ftodos']);
  });

  it('ToastError_OnDeleteFailure_ExposesAriaLiveRegion', async () => {
    const user = userEvent.setup();
    const item = {
      id: 'cccccccc-cccc-4ccc-8ccc-cccccccccccc',
      title: 'Visible Item',
      description: '',
      dueDate: null,
      priority: 'Medium' as const,
      isCompleted: false,
      completedAt: null,
      tags: [],
      rowVersion: 1,
      createdAt: '2026-05-08T12:00:00Z',
      updatedAt: '2026-05-08T12:00:00Z',
    };

    server.use(
      http.get('/api/auth/me', () =>
        HttpResponse.json({
          id: '11111111-1111-4111-8111-111111111111',
          email: 'demo@example.com',
          role: 'Basic',
        }),
      ),
      http.get('/api/todos', () =>
        HttpResponse.json({
          items: [item],
          page: 1,
          pageSize: 20,
          total: 1,
          hasNext: false,
        }),
      ),
      http.delete('/api/todos/:id', () =>
        HttpResponse.json(
          {
            type: 'about:blank',
            title: 'Server error.',
            status: 500,
            detail: 'Server-supplied delete failure.',
            traceId: 'trace-test',
          },
          { status: 500 },
        ),
      ),
    );

    renderWithProviders(<TodoListPage />, { route: '/todos' });

    expect(await screen.findByText('Visible Item')).toBeInTheDocument();
    await user.click(screen.getByRole('listitem'));
    await user.click(screen.getByRole('button', { name: /delete todo Visible Item/i }));
    await user.click(screen.getByTestId('todo-delete-confirm'));

    const toastBody = await screen.findByText('Server-supplied delete failure.');
    expect(toastBody).toBeInTheDocument();

    // The toast surface must announce errors via an ARIA live region.
    // Walk up from the message to find the live container.
    const liveRegion = toastBody.closest('[role="status"], [role="alert"], [aria-live]');
    expect(liveRegion).not.toBeNull();
    await waitFor(() => {
      const ariaLive = liveRegion?.getAttribute('aria-live');
      const role = liveRegion?.getAttribute('role');
      expect(
        ariaLive === 'assertive' || ariaLive === 'polite' || role === 'alert' || role === 'status',
      ).toBe(true);
    });
  });
});
