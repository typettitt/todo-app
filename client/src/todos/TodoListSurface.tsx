import type { RefObject } from 'react';
import { formatDate, formatTimestamp, isOverdue } from './dateHelpers';
import { priorityClass } from './todoDisplay';
import type { TodoStats } from './todoStats';
import type { TodoResponse } from './schemas';
import styles from './Todos.module.css';

type ListSurfaceProps = {
  error: Error | null;
  hasNext: boolean;
  isError: boolean;
  isFetching: boolean;
  isFetchingNextPage: boolean;
  isPending: boolean;
  loaded: number;
  onDelete: (todo: TodoResponse) => void;
  onEdit: (todo: TodoResponse) => void;
  onRefresh: () => void;
  onRetry: () => void;
  onSelect: (id: string) => void;
  onToggle: (todo: TodoResponse) => void;
  selectedTodoId: string | null;
  sentinelRef: RefObject<HTMLDivElement | null>;
  todos: TodoResponse[];
  togglePendingId?: string;
  total: number;
};

export function ListSurface({
  error,
  hasNext,
  isError,
  isFetching,
  isFetchingNextPage,
  isPending,
  loaded,
  onDelete,
  onEdit,
  onRefresh,
  onRetry,
  onSelect,
  onToggle,
  selectedTodoId,
  sentinelRef,
  todos,
  togglePendingId,
  total,
}: ListSurfaceProps) {
  if (isPending) {
    return <LoadingRows />;
  }

  if (isError) {
    return (
      <div className={styles.errorState} role="alert">
        <h2>Could not load todos</h2>
        <p>{error?.message ?? 'The API returned an error.'}</p>
        <button className={styles.ghostButton} onClick={onRetry} type="button">
          Retry
        </button>
      </div>
    );
  }

  return (
    <>
      <div className={styles.tableToolbar}>
        <span className={styles.pageReadout}>
          Loaded {loaded} / {total}
        </span>
        <button
          className={styles.microButton}
          disabled={isFetching}
          onClick={onRefresh}
          type="button"
        >
          Refresh
        </button>
      </div>

      {todos.length === 0 ? <EmptyState /> : null}

      {todos.length > 0 ? (
        <div className={styles.recordList} role="list" aria-label="Todo list">
          {todos.map((todo) => (
            <TodoRow
              busy={togglePendingId === todo.id}
              key={todo.id}
              onDelete={() => onDelete(todo)}
              onEdit={() => onEdit(todo)}
              onSelect={() => onSelect(todo.id)}
              onToggle={() => onToggle(todo)}
              selected={selectedTodoId === todo.id}
              todo={todo}
            />
          ))}
        </div>
      ) : null}

      {todos.length > 0 ? (
        <div className={styles.scrollSentinel} ref={sentinelRef} aria-live="polite">
          {isFetchingNextPage ? 'Loading more' : hasNext ? 'Scroll to load more' : 'End of list'}
        </div>
      ) : null}
    </>
  );
}

function TodoRow({
  busy,
  onDelete,
  onEdit,
  onSelect,
  onToggle,
  selected,
  todo,
}: {
  busy: boolean;
  onDelete: () => void;
  onEdit: () => void;
  onSelect: () => void;
  onToggle: () => void;
  selected: boolean;
  todo: TodoResponse;
}) {
  return (
    <article
      className={`${styles.todoRow} ${styles[priorityClass(todo.priority)]} ${
        todo.isCompleted ? styles.complete : ''
      } ${selected ? styles.selected : ''}`}
      onClick={onSelect}
      onKeyDown={(event) => {
        if (event.target !== event.currentTarget) {
          return;
        }

        if (event.key === 'Enter' || event.key === ' ') {
          event.preventDefault();
          onSelect();
        }
      }}
      role="listitem"
      tabIndex={0}
    >
      <div className={styles.rowStatusLine} aria-hidden="true" />
      <div className={styles.rowMain}>
        <div className={styles.rowTitleline}>
          <h4>{todo.title}</h4>
          <span className={`${styles.statusBadge} ${todo.isCompleted ? styles.done : styles.open}`}>
            {todo.isCompleted ? 'Complete' : 'Open'}
          </span>
          <span className={`${styles.priorityBadge} ${styles[priorityClass(todo.priority)]}`}>
            {todo.priority}
          </span>
        </div>
        {todo.description ? (
          <p className={styles.rowDescription}>{todo.description}</p>
        ) : (
          <p className={`${styles.rowDescription} ${styles.muted}`}>No description supplied.</p>
        )}
        <div className={styles.dateStrip} aria-label="Todo dates">
          <DateChip
            label="Due"
            tone={isOverdue(todo) ? 'danger' : todo.dueDate ? 'warn' : 'muted'}
            value={formatDate(todo.dueDate)}
          />
          <DateChip label="Created" value={formatTimestamp(todo.createdAt)} />
          <DateChip label="Modified" value={formatTimestamp(todo.updatedAt)} />
          {todo.completedAt ? (
            <DateChip label="Completed" tone="success" value={formatTimestamp(todo.completedAt)} />
          ) : null}
        </div>
        {todo.tags.length > 0 ? (
          <div className={styles.tagStrip} aria-label="Tags">
            {todo.tags.map((tag) => (
              <span className={styles.tagToken} key={tag}>
                #{tag}
              </span>
            ))}
          </div>
        ) : null}
      </div>
      {selected ? (
        <div className={styles.rowActions}>
          <button
            aria-label={
              todo.isCompleted ? `Mark "${todo.title}" active` : `Mark "${todo.title}" complete`
            }
            className={styles.microButton}
            disabled={busy}
            onClick={(event) => {
              event.stopPropagation();
              onToggle();
            }}
            type="button"
          >
            {todo.isCompleted ? 'Open' : 'Done'}
          </button>
          <button
            aria-label={`Edit todo ${todo.title}`}
            className={styles.microButton}
            disabled={busy}
            onClick={(event) => {
              event.stopPropagation();
              onEdit();
            }}
            type="button"
          >
            Edit
          </button>
          <button
            aria-label={`Delete todo ${todo.title}`}
            className={`${styles.microButton} ${styles.danger}`}
            disabled={busy}
            onClick={(event) => {
              event.stopPropagation();
              onDelete();
            }}
            type="button"
          >
            Delete
          </button>
        </div>
      ) : null}
    </article>
  );
}

function DateChip({
  label,
  tone = 'muted',
  value,
}: {
  label: string;
  tone?: 'danger' | 'muted' | 'success' | 'warn';
  value: string;
}) {
  return (
    <span className={`${styles.dateChip} ${styles[tone]}`}>
      <span>{label}</span>
      <strong>{value}</strong>
    </span>
  );
}

export function LoadingRows() {
  return (
    <div className={styles.loadingRows} role="status" aria-busy="true">
      <span className={styles.srOnly}>Loading todos...</span>
      {Array.from({ length: 6 }, (_, index) => (
        <div className={styles.skeletonRow} key={index}>
          <span />
          <span />
          <span />
        </div>
      ))}
    </div>
  );
}

export function StatsBar({ stats }: { stats: TodoStats }) {
  return (
    <dl className={styles.statsBar} aria-label="Visible todo statistics">
      <div>
        <dt>Visible</dt>
        <dd>{stats.visible}</dd>
      </div>
      <div>
        <dt>Open</dt>
        <dd>{stats.open}</dd>
      </div>
      <div>
        <dt>Done</dt>
        <dd>{stats.completed}</dd>
      </div>
      <div>
        <dt>Due</dt>
        <dd>{stats.due}</dd>
      </div>
      <div>
        <dt>Total</dt>
        <dd>{stats.total}</dd>
      </div>
    </dl>
  );
}

function EmptyState() {
  return (
    <div className={styles.emptyState}>
      <span className={`${styles.method} ${styles.get}`}>GET</span>
      <h3>NO TODOS YET</h3>
      <p>No matching records. Adjust filters or create a new todo.</p>
    </div>
  );
}
