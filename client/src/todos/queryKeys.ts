export type TodoStatusFilter = 'Active' | 'Completed' | 'DueToday' | null;
export type TodoSortBy = 'CreatedAt' | 'DueDate' | 'Priority' | 'Title';
export type TodoSortDir = 'Asc' | 'Desc';
export type TodoPageSize = 20 | 50 | 100;

export type TodoListParams = {
  dueFrom: string | null;
  dueTo: string | null;
  status: TodoStatusFilter;
  search: string;
  sortBy: TodoSortBy;
  sortDir: TodoSortDir;
  page: number;
  pageSize: TodoPageSize;
  today: string | null;
};

export const defaultTodoListParams: TodoListParams = {
  dueFrom: null,
  dueTo: null,
  status: null,
  search: '',
  sortBy: 'CreatedAt',
  sortDir: 'Desc',
  page: 1,
  pageSize: 20,
  today: null,
};

export function normalizeTodoListParams(params: Partial<TodoListParams>): TodoListParams {
  const status = normalizeStatus(params.status);
  const pageSize = normalizePageSize(params.pageSize);

  return {
    dueFrom: normalizeDateParam(params.dueFrom),
    dueTo: normalizeDateParam(params.dueTo),
    status,
    search: (params.search ?? '').trim(),
    sortBy: normalizeSortBy(params.sortBy),
    sortDir: normalizeSortDir(params.sortDir),
    page: normalizePage(params.page),
    pageSize,
    today: status === 'DueToday' ? (params.today ?? null) : null,
  };
}

// User-scoped private cache namespace. Every todo query lives under
// `['private', userId, 'todos', ...]` so logout/login/register can drop the
// previous owner's cache by removing the `['private']` prefix wholesale. See
// `useAuthBoundary.clearPrivateData` and `docs/decisions.md`.
export const todosKeys = {
  all: (userId: string) => ['private', userId, 'todos'] as const,
  detail: (userId: string, id: string) => [...todosKeys.all(userId), 'detail', id] as const,
  lists: (userId: string) => [...todosKeys.all(userId), 'list'] as const,
  list: (userId: string, params: Partial<TodoListParams>) =>
    [...todosKeys.lists(userId), normalizeTodoListParams(params)] as const,
};

function normalizeStatus(value: TodoListParams['status'] | undefined) {
  return value === 'Active' || value === 'Completed' || value === 'DueToday' ? value : null;
}

function normalizeSortBy(value: TodoSortBy | undefined): TodoSortBy {
  return value === 'DueDate' || value === 'Priority' || value === 'Title' ? value : 'CreatedAt';
}

function normalizeSortDir(value: TodoSortDir | undefined): TodoSortDir {
  return value === 'Asc' ? 'Asc' : 'Desc';
}

function normalizePage(value: number | undefined) {
  if (!value || !Number.isFinite(value) || value < 1) {
    return 1;
  }

  return Math.trunc(value);
}

function normalizePageSize(value: TodoPageSize | undefined): TodoPageSize {
  return value === 50 || value === 100 ? value : 20;
}

function normalizeDateParam(value: string | null | undefined) {
  return value && /^\d{4}-\d{2}-\d{2}$/.test(value) ? value : null;
}
