import { delay, http, HttpResponse } from 'msw';
import { format } from 'date-fns';
import { afterEach, describe, expect, it, vi } from 'vitest';
import userEvent from '@testing-library/user-event';
import { screen, waitFor, within } from '@testing-library/react';
import { axe } from 'vitest-axe';
import { LoginForm } from '../../auth/LoginForm';
import { RegisterForm } from '../../auth/RegisterForm';
import { TodoForm } from '../../todos/TodoForm';
import { TodoListPage } from '../../todos/TodoListPage';
import { defaultTodoListParams } from '../../todos/queryKeys';
import type { PagedTodos, TodoResponse } from '../../todos/schemas';
import { server } from '../../test/msw';
import { renderWithProviders } from '../../test/render';

describe('todos UI', () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  it('TodoForm_OnValidationError_StaysMounted_ShowsFieldErrors', async () => {
    const user = userEvent.setup();

    server.use(
      authHandler(),
      http.post('/api/todos', () =>
        HttpResponse.json(
          problemDetails({
            errors: {
              title: ['Server says title is invalid.'],
            },
            status: 400,
          }),
          { status: 400 },
        ),
      ),
    );

    renderWithProviders(<TodoForm listParams={defaultTodoListParams} mode="create" />);

    const title = screen.getByLabelText(/title/i);
    await user.type(title, 'Invalid server title');
    await user.click(screen.getByTestId('todo-create-submit'));

    expect(await screen.findByText('Server says title is invalid.')).toBeInTheDocument();
    expect(screen.getByTestId('todo-create-submit')).toBeInTheDocument();
    expect(title).toHaveFocus();
  });

  it('TodoForm_On409_StaysMounted_ShowsReloadBanner', async () => {
    const user = userEvent.setup();

    server.use(
      authHandler(),
      http.put('/api/todos/:id', () =>
        HttpResponse.json(
          problemDetails({
            errors: {
              rowVersion: ['The todo was changed by another request.'],
            },
            status: 409,
          }),
          { status: 409 },
        ),
      ),
    );

    renderWithProviders(
      <TodoForm initialTodo={todo()} listParams={defaultTodoListParams} mode="edit" />,
    );

    await user.click(screen.getByTestId('todo-edit-submit'));

    expect(await screen.findByText('Someone else changed this. Reload?')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /reload/i })).toBeInTheDocument();
    expect(screen.getByTestId('todo-edit-submit')).toBeInTheDocument();
  });

  it('TodoForm_EditAlreadyOverdueTodo_KeepsPastDueDate_AndSavesWithoutFutureValidation', async () => {
    const user = userEvent.setup();
    const overdue = todo({ dueDate: '2020-01-15', title: 'Stale chore' });
    let putBody: { dueDate?: string | null } | undefined;

    server.use(
      authHandler(),
      http.put('/api/todos/:id', async ({ request }) => {
        putBody = (await request.json()) as { dueDate?: string | null };
        return HttpResponse.json({ ...overdue, title: 'Stale chore (edited)', rowVersion: 2 });
      }),
    );

    renderWithProviders(
      <TodoForm initialTodo={overdue} listParams={defaultTodoListParams} mode="edit" />,
    );

    const dueDate = screen.getByLabelText(/due date/i) as HTMLInputElement;
    expect(dueDate.value).toBe('2020-01-15');

    const title = screen.getByLabelText(/title/i);
    await user.clear(title);
    await user.type(title, 'Stale chore (edited)');
    await user.click(screen.getByTestId('todo-edit-submit'));

    await waitFor(() => expect(putBody?.dueDate).toBe('2020-01-15'));
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('DueTodayTab_PassesLocalTodayString', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    vi.setSystemTime(new Date('2026-05-07T02:30:00.000Z'));
    const expectedLocalToday = format(new Date(), 'yyyy-MM-dd');
    const requestedTodays: string[] = [];
    const requestedStatuses: string[] = [];

    server.use(
      authHandler(),
      http.get('/api/todos', ({ request }) => {
        const requestUrl = new URL(request.url);
        requestedTodays.push(requestUrl.searchParams.get('today') ?? '');
        requestedStatuses.push(requestUrl.searchParams.get('status') ?? '');
        return HttpResponse.json(paged([]));
      }),
    );

    renderWithProviders(<TodoListPage />, {
      route: '/todos?status=DueToday',
    });

    await waitFor(() => expect(requestedTodays).toContain(expectedLocalToday));
    expect(requestedStatuses).toContain('DueToday');
  });

  it('DeleteFails_RowStays_ToastShown', async () => {
    const user = userEvent.setup();
    const item = todo({ title: 'Keep me' });

    server.use(
      authHandler(),
      http.get('/api/todos', () => HttpResponse.json(paged([item]))),
      http.delete('/api/todos/:id', () =>
        HttpResponse.json(problemDetails({ status: 500 }), { status: 500 }),
      ),
    );

    renderWithProviders(<TodoListPage />, { route: '/todos' });

    expect(await screen.findByText('Keep me')).toBeInTheDocument();

    await user.click(screen.getByRole('listitem'));
    await user.click(screen.getByRole('button', { name: `Delete todo ${item.title}` }));
    await user.click(screen.getByTestId('todo-delete-confirm'));

    // Surface server-supplied detail/title via toast.
    expect(await screen.findByText('Request failed.')).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 4, name: 'Keep me' })).toBeInTheDocument();
  });

  it('Toggle409_RefetchesTodo_AndShowsRecoveryMessage', async () => {
    const user = userEvent.setup();
    const stale = todo({ title: 'Stale row', rowVersion: 1 });
    const fresh = todo({ title: 'Fresh row', rowVersion: 2 });

    server.use(
      authHandler(),
      http.get('/api/todos', () => HttpResponse.json(paged([fresh]))),
      http.get('/api/todos/:id', () => HttpResponse.json(fresh)),
      http.patch('/api/todos/:id/complete', () =>
        HttpResponse.json(problemDetails({ status: 409 }), { status: 409 }),
      ),
    );

    renderWithProviders(<TodoListPage />, { route: '/todos' });

    expect(await screen.findByText('Fresh row')).toBeInTheDocument();
    await user.click(screen.getByRole('listitem'));
    await user.click(screen.getByRole('button', { name: /mark .*fresh row.* complete/i }));

    expect(
      await screen.findByText(
        '"Fresh row" was refreshed. Review the latest version and try again.',
      ),
    ).toBeInTheDocument();
    expect(screen.queryByText(stale.title)).not.toBeInTheDocument();
  });

  it('Delete409_RefetchesTodo_AndKeepsDialogRecoveryPath', async () => {
    const user = userEvent.setup();
    const item = todo({ title: 'Changed elsewhere', rowVersion: 1 });

    server.use(
      authHandler(),
      http.get('/api/todos', () => HttpResponse.json(paged([item]))),
      http.get('/api/todos/:id', () => HttpResponse.json({ ...item, rowVersion: 2 })),
      http.delete('/api/todos/:id', () =>
        HttpResponse.json(problemDetails({ status: 409 }), { status: 409 }),
      ),
    );

    renderWithProviders(<TodoListPage />, { route: '/todos' });

    expect(await screen.findByText('Changed elsewhere')).toBeInTheDocument();
    await user.click(screen.getByRole('listitem'));
    await user.click(screen.getByRole('button', { name: `Delete todo ${item.title}` }));
    await user.click(screen.getByTestId('todo-delete-confirm'));

    expect(
      await screen.findByText(
        '"Changed elsewhere" was refreshed. Review the latest version and try again.',
      ),
    ).toBeInTheDocument();
    expect(screen.getByTestId('dialog-delete-todo')).toBeInTheDocument();
  });

  it('TodoListPage_AxeSmoke_HasNoWcag21AaViolations', async () => {
    const today = '2026-05-07';
    const items = [
      todo({
        dueDate: '2026-05-10',
        id: '22222222-2222-4222-8222-222222222201',
        title: 'Active todo',
      }),
      todo({
        dueDate: '2026-05-01',
        id: '22222222-2222-4222-8222-222222222202',
        isCompleted: true,
        title: 'Completed todo',
      }),
      todo({
        dueDate: today,
        id: '22222222-2222-4222-8222-222222222203',
        title: 'Due today todo',
      }),
    ];

    server.use(
      authHandler(),
      http.get('/api/todos', () => HttpResponse.json(paged(items))),
    );

    const { container } = renderWithProviders(<TodoListPage />, {
      route: '/todos',
    });

    expect(await screen.findByText('Active todo')).toBeInTheDocument();
    expect(screen.getByText('Completed todo')).toBeInTheDocument();
    expect(screen.getByText('Due today todo')).toBeInTheDocument();

    const results = await axe(container, {
      runOnly: {
        type: 'tag',
        values: ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'],
      },
      rules: {
        // axe's color-contrast implementation needs canvas APIs that jsdom does
        // not implement. Browser-level contrast checks belong in Playwright.
        'color-contrast': { enabled: false },
      },
    });

    expect(results).toHaveNoViolations();
  });

  it('Forms_HaveExplicitLabelForAssociations', () => {
    server.use(authHandler());
    const login = renderWithProviders(<LoginForm />, { route: '/login' });
    expectExplicitLabels(login.container);
    login.unmount();

    const register = renderWithProviders(<RegisterForm />, {
      route: '/register',
    });
    expectExplicitLabels(register.container);
    register.unmount();

    const todoForm = renderWithProviders(
      <TodoForm listParams={defaultTodoListParams} mode="create" />,
    );
    expectExplicitLabels(todoForm.container);
  });

  it('TodoListPage_KeyboardFlow_OpensEditAndEscReturnsFocus', async () => {
    const user = userEvent.setup();
    const item = todo({
      id: '22222222-2222-4222-8222-222222222204',
      title: 'Keyboard todo',
    });

    server.use(
      authHandler(),
      http.get('/api/todos', () => HttpResponse.json(paged([item]))),
      http.get('/api/todos/:id', () => HttpResponse.json(item)),
    );

    renderWithProviders(<TodoListPage />, { route: '/todos' });

    expect(await screen.findByText('Keyboard todo')).toBeInTheDocument();

    const search = screen.getByRole('searchbox', { name: /search/i });
    const allTab = screen.getByRole('tab', { name: 'All' });
    const activeTab = screen.getByRole('tab', { name: 'Open' });
    const completedTab = screen.getByRole('tab', { name: 'Done' });
    const dueTodayTab = screen.getByTestId('tab-duetoday');
    const sort = screen.getByLabelText(/sort/i);
    const sortDirection = screen.getByLabelText(/direction/i);
    const row = screen.getByRole('listitem');

    search.focus();
    expect(search).toHaveFocus();

    await user.tab();
    expect(allTab).toHaveFocus();
    await user.tab();
    expect(activeTab).toHaveFocus();
    await user.tab();
    expect(completedTab).toHaveFocus();
    await user.tab();
    expect(dueTodayTab).toHaveFocus();
    await user.tab();
    expect(sort).toHaveFocus();
    await user.tab();
    expect(sortDirection).toHaveFocus();

    row.focus();
    expect(row).toHaveFocus();
    await user.keyboard('{Enter}');

    const complete = await screen.findByRole('button', {
      name: /mark .*keyboard todo.* complete/i,
    });
    const edit = screen.getByRole('button', { name: `Edit todo ${item.title}` });
    const deleteButton = screen.getByRole('button', {
      name: `Delete todo ${item.title}`,
    });

    await user.tab();
    expect(complete).toHaveFocus();
    await user.tab();
    expect(edit).toHaveFocus();
    await user.tab();
    expect(deleteButton).toHaveFocus();
    await user.tab({ shift: true });
    expect(edit).toHaveFocus();

    await user.keyboard('{Enter}');

    expect(await screen.findByTestId('dialog-edit-todo')).toBeInTheDocument();

    await user.keyboard('{Escape}');

    await waitFor(() => expect(screen.getByTestId('dialog-edit-todo')).not.toHaveAttribute('open'));
    expect(edit).toHaveFocus();
  });

  it('TodoListPage_LoadingState_ShowsSkeletonWhileFetching', async () => {
    server.use(
      authHandler(),
      http.get('/api/todos', async () => {
        await delay(80);
        return HttpResponse.json(paged([]));
      }),
    );

    renderWithProviders(<TodoListPage />, { route: '/todos' });

    expect(screen.getByRole('status')).toHaveTextContent('Loading todos...');
    expect(await screen.findByText('NO TODOS YET')).toBeInTheDocument();
  });

  it('TodoListPage_EmptyState_ShowsMessageForEmptyPage', async () => {
    server.use(
      authHandler(),
      http.get('/api/todos', () => HttpResponse.json(paged([]))),
    );

    renderWithProviders(<TodoListPage />, { route: '/todos' });

    expect(await screen.findByText('NO TODOS YET')).toBeInTheDocument();
  });

  it('TodoListPage_ErrorState_RetryRefetches', async () => {
    const user = userEvent.setup();
    let listCalls = 0;

    server.use(
      authHandler(),
      http.get('/api/todos', () => {
        listCalls += 1;
        return HttpResponse.json(problemDetails({ status: 500 }), {
          status: 500,
        });
      }),
    );

    renderWithProviders(<TodoListPage />, { route: '/todos' });

    expect(
      await screen.findByRole('heading', { name: /could not load todos/i }),
    ).toBeInTheDocument();
    expect(listCalls).toBe(1);

    await user.click(screen.getByRole('button', { name: /retry/i }));

    await waitFor(() => expect(listCalls).toBe(2));
  });
});

const TEST_USER_ID = '11111111-1111-4111-8111-111111111111';

function authHandler() {
  return http.get('/api/auth/me', () =>
    HttpResponse.json({
      id: TEST_USER_ID,
      email: 'demo@example.com',
      role: 'Basic',
    }),
  );
}

function todo(overrides: Partial<TodoResponse> = {}): TodoResponse {
  return {
    id: '22222222-2222-4222-8222-222222222222',
    title: 'Write tests',
    description: 'Cover the brittle paths.',
    dueDate: '2026-05-07',
    priority: 'Medium',
    isCompleted: false,
    completedAt: null,
    tags: ['phase-6'],
    rowVersion: 1,
    createdAt: '2026-05-07T12:00:00Z',
    updatedAt: '2026-05-07T12:00:00Z',
    ...overrides,
  };
}

function paged(items: TodoResponse[]): PagedTodos {
  return {
    items,
    page: 1,
    pageSize: 20,
    total: items.length,
    hasNext: false,
  };
}

function problemDetails({ errors, status }: { errors?: Record<string, string[]>; status: number }) {
  return {
    type: 'about:blank',
    title: status === 409 ? 'Concurrency conflict.' : 'Request failed.',
    status,
    detail: 'Request failed.',
    instance: '/api/todos',
    traceId: 'trace-test',
    ...(errors ? { errors } : {}),
  };
}

function expectExplicitLabels(container: HTMLElement) {
  const controls = Array.from(
    container.querySelectorAll<HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement>(
      'input, select, textarea',
    ),
  );
  const labels = Array.from(container.querySelectorAll('label'));

  expect(controls.length).toBeGreaterThan(0);

  for (const control of controls) {
    const id = control.getAttribute('id');
    expect(id).toBeTruthy();

    const label = labels.find((candidate) => candidate.htmlFor === id);
    expect(label).toBeDefined();

    const labelText = label?.textContent?.trim();
    expect(labelText).toBeTruthy();
    expect(within(container).getByLabelText(labelText ?? '')).toBe(control);
  }
}
