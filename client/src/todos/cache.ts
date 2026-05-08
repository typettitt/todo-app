import type { QueryClient } from '@tanstack/react-query';
import { todosKeys, type TodoListParams } from './queryKeys';
import type { PagedTodos, TodoResponse } from './schemas';

type TodoListCache = PagedTodos | { pages: PagedTodos[]; pageParams: unknown[] };

export function replaceTodoInListCache(
  queryClient: QueryClient,
  userId: string,
  listParams: Partial<TodoListParams>,
  todo: TodoResponse,
) {
  queryClient.setQueryData<TodoListCache>(todosKeys.list(userId, listParams), (previous) => {
    if (!previous) {
      return previous;
    }

    if (isInfiniteTodoCache(previous)) {
      return {
        ...previous,
        pages: previous.pages.map((page) => replaceTodoInPage(page, todo)),
      };
    }

    return replaceTodoInPage(previous, todo);
  });
  queryClient.setQueryData(todosKeys.detail(userId, todo.id), todo);
}

export function removeTodoFromListCache(
  queryClient: QueryClient,
  userId: string,
  listParams: Partial<TodoListParams>,
  id: string,
) {
  queryClient.setQueryData<TodoListCache>(todosKeys.list(userId, listParams), (previous) => {
    if (!previous) {
      return previous;
    }

    if (isInfiniteTodoCache(previous)) {
      return {
        ...previous,
        pages: previous.pages.map((page) => removeTodoFromPage(page, id)),
      };
    }

    return removeTodoFromPage(previous, id);
  });
  queryClient.removeQueries({ queryKey: todosKeys.detail(userId, id), exact: true });
}

function replaceTodoInPage(page: PagedTodos, todo: TodoResponse): PagedTodos {
  return {
    ...page,
    items: page.items.map((item) => (item.id === todo.id ? todo : item)),
  };
}

function removeTodoFromPage(page: PagedTodos, id: string): PagedTodos {
  return {
    ...page,
    items: page.items.filter((item) => item.id !== id),
    total: Math.max(0, page.total - 1),
  };
}

function isInfiniteTodoCache(
  value: TodoListCache,
): value is { pages: PagedTodos[]; pageParams: unknown[] } {
  return 'pages' in value && Array.isArray(value.pages);
}

/*
 * The old one-page cache shape is still used by component-level unit tests and
 * by a few narrow primitives. The page-level UI now uses useInfiniteQuery, so
 * the helpers above handle both shapes without corrupting the active cache.
 */
