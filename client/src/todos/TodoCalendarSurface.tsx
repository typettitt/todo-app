import { useMemo } from 'react';
import {
  addDays,
  formatDayLabel,
  formatRangeTitle,
  toIsoDate,
  weekdayLabel,
  type CalendarMode,
  type CalendarRange,
} from './dateHelpers';
import { LoadingRows } from './TodoListSurface';
import { priorityClass } from './todoDisplay';
import type { TodoResponse } from './schemas';
import styles from './Todos.module.css';

type CalendarSurfaceProps = {
  calendarCursor: Date;
  calendarMode: CalendarMode;
  items: TodoResponse[];
  onCreateForDay: (dueDate?: string) => void;
  onEdit: (todo: TodoResponse) => void;
  onMoveCursor: (date: Date) => void;
  onRefresh: () => void;
  onRetry: () => void;
  onSetMode: (mode: CalendarMode) => void;
  queryError: Error | null;
  range: CalendarRange;
  status: 'error' | 'pending' | 'success';
};

export function CalendarSurface({
  calendarCursor,
  calendarMode,
  items,
  onCreateForDay,
  onEdit,
  onMoveCursor,
  onRefresh,
  onRetry,
  onSetMode,
  queryError,
  range,
  status,
}: CalendarSurfaceProps) {
  const itemsByDay = useMemo(() => groupByDueDate(items), [items]);

  if (status === 'pending') {
    return <LoadingRows />;
  }

  if (status === 'error') {
    return (
      <div className={styles.errorState} role="alert">
        <h2>Could not load calendar</h2>
        <p>{queryError?.message ?? 'The API returned an error.'}</p>
        <button className={styles.ghostButton} onClick={onRetry} type="button">
          Retry
        </button>
      </div>
    );
  }

  return (
    <section className={styles.calendarPanel} aria-label="Todo calendar">
      <div className={styles.calendarToolbar}>
        <div>
          <p className={styles.eyebrow}>{calendarMode === 'week' ? 'Week View' : 'Month View'}</p>
          <h3>{formatRangeTitle(range, calendarMode)}</h3>
        </div>
        <div className={styles.calendarActions}>
          <div className={styles.viewTabs} role="tablist" aria-label="Calendar mode">
            <button
              aria-selected={calendarMode === 'week'}
              className={calendarMode === 'week' ? styles.active : undefined}
              onClick={() => onSetMode('week')}
              role="tab"
              type="button"
            >
              Week
            </button>
            <button
              aria-selected={calendarMode === 'month'}
              className={calendarMode === 'month' ? styles.active : undefined}
              onClick={() => onSetMode('month')}
              role="tab"
              type="button"
            >
              Month
            </button>
          </div>
          <button
            className={styles.microButton}
            onClick={() =>
              onMoveCursor(addDays(calendarCursor, calendarMode === 'week' ? -7 : -31))
            }
            type="button"
          >
            Prev
          </button>
          <button
            className={styles.microButton}
            onClick={() => onMoveCursor(new Date())}
            type="button"
          >
            Today
          </button>
          <button
            className={styles.microButton}
            onClick={() => onMoveCursor(addDays(calendarCursor, calendarMode === 'week' ? 7 : 31))}
            type="button"
          >
            Next
          </button>
          <button className={styles.microButton} onClick={onRefresh} type="button">
            Refresh
          </button>
        </div>
      </div>

      <div className={styles.calendarGrid} data-mode={calendarMode}>
        {range.days.map((day) => {
          const iso = toIsoDate(day);
          const dayItems = itemsByDay.get(iso) ?? [];
          const isOtherMonth =
            calendarMode === 'month' && day.getMonth() !== calendarCursor.getMonth();
          return (
            <section
              className={`${styles.calendarDay} ${isOtherMonth ? styles.otherMonth : ''}`}
              key={iso}
              aria-label={formatDayLabel(day)}
            >
              <button
                className={styles.calendarDayButton}
                onClick={() => onCreateForDay(iso)}
                type="button"
              >
                <span>{weekdayLabel(day)}</span>
                <strong>{day.getDate()}</strong>
              </button>
              <div className={styles.calendarItems}>
                {dayItems.slice(0, calendarMode === 'week' ? 8 : 4).map((todo) => (
                  <button
                    className={`${styles.calendarItem} ${todo.isCompleted ? styles.complete : ''}`}
                    key={todo.id}
                    onClick={() => onEdit(todo)}
                    type="button"
                  >
                    <span
                      className={`${styles.priorityDot} ${styles[priorityClass(todo.priority)]}`}
                    />
                    <span>{todo.title}</span>
                  </button>
                ))}
                {dayItems.length > (calendarMode === 'week' ? 8 : 4) ? (
                  <span className={styles.moreItems}>
                    +{dayItems.length - (calendarMode === 'week' ? 8 : 4)} more
                  </span>
                ) : null}
              </div>
            </section>
          );
        })}
      </div>
    </section>
  );
}

function groupByDueDate(items: TodoResponse[]) {
  const result = new Map<string, TodoResponse[]>();
  for (const item of items) {
    if (!item.dueDate) {
      continue;
    }

    const current = result.get(item.dueDate) ?? [];
    current.push(item);
    result.set(item.dueDate, current);
  }

  return result;
}
