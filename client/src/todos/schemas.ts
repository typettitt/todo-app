import { z } from 'zod';
import type { components } from '../lib/openapi-types';

export type Priority = Exclude<components['schemas']['Priority'], null>;
export type CreateTodoRequest = components['schemas']['CreateTodoRequest'];
export type UpdateTodoRequest = components['schemas']['UpdateTodoRequest'];
export type CompleteTodoRequest = components['schemas']['CompleteTodoRequest'];

export const PrioritySchema = z.enum(['Low', 'Medium', 'High']);

export const TodoResponseSchema = z.object({
  id: z.string().uuid(),
  title: z.string(),
  description: z.string().nullable(),
  dueDate: z
    .string()
    .regex(/^\d{4}-\d{2}-\d{2}$/)
    .nullable(),
  priority: PrioritySchema,
  isCompleted: z.boolean(),
  completedAt: z.string().nullable(),
  tags: z.array(z.string()),
  rowVersion: z.coerce.number(),
  createdAt: z.string(),
  updatedAt: z.string(),
});

export const PagedTodosSchema = z.object({
  items: z.array(TodoResponseSchema),
  page: z.number().int(),
  pageSize: z.number().int(),
  total: z.number().int(),
  hasNext: z.boolean(),
});

export type TodoResponse = z.infer<typeof TodoResponseSchema>;
export type PagedTodos = z.infer<typeof PagedTodosSchema>;
