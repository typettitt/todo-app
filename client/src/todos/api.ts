import { z } from 'zod';
import { apiRequest } from '../lib/api';
import { normalizeTodoListParams, type TodoListParams } from './queryKeys';
import {
  PagedTodosSchema,
  TodoResponseSchema,
  type CompleteTodoRequest,
  type CreateTodoRequest,
  type TodoResponse,
  type UpdateTodoRequest,
} from './schemas';

export function listTodos(params: Partial<TodoListParams>) {
  const normalized = normalizeTodoListParams(params);
  const query = new URLSearchParams();

  if (normalized.status) {
    query.set('status', normalized.status);
  }

  if (normalized.search) {
    query.set('q', normalized.search);
  }

  query.set('sortBy', normalized.sortBy);
  query.set('sortDir', normalized.sortDir);
  query.set('page', String(normalized.page));
  query.set('pageSize', String(normalized.pageSize));

  if (normalized.today) {
    query.set('today', normalized.today);
  }

  if (normalized.dueFrom) {
    query.set('dueFrom', normalized.dueFrom);
  }

  if (normalized.dueTo) {
    query.set('dueTo', normalized.dueTo);
  }

  return apiRequest(`/api/todos?${query.toString()}`, {
    method: 'GET',
    successSchema: PagedTodosSchema,
  });
}

export async function listTodoWindow(params: Partial<TodoListParams>) {
  const windowParams = normalizeTodoListParams({
    ...params,
    page: 1,
    pageSize: 100,
  });
  const items: TodoResponse[] = [];
  let page = 1;
  let total = 0;
  let hasNext = true;

  while (hasNext) {
    const result = await listTodos({
      ...windowParams,
      page,
      pageSize: 100,
    });
    items.push(...result.items);
    total = result.total;
    hasNext = result.hasNext;
    page += 1;
  }

  return {
    hasNext: false,
    items,
    page: 1,
    pageSize: 100,
    total,
  };
}

export function getTodo(id: string) {
  return apiRequest(`/api/todos/${id}`, {
    method: 'GET',
    successSchema: TodoResponseSchema,
  });
}

export function createTodo(request: CreateTodoRequest) {
  return apiRequest('/api/todos', {
    body: request,
    method: 'POST',
    successSchema: TodoResponseSchema,
  });
}

export function updateTodo(id: string, request: UpdateTodoRequest) {
  return apiRequest(`/api/todos/${id}`, {
    body: request,
    method: 'PUT',
    successSchema: TodoResponseSchema,
  });
}

export function completeTodo(id: string, request: CompleteTodoRequest) {
  return apiRequest(`/api/todos/${id}/complete`, {
    body: request,
    method: 'PATCH',
    successSchema: TodoResponseSchema,
  });
}

export function deleteTodo(id: string) {
  return apiRequest(`/api/todos/${id}`, {
    method: 'DELETE',
    successSchema: z.null(),
  });
}
