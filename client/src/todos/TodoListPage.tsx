import { useEffect, useMemo, useRef, useState } from 'react';
import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useSearchParams } from 'react-router-dom';
import { useLogout, useMe } from '../auth/useAuth';
import ThemeToggle from '../components/ThemeToggle';
import { ApiProblem } from '../lib/api';
import { toast } from '../lib/toast';
import { completeTodo, deleteTodo, getTodo, listTodos, listTodoWindow } from './api';
import { getCalendarRange, localDate, toIsoDate, type CalendarMode } from './dateHelpers';
import { EditTodoDrawer } from './EditTodoDrawer';
import { CreateTodoDialog, DeleteTodoDialog } from './TodoDialogs';
import { TodoFilters, type StatusTabId, type ViewMode } from './TodoFilters';
import { CalendarSurface } from './TodoCalendarSurface';
import { ListSurface, StatsBar } from './TodoListSurface';
import { summarizeTodos } from './todoStats';
import {
  defaultTodoListParams,
  normalizeTodoListParams,
  todosKeys,
  type TodoListParams,
  type TodoSortBy,
  type TodoSortDir,
  type TodoStatusFilter,
} from './queryKeys';
import type { TodoResponse } from './schemas';
import styles from './Todos.module.css';

const PAGE_SIZE = 20;

export function TodoListPage() {
  const me = useMe();
  const userId = me.data?.id ?? '';
  const logout = useLogout();
  const queryClient = useQueryClient();
  const [searchParams, setSearchParams] = useSearchParams();
  const localToday = localDate();
  const urlParams = useMemo(() => readListParams(searchParams), [searchParams]);
  const view = readView(searchParams.get('view'));
  const todayParam = searchParams.get('today');
  const listParams = useMemo(
    () =>
      normalizeTodoListParams({
        ...urlParams,
        page: 1,
        pageSize: PAGE_SIZE,
        today: urlParams.status === 'DueToday' ? localToday : null,
      }),
    [localToday, urlParams],
  );
  const [searchDraftState, setSearchDraftState] = useState(() => ({
    source: listParams.search,
    value: listParams.search,
  }));
  const [creating, setCreating] = useState(false);
  const [createDueDate, setCreateDueDate] = useState<string | null>(null);
  const [editingTodo, setEditingTodo] = useState<TodoResponse | null>(null);
  const [deletingTodo, setDeletingTodo] = useState<TodoResponse | null>(null);
  const [selectedTodoId, setSelectedTodoId] = useState<string | null>(null);
  const [calendarMode, setCalendarMode] = useState<CalendarMode>('week');
  const [calendarCursor, setCalendarCursor] = useState(() => new Date());
  const loadMoreRef = useRef<HTMLDivElement | null>(null);
  const searchDraft =
    searchDraftState.source === listParams.search ? searchDraftState.value : listParams.search;
  const debouncedSearch = useDebouncedValue(searchDraft, 300);

  const todosQuery = useInfiniteQuery({
    enabled: userId !== '',
    queryFn: ({ pageParam }) =>
      listTodos({
        ...listParams,
        page: pageParam,
      }),
    queryKey: todosKeys.list(userId, listParams),
    initialPageParam: 1,
    getNextPageParam: (lastPage) => (lastPage.hasNext ? lastPage.page + 1 : undefined),
    retry: false,
  });

  const listPages = todosQuery.data?.pages ?? [];
  const todos = listPages.flatMap((page) => page.items);
  const total = listPages[0]?.total ?? 0;
  const stats = useMemo(() => summarizeTodos(todos, total), [todos, total]);
  const activeTabId: StatusTabId = listParams.status ?? 'all';
  const calendarRange = useMemo(
    () => getCalendarRange(calendarCursor, calendarMode),
    [calendarCursor, calendarMode],
  );
  const calendarParams = useMemo(
    () =>
      normalizeTodoListParams({
        ...listParams,
        dueFrom: toIsoDate(calendarRange.start),
        dueTo: toIsoDate(calendarRange.end),
        page: 1,
        pageSize: 100,
        sortBy: 'DueDate',
        sortDir: 'Asc',
      }),
    [calendarRange.end, calendarRange.start, listParams],
  );
  const calendarQuery = useQuery({
    enabled: view === 'calendar' && userId !== '',
    queryFn: () => listTodoWindow(calendarParams),
    queryKey: todosKeys.list(userId, calendarParams),
    retry: false,
  });

  const toggleMutation = useMutation({
    mutationFn: (todo: TodoResponse) =>
      completeTodo(todo.id, {
        isCompleted: !todo.isCompleted,
        rowVersion: todo.rowVersion,
      }),
    onError: (error, todo) => {
      if (error instanceof ApiProblem && error.status === 409) {
        void recoverTodoConflict(todo.id);
        return;
      }

      const message =
        error instanceof ApiProblem
          ? (error.problem.detail ?? error.problem.title)
          : 'Could not update todo.';
      toast.error(message);
    },
    onSuccess: (updatedTodo) => {
      queryClient.setQueryData(todosKeys.detail(userId, updatedTodo.id), updatedTodo);
      void queryClient.invalidateQueries({ queryKey: todosKeys.lists(userId) });
    },
  });

  const deleteMutation = useMutation({
    mutationFn: (todo: TodoResponse) => deleteTodo(todo.id),
    onError: (error, todo) => {
      if (error instanceof ApiProblem && error.status === 409) {
        void recoverTodoConflict(todo.id);
        return;
      }

      const message =
        error instanceof ApiProblem
          ? (error.problem.detail ?? error.problem.title)
          : 'Could not delete todo.';
      toast.error(message);
    },
    onSuccess: (_result, todo) => {
      if (selectedTodoId === todo.id) {
        setSelectedTodoId(null);
      }
      if (editingTodo?.id === todo.id) {
        setEditingTodo(null);
      }
      setDeletingTodo(null);
      queryClient.removeQueries({ exact: true, queryKey: todosKeys.detail(userId, todo.id) });
      void queryClient.invalidateQueries({ queryKey: todosKeys.lists(userId) });
    },
  });

  useEffect(() => {
    if (debouncedSearch === listParams.search) {
      return;
    }

    writeParams(searchParams, setSearchParams, localToday, {
      search: debouncedSearch,
    });
  }, [debouncedSearch, listParams.search, localToday, searchParams, setSearchParams]);

  useEffect(() => {
    if (listParams.status !== 'DueToday' || todayParam === localToday) {
      return;
    }

    writeParams(searchParams, setSearchParams, localToday, {
      status: 'DueToday',
      today: localToday,
    });
  }, [listParams.status, localToday, searchParams, setSearchParams, todayParam]);

  useEffect(() => {
    const node = loadMoreRef.current;
    if (
      !node ||
      view !== 'list' ||
      !todosQuery.hasNextPage ||
      todosQuery.isFetchingNextPage ||
      todosQuery.isPending ||
      todosQuery.isError
    ) {
      return;
    }

    const observer = new IntersectionObserver(
      (entries) => {
        if (entries.some((entry) => entry.isIntersecting)) {
          void todosQuery.fetchNextPage();
        }
      },
      { rootMargin: '360px 0px' },
    );

    observer.observe(node);
    return () => observer.disconnect();
  }, [
    todosQuery,
    todosQuery.hasNextPage,
    todosQuery.isError,
    todosQuery.isFetchingNextPage,
    todosQuery.isPending,
    view,
  ]);

  function updateFilters(changes: Partial<TodoListParams>) {
    writeParams(searchParams, setSearchParams, localToday, changes);
    setSelectedTodoId(null);
  }

  function updateSearchDraft(value: string) {
    setSearchDraftState({ source: listParams.search, value });
    setSelectedTodoId(null);
  }

  function updateView(nextView: ViewMode) {
    writeParams(searchParams, setSearchParams, localToday, {}, nextView);
    setSelectedTodoId(null);
  }

  function openCreate(dueDate?: string) {
    setCreateDueDate(dueDate ?? null);
    setCreating(true);
  }

  function closeCreate() {
    setCreating(false);
    setCreateDueDate(null);
  }

  function handleFormSuccess(todo: TodoResponse) {
    setSelectedTodoId(todo.id);
    closeCreate();
    setEditingTodo(null);
    void queryClient.invalidateQueries({ queryKey: todosKeys.lists(userId) });
  }

  function resetFilters() {
    writeParams(searchParams, setSearchParams, localToday, {
      search: '',
      sortBy: defaultTodoListParams.sortBy,
      sortDir: defaultTodoListParams.sortDir,
      status: null,
      today: null,
    });
    updateSearchDraft('');
    setSelectedTodoId(null);
  }

  async function recoverTodoConflict(todoId: string) {
    try {
      const fresh = await queryClient.fetchQuery({
        queryFn: () => getTodo(todoId),
        queryKey: todosKeys.detail(userId, todoId),
      });
      void queryClient.invalidateQueries({ queryKey: todosKeys.lists(userId) });
      toast.message(`"${fresh.title}" was refreshed. Review the latest version and try again.`);
    } catch {
      void queryClient.invalidateQueries({ queryKey: todosKeys.lists(userId) });
      toast.message('Todo changed elsewhere. The list was refreshed.');
    }
  }

  return (
    <main className={styles.appShell}>
      <header className={styles.shellHeader} aria-label="Primary">
        <div className={styles.brandLockup} aria-label="Todo operations console">
          <span className={styles.brandSigil} aria-hidden="true" />
          <div>
            <p className={styles.eyebrow}>Todo Operations</p>
            <h1 className={styles.brand}>Todo Console</h1>
          </div>
        </div>

        <div className={styles.headerActions}>
          {me.data ? (
            <span className={styles.userStrip} title={me.data.email}>
              <span className={styles.statusDot} aria-hidden="true" />
              <span className={styles.truncate}>{me.data.email}</span>
            </span>
          ) : null}
          <ThemeToggle />
          <button
            className={styles.dangerButton}
            disabled={logout.isPending}
            onClick={() => logout.mutate()}
            type="button"
          >
            Logout
          </button>
        </div>
      </header>

      <section className={styles.mainFrame} aria-labelledby="workspace-title">
        <div className={styles.workspaceTopline}>
          <div>
            <p className={styles.eyebrow}>Active Workspace</p>
            <h2 id="workspace-title">Tasks</h2>
          </div>
          <StatsBar
            stats={view === 'calendar' ? summarizeTodos(calendarQuery.data?.items ?? []) : stats}
          />
        </div>

        <section className={styles.recordsPanel} aria-label="Todo records">
          <TodoFilters
            activeTabId={activeTabId}
            listParams={listParams}
            localToday={localToday}
            onCreate={() => openCreate()}
            onFiltersChange={updateFilters}
            onReset={resetFilters}
            onSearchChange={updateSearchDraft}
            onViewChange={updateView}
            searchDraft={searchDraft}
            view={view}
          />

          {view === 'list' ? (
            <ListSurface
              error={todosQuery.error}
              hasNext={todosQuery.hasNextPage}
              isError={todosQuery.isError}
              isFetching={todosQuery.isFetching}
              isFetchingNextPage={todosQuery.isFetchingNextPage}
              isPending={todosQuery.isPending}
              loaded={todos.length}
              onDelete={setDeletingTodo}
              onEdit={setEditingTodo}
              onRefresh={() => void todosQuery.refetch()}
              onRetry={() => void todosQuery.refetch()}
              onSelect={setSelectedTodoId}
              onToggle={(todo) => toggleMutation.mutate(todo)}
              selectedTodoId={selectedTodoId}
              sentinelRef={loadMoreRef}
              todos={todos}
              togglePendingId={toggleMutation.isPending ? toggleMutation.variables?.id : undefined}
              total={total}
            />
          ) : (
            <CalendarSurface
              calendarCursor={calendarCursor}
              calendarMode={calendarMode}
              items={calendarQuery.data?.items ?? []}
              onCreateForDay={openCreate}
              onEdit={setEditingTodo}
              onMoveCursor={setCalendarCursor}
              onRefresh={() => void calendarQuery.refetch()}
              onRetry={() => void calendarQuery.refetch()}
              onSetMode={setCalendarMode}
              queryError={calendarQuery.error}
              range={calendarRange}
              status={calendarQuery.status}
            />
          )}
        </section>

        <CreateTodoDialog
          initialDueDate={createDueDate}
          listParams={listParams}
          onClose={closeCreate}
          onSuccess={handleFormSuccess}
          open={creating}
        />

        <EditTodoDrawer
          listParams={listParams}
          onClose={() => setEditingTodo(null)}
          todo={editingTodo}
        />

        <DeleteTodoDialog
          isError={deleteMutation.isError}
          isPending={deleteMutation.isPending}
          onClose={() => setDeletingTodo(null)}
          onConfirm={(todo) => deleteMutation.mutate(todo)}
          todo={deletingTodo}
        />
      </section>
    </main>
  );
}

function readListParams(searchParams: URLSearchParams): TodoListParams {
  return normalizeTodoListParams({
    dueFrom: null,
    dueTo: null,
    page: 1,
    pageSize: PAGE_SIZE,
    search: searchParams.get('search') ?? '',
    sortBy: readSortBy(searchParams.get('sortBy')),
    sortDir: readSortDir(searchParams.get('sortDir')),
    status: readStatus(searchParams.get('status')),
    today: searchParams.get('today'),
  });
}

function writeParams(
  current: URLSearchParams,
  setSearchParams: ReturnType<typeof useSearchParams>[1],
  localToday: string,
  changes: Partial<TodoListParams>,
  view?: ViewMode,
) {
  const currentView = readView(current.get('view'));
  const currentValues = readListParams(current);
  const nextValues = normalizeTodoListParams({
    ...currentValues,
    ...changes,
    page: 1,
    pageSize: PAGE_SIZE,
    today:
      changes.status === 'DueToday' ||
      (changes.status === undefined && currentValues.status === 'DueToday')
        ? (changes.today ?? localToday)
        : null,
  });
  const next = new URLSearchParams();
  const nextView = view ?? currentView;

  if (nextView === 'calendar') {
    next.set('view', 'calendar');
  }

  if (nextValues.status) {
    next.set('status', nextValues.status);
  }

  if (nextValues.status === 'DueToday' && nextValues.today) {
    next.set('today', nextValues.today);
  }

  if (nextValues.search) {
    next.set('search', nextValues.search);
  }

  if (nextValues.sortBy !== defaultTodoListParams.sortBy) {
    next.set('sortBy', nextValues.sortBy);
  }

  if (nextValues.sortDir !== defaultTodoListParams.sortDir) {
    next.set('sortDir', nextValues.sortDir);
  }

  setSearchParams(next, { replace: true });
}

function readView(value: string | null): ViewMode {
  return value === 'calendar' ? 'calendar' : 'list';
}

function readStatus(value: string | null): TodoStatusFilter {
  return value === 'Active' || value === 'Completed' || value === 'DueToday' ? value : null;
}

function readSortBy(value: string | null): TodoSortBy {
  return value === 'DueDate' || value === 'Priority' || value === 'Title' ? value : 'CreatedAt';
}

function readSortDir(value: string | null): TodoSortDir {
  return value === 'Asc' ? 'Asc' : 'Desc';
}

function useDebouncedValue<T>(value: T, delayMs: number) {
  const [debounced, setDebounced] = useState(value);

  useEffect(() => {
    const timeout = window.setTimeout(() => setDebounced(value), delayMs);
    return () => window.clearTimeout(timeout);
  }, [delayMs, value]);

  return debounced;
}
