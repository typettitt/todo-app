import Dialog from '../components/ui/Dialog';
import { TodoForm } from './TodoForm';
import type { TodoListParams } from './queryKeys';
import type { TodoResponse } from './schemas';
import styles from './Todos.module.css';

type CreateTodoDialogProps = {
  initialDueDate: string | null;
  listParams: Partial<TodoListParams>;
  onClose: () => void;
  onSuccess: (todo: TodoResponse) => void;
  open: boolean;
};

export function CreateTodoDialog({
  initialDueDate,
  listParams,
  onClose,
  onSuccess,
  open,
}: CreateTodoDialogProps) {
  return (
    <Dialog data-testid="dialog-new-todo" onClose={onClose} open={open} title="Create Task">
      {open ? (
        <TodoForm
          heading={null}
          initialDueDate={initialDueDate}
          listParams={listParams}
          mode="create"
          onCancel={onClose}
          onSuccess={onSuccess}
        />
      ) : null}
    </Dialog>
  );
}

type DeleteTodoDialogProps = {
  isError: boolean;
  isPending: boolean;
  onClose: () => void;
  onConfirm: (todo: TodoResponse) => void;
  todo: TodoResponse | null;
};

export function DeleteTodoDialog({
  isError,
  isPending,
  onClose,
  onConfirm,
  todo,
}: DeleteTodoDialogProps) {
  return (
    <Dialog
      actions={
        todo ? (
          <>
            <button
              className={styles.dangerButton}
              data-testid="todo-delete-confirm"
              disabled={isPending}
              onClick={() => onConfirm(todo)}
              type="button"
            >
              Delete Todo
            </button>
            <button
              className={styles.ghostButton}
              disabled={isPending}
              onClick={onClose}
              type="button"
            >
              Cancel
            </button>
          </>
        ) : null
      }
      data-testid="dialog-delete-todo"
      description="This will permanently remove the selected todo."
      onClose={onClose}
      open={todo !== null}
      title={todo?.title ?? 'Delete Todo'}
    >
      {todo && isError ? (
        <div className={styles.errorBlock} role="alert">
          <strong>Could not delete todo.</strong>
        </div>
      ) : null}
    </Dialog>
  );
}
