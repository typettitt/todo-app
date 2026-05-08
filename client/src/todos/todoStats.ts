import type { TodoResponse } from './schemas';

export type TodoStats = {
  completed: number;
  due: number;
  open: number;
  total: number;
  visible: number;
};

export function summarizeTodos(items: TodoResponse[], total = items.length): TodoStats {
  return {
    completed: items.filter((todo) => todo.isCompleted).length,
    due: items.filter((todo) => Boolean(todo.dueDate && !todo.isCompleted)).length,
    open: items.filter((todo) => !todo.isCompleted).length,
    total,
    visible: items.length,
  };
}
