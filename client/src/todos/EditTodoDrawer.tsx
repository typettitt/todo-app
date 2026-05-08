import { useQuery } from '@tanstack/react-query';
import { useMe } from '../auth/useAuth';
import Dialog from '../components/ui/Dialog';
import { getTodo } from './api';
import { TodoForm } from './TodoForm';
import { todosKeys, type TodoListParams } from './queryKeys';
import type { TodoResponse } from './schemas';
import styles from './Todos.module.css';

type EditTodoDrawerProps = {
  listParams: Partial<TodoListParams>;
  onClose: () => void;
  todo: TodoResponse | null;
};

export function EditTodoDrawer({ listParams, onClose, todo }: EditTodoDrawerProps) {
  const open = todo !== null;
  const me = useMe();
  const userId = me.data?.id ?? '';
  const detailQuery = useQuery({
    enabled: open && userId !== '',
    queryFn: () => getTodo(requireTodo(todo).id),
    queryKey: todosKeys.detail(userId, todo?.id ?? ''),
    retry: false,
  });

  return (
    <Dialog
      data-testid="dialog-edit-todo"
      onClose={onClose}
      open={open}
      title={todo?.title ?? 'Edit Todo'}
    >
      {open && detailQuery.isPending ? (
        <div className={styles.skeleton} role="status">
          Loading todo...
        </div>
      ) : null}

      {open && detailQuery.isError ? (
        <div className={styles.errorState} role="alert">
          <p>Could not load todo.</p>
          <button onClick={() => detailQuery.refetch()} type="button">
            Retry
          </button>
        </div>
      ) : null}

      {open && detailQuery.data ? (
        <TodoForm
          initialTodo={detailQuery.data}
          heading={null}
          listParams={listParams}
          mode="edit"
          onCancel={onClose}
          onReload={async () => {
            const result = await detailQuery.refetch();
            if (!result.data) {
              throw new Error('Could not reload todo.');
            }

            return result.data;
          }}
          onSuccess={onClose}
        />
      ) : null}
    </Dialog>
  );
}

function requireTodo(todo: TodoResponse | null) {
  if (!todo) {
    throw new Error('Todo is required.');
  }

  return todo;
}
